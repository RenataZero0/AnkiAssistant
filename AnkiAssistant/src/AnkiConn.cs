using System;
using System.Collections.Generic;
using System.IO;

namespace AnkiAssistant
{
    /// <summary>
    /// 收藏库访问层 —— 内嵌官方 rslib 引擎（`rslib_aa.dll`，就在 exe 旁）。
    ///
    /// 本程序有**自己的一份收藏库**，不依赖任何外部桌面端，也不需要任何插件：
    ///   · 收藏库文件  &lt;数据目录&gt;\anki\collection.anki2
    ///   · 媒体文件夹  &lt;数据目录&gt;\anki\collection.media
    ///   · 媒体库文件  &lt;数据目录&gt;\anki\collection.media.db2
    /// 其中「数据目录」默认是 `%APPDATA%\AnkiAssistant\data`（见 <see cref="Store.DataDir"/>）。
    /// **绝不会碰**用户在 `%APPDATA%\Anki2\` 下的真实收藏库。
    ///
    /// 这一层只做三件事：
    ///   1. 路径与生命周期（<see cref="Open"/> / <see cref="Close"/> / <see cref="Opened"/>）；
    ///   2. 把 <see cref="EngineException"/> 翻成人能看懂的中文；
    ///   3. 把旧的「按字段名读写」的调用习惯，翻译成引擎的「按字段顺序下标读写」。
    ///
    /// 真正的 protobuf 组包/解包全在 <see cref="EngineRpc"/> 里，这里不自己拼报文。
    /// </summary>
    public static class AnkiConn
    {
        /// <summary>诊断用：最近一次操作/错误的简述（旧版存的是 HTTP 报文）。</summary>
        public static string LastPayload = "";

        /// <summary>
        /// 测试钩子：非空时收藏库放在这个目录下的 `anki\` 里。
        /// 生产代码永远留空（此时用 <see cref="Store.DataDir"/>）；探针用它指向临时目录。
        /// </summary>
        public static string DataDirOverride = "";

        /// <summary>媒体回收站方法的 RPC 编号（media.proto `rpc TrashMediaFiles`；svc 41 = EngineMethods.Media）。</summary>
        const int MediaTrashFiles = 4;

        public class AnkiException : Exception
        {
            public AnkiException(string msg) : base(msg) { }
        }

        // ===== 路径 =====

        static string BaseDir()
        {
            return string.IsNullOrEmpty(DataDirOverride) ? Store.DataDir : DataDirOverride;
        }

        /// <summary>`&lt;数据目录&gt;\anki`。</summary>
        public static string AnkiDir() { return Path.Combine(BaseDir(), "anki"); }

        /// <summary>收藏库文件全路径。</summary>
        public static string CollectionPath() { return Path.Combine(AnkiDir(), "collection.anki2"); }

        /// <summary>媒体文件夹全路径。</summary>
        public static string MediaFolder() { return Path.Combine(AnkiDir(), "collection.media"); }

        /// <summary>媒体库（媒体索引 sqlite）全路径。</summary>
        public static string MediaDbPath() { return Path.Combine(AnkiDir(), "collection.media.db2"); }

        // ===== 生命周期 =====

        static readonly object _openLock = new object();

        /// <summary>收藏库当前是否已经打开。</summary>
        public static bool Opened { get { return Engine.Opened; } }

        /// <summary>
        /// 打开本程序自己的收藏库（目录不存在就建）。已经打开时什么都不做。
        ///
        /// 引擎自己会做「同一份收藏库只能被一个进程打开」的保护，第二个进程打开会干净地
        /// 报 `Anki already open, or media currently syncing.` —— 这里把那条翻成
        /// 「收藏库正被占用……」而不是说成数据库损坏。
        /// </summary>
        public static void Open()
        {
            lock (_openLock)
            {
                if (Engine.Opened) return;
                if (!Engine.Available) throw new AnkiException(UnavailableText());

                string dir = AnkiDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string media = MediaFolder();
                if (!Directory.Exists(media)) Directory.CreateDirectory(media);

                LastPayload = "open: " + CollectionPath();
                try
                {
                    EngineRpc.OpenCollection(CollectionPath(), media, MediaDbPath());
                }
                catch (EngineException e)
                {
                    throw Wrap(e);
                }
            }
        }

        /// <summary>
        /// 关闭收藏库。**程序退出前必须调用**（窗体关闭时接线），否则下次启动会撞上
        /// 「收藏库正被占用」。引擎没打开时是安全的空操作。
        /// </summary>
        public static void Close()
        {
            lock (_openLock)
            {
                LastPayload = "close";
                try
                {
                    if (Engine.Opened) EngineRpc.CloseCollection();
                }
                catch (EngineException) { }
                catch (Exception) { }
            }
        }

        /// <summary>没打开就打开（所有公开方法开头都会走一遍）。</summary>
        public static void EnsureOpen()
        {
            if (!Engine.Opened) Open();
        }

        // ===== 错误翻译 =====

        static string UnavailableText()
        {
            string r = Engine.UnavailableReason;
            if (string.IsNullOrEmpty(r)) r = "找不到或无法加载引擎 DLL";
            return "内嵌引擎不可用：" + r + "（应该是安装包里的 rslib_aa.dll 缺失或被安全软件拦了）";
        }

        static AnkiException Wrap(EngineException e)
        {
            LastPayload = "error: " + e.Message;
            return new AnkiException(Explain(e));
        }

        /// <summary>把引擎的原始报错翻成中文；已经能看懂的原样返回。</summary>
        static string Explain(EngineException e)
        {
            string m = e.Message == null ? "" : e.Message;
            if (m.IndexOf("already open", StringComparison.OrdinalIgnoreCase) >= 0)
                return "收藏库正被占用：可能是上一个 AnkiAssistant 还没退干净，或者同一份收藏库正被别的进程打开。" +
                       "\n\n关掉它（或重启一次本程序）再试。这不是数据损坏，不要反复重试。";
            if (m.IndexOf("media currently syncing", StringComparison.OrdinalIgnoreCase) >= 0)
                return "收藏库正忙（媒体正在同步）：等同步结束再试，不要重复点。";
            if (m.IndexOf("inconsistent state", StringComparison.OrdinalIgnoreCase) >= 0)
                return "引擎认为这份收藏库进入了不一致状态：" + m +
                       "\n\n请关掉本程序再重新打开；如果反复出现，先把收藏库文件备份出来再看看。";
            if (m.IndexOf("no such file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return m + "\n\n找不到对应的收藏库/文件，检查一下数据目录是否被清理过。";
            return m;
        }

        // ===== 基本查询 =====

        /// <summary>
        /// 探活：引擎能用、且收藏库能打开时返回 true，不抛异常（旧版语义也是「能用吗」）。
        /// </summary>
        public static bool Ping()
        {
            try { Version(); return true; }
            catch { return false; }
        }

        /// <summary>
        /// 引擎版本号（整数）。旧接口返回的是远端服务版本，这里返回 rslib 的主版本号
        /// （例如 `rslib anki 26.09.3` → 26）；可用时不抛异常，不可用/开不了库才抛。
        /// </summary>
        public static int Version()
        {
            if (!Engine.Available) throw new AnkiException(UnavailableText());
            EnsureOpen();
            string v = Engine.Version();
            int n = FirstIntAfter(v, "anki ");
            return n > 0 ? n : 1;
        }

        /// <summary>完整版本文本（诊断/关于页用）。引擎不可用时返回空串。</summary>
        public static string EngineVersionText()
        {
            try { return Engine.Version(); }
            catch { return ""; }
        }

        static int FirstIntAfter(string s, string marker)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int i = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            i += marker.Length;
            int j = i;
            while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++;
            if (j == i) return 0;
            int n;
            return int.TryParse(s.Substring(i, j - i), out n) ? n : 0;
        }

        /// <summary>列出所有牌组名。</summary>
        public static List<string> DeckNames()
        {
            EnsureOpen();
            LastPayload = "deckNames";
            try
            {
                List<AaNameId> list = EngineRpc.DeckNames();
                List<string> names = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++) names.Add(list[i].Name);
                return names;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>
        /// 把引擎的英文/技术报错翻成能看懂的中文（保留旧方法名，界面在用）。没命中就原样返回。
        /// </summary>
        public static string Humanize(string err)
        {
            if (string.IsNullOrEmpty(err)) return err;
            string e = err.Trim();
            if (e.IndexOf("already open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.IndexOf("被占用", StringComparison.Ordinal) >= 0)
                return "收藏库正被占用：" + err +
                       "\n\n可能是上一个 AnkiAssistant 还没退干净，或者同一份收藏库正被别的进程打开。" +
                       "\n关掉它（或重启本程序）再试，不要反复点。这不是数据损坏。";
            if (e.IndexOf("deck was not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.IndexOf("无法创建/找到牌组", StringComparison.Ordinal) >= 0)
                return err + "\n\n牌组不存在，而且自动创建也没成功。请到「选择牌组」里重新挑一个牌组。";
            if (e.IndexOf("找不到笔记类型", StringComparison.Ordinal) >= 0 ||
                e.IndexOf("model was not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return err + "\n\n笔记类型不存在，请到「设置 → 输出格式」里把当前格式的笔记类型名改成已有的名字。";
            if (e.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
                return err + "\n\n这张卡和牌组里已有的某张重复了。改一下内容或换张牌组再存。";
            if (e.IndexOf("inconsistent state", StringComparison.OrdinalIgnoreCase) >= 0)
                return err + "\n\n收藏库需要重新打开一次：关掉本程序再启动即可。";
            return err;
        }

        /// <summary>牌组是否存在（大小写不敏感）。</summary>
        public static bool DeckExists(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            EnsureOpen();
            LastPayload = "deckIdByName: " + deck;
            try
            {
                foreach (string n in DeckNames())
                    if (string.Equals(n, deck, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>建牌组；已存在时直接返回 true，不会重复建。</summary>
        public static bool CreateDeck(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            EnsureOpen();
            LastPayload = "addDeck: " + deck;
            try
            {
                if (EngineRpc.DeckIdByName(deck) > 0) return true;
                long id = EngineRpc.AddDeck(deck);
                return id > 0;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>确保牌组存在（不存在就建），返回是否可用。</summary>
        public static bool EnsureDeck(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            if (DeckExists(deck)) return true;
            return CreateDeck(deck);
        }

        // ===== 笔记类型 =====

        /// <summary>列出所有笔记类型名。</summary>
        public static List<string> ModelNames()
        {
            EnsureOpen();
            LastPayload = "notetypeNames";
            try
            {
                List<AaNameId> list = EngineRpc.NotetypeNames();
                List<string> names = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++) names.Add(list[i].Name);
                return names;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>
        /// 某个笔记类型的字段名，**按 ord 顺序**（用 notetypes 的 GetFieldNames，
        /// 不是 notes 的 FieldNamesForNotes —— 后者顺序不可靠）。找不到返回空列表。
        /// </summary>
        public static List<string> ModelFieldNames(string model)
        {
            List<string> out_ = new List<string>();
            if (string.IsNullOrEmpty(model)) return out_;
            EnsureOpen();
            LastPayload = "fieldNames: " + model;
            try
            {
                long ntid = EngineRpc.NotetypeIdByName(model);
                if (ntid <= 0) return out_;
                string[] f = EngineRpc.FieldNames(ntid);
                if (f != null) out_.AddRange(f);
                return out_;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>"名字\tid" 列表（旧接口语义，设置界面在用）。</summary>
        public static List<string> ModelNamesAndIds()
        {
            EnsureOpen();
            LastPayload = "notetypeNamesAndIds";
            try
            {
                List<AaNameId> list = EngineRpc.NotetypeNames();
                List<string> out_ = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++) out_.Add(list[i].Name + "\t" + list[i].Id);
                return out_;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>笔记类型是否存在（精确匹配，保持旧语义）。</summary>
        public static bool NoteTypeExists(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            foreach (string m in ModelNames()) if (m == model) return true;
            return false;
        }

        /// <summary>
        /// 建笔记类型（模型）；已存在返回 false，不做任何改动。
        /// 模板 JSON 交给 <see cref="EngineRpc.BuildNotetypeJson"/> 生成（它封装了 id 必须为 0、
        /// flds/tmpls 结构、req 需求表达式这些坑）。
        /// </summary>
        public static bool CreateModel(string model, List<string> fields, string front, string back, string css)
        {
            if (string.IsNullOrEmpty(model)) return false;
            if (NoteTypeExists(model)) return false;
            if (fields == null || fields.Count == 0)
                throw new AnkiException("建笔记类型失败：至少要有 1 个字段。");

            EnsureOpen();
            LastPayload = "addNotetype: " + model;
            try
            {
                // did 是「这个笔记类型的默认牌组」，1 是引擎自带的默认牌组。
                long did = EngineRpc.DeckIdByName("Default");
                if (did <= 0) did = 1;
                string json = EngineRpc.BuildNotetypeJson(model, fields.ToArray(), did, front, back, css);
                long id = EngineRpc.AddNotetypeLegacy(json);
                return id > 0;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        // ===== 笔记 =====

        /// <summary>
        /// 按查询语法找笔记 id（`deck:` / `tag:` / `nid:` 等都能用）。
        /// 旧版是 AnkiConnect 的 findNotes，这里落到引擎搜索（svc 29）。
        /// </summary>
        public static List<long> FindNotes(string query)
        {
            EnsureOpen();
            LastPayload = "search: " + (query == null ? "" : query);
            try
            {
                long[] ids = EngineRpc.SearchNotes(query == null ? "" : query);
                return new List<long>(ids);
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        public class Note
        {
            public long Id;
            public string Model = "";
            public List<string> Tags = new List<string>();
            public Dictionary<string, string> Fields = new Dictionary<string, string>();
            /// <summary>旧接口的卡片数。引擎的笔记接口不返回卡片，这里恒为 0（无人使用）。</summary>
            public int CardCount;
        }

        /// <summary>
        /// 批量取笔记详情。字段名按 notetype 的 ord 顺序取（引擎的 FieldNames），
        /// 所以 `Fields` 里的名字与内容是**同一个字段**。
        /// </summary>
        public static List<Note> NotesInfo(List<long> ids)
        {
            List<Note> out_ = new List<Note>();
            if (ids == null || ids.Count == 0) return out_;
            EnsureOpen();
            LastPayload = "notesInfo: " + ids.Count;
            try
            {
                // 笔记类型 id → 名字（一次调用拿全，避免每条笔记查一次）
                Dictionary<long, string> modelNames = new Dictionary<long, string>();
                List<AaNameId> nts = EngineRpc.NotetypeNames();
                for (int i = 0; i < nts.Count; i++) modelNames[nts[i].Id] = nts[i].Name;

                List<AaNoteInfo> raw = EngineRpc.NotesInfo(ids.ToArray());
                for (int i = 0; i < raw.Count; i++)
                {
                    AaNoteInfo r = raw[i];
                    Note n = new Note();
                    n.Id = r.Id;
                    string mn;
                    if (modelNames.TryGetValue(r.NotetypeId, out mn)) n.Model = mn;
                    if (r.Tags != null) n.Tags.AddRange(r.Tags);

                    string[] names = r.FieldNames;
                    string[] vals = r.Fields;
                    if (names == null) names = new string[0];
                    if (vals == null) vals = new string[0];
                    for (int k = 0; k < names.Length; k++)
                        n.Fields[names[k]] = k < vals.Length ? vals[k] : "";
                    out_.Add(n);
                }
                return out_;
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>
        /// 加一条笔记，返回 noteId（失败抛异常，返回 0 只有引擎真的给了 0 才会出现）。
        /// 卡片落在哪个牌组由引擎的 AddNoteRequest.deck_id 决定——写完即生效，
        /// 不需要（也没有）事后搬牌组这一步。牌组不存在会先自动创建。
        /// </summary>
        public static long AddNote(string deck, string model, Dictionary<string, string> fields, List<string> tags)
        {
            if (string.IsNullOrEmpty(deck))
                throw new AnkiException("写卡失败：牌组名为空。");
            if (string.IsNullOrEmpty(model))
                throw new AnkiException("写卡失败：笔记类型为空。");

            EnsureOpen();
            LastPayload = "addNote: " + deck + " / " + model;
            try
            {
                // 引擎要的是「按 ord 顺序的字段值数组」，调用方给的是「字段名 → 值」
                List<string> names = ModelFieldNames(model);
                if (names.Count == 0)
                    throw new AnkiException("写卡失败：找不到笔记类型「" + model + "」。请到「设置 → 输出格式」里改成已有的笔记类型。");

                string[] vals = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                {
                    string v = null;
                    if (fields != null) fields.TryGetValue(names[i], out v);
                    vals[i] = v == null ? "" : v;
                }
                string[] tg = tags == null ? new string[0] : tags.ToArray();
                return EngineRpc.AddNote(deck, model, vals, tg);
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>
        /// 【不再支持】把已有笔记的卡片挪到别的牌组。
        ///
        /// 引擎没有 changeDeck / set_deck 这样的 RPC（只有 notes/decks/search 那几组），
        /// 而卡片的目标牌组在 <see cref="AddNote"/> 时就已经由 deck_id 定死了。
        /// 旧实现之所以要搬，是因为当时远端插件会忽略 note 里的牌组名；现在不需要了。
        /// 这个方法保留只为兼容：**不会做任何事**，也不会把笔记挪走。
        /// </summary>
        public static void MoveCardsToDeck(long noteId, string deck)
        {
            LastPayload = "moveCardsToDeck(不支持，空操作): " + noteId + " -> " + deck;
        }

        public static void DeleteNotes(List<long> ids)
        {
            if (ids == null || ids.Count == 0) return;
            EnsureOpen();
            LastPayload = "removeNotes: " + ids.Count;
            try
            {
                EngineRpc.RemoveNotes(ids.ToArray());
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>
        /// 同步到 AnkiWeb（等同于在界面里点「同步」）。需要先登录（hkey 存在）。
        /// 只动本程序自己的收藏库与媒体。
        /// </summary>
        public static void Sync()
        {
            EnsureOpen();   // 引擎要求：同步时收藏库必须是打开的

            string hkey = Store.Get("anki.web.hkey", "").Trim();
            if (hkey.Length == 0)
                throw new AnkiException("还没登录 AnkiWeb：先到「设置 → 同步」里登录，再来同步。");

            LastPayload = "sync";
            AaSyncAuth auth = new AaSyncAuth();
            auth.HKey = hkey;
            string ep = Store.Get("anki.web.endpoint", "").Trim();
            if (ep.Length > 0) { auth.Endpoint = ep; auth.HasEndpoint = true; }

            try
            {
                // syncMedia=true：旧语义里「同步」是连头像等媒体一起带走的
                AaSyncCollection c = EngineRpc.SyncCollection(auth, true);
                if (c.Required == AaSyncCollection.FullSync ||
                    c.Required == AaSyncCollection.FullDownload ||
                    c.Required == AaSyncCollection.FullUpload)
                    throw new AnkiException("服务器要求先做一次全量同步（" + c.RequiredText() +
                                            "）：请到「设置 → 同步」里选择上传或下载，再来普通同步。");
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>卡片到期数量（首页看板用）。</summary>
        public static int DueCount(string deck)
        {
            string q = string.IsNullOrEmpty(deck) ? "is:due" : "deck:\"" + deck + "\" is:due";
            return FindNotes(q).Count;
        }

        // ===== 媒体 =====

        /// <summary>
        /// 把一张本地图片放进本程序的媒体库，返回**实际落盘的文件名**（头像同步用）。
        ///
        /// 引擎按内容去重：同名但内容不同时会把文件改名成 `名字-&lt;sha1&gt;.扩展名`，
        /// 所以写入前先把同名旧文件丢进回收站，保证拿到的名字就是我们要求的名字
        /// （头像的媒体名是按邮箱算出来的固定值，必须稳定）。
        /// </summary>
        public static string StoreMediaFile(string fileName, byte[] data)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new AnkiException("保存媒体失败：文件名为空。");
            EnsureOpen();
            LastPayload = "addMedia: " + fileName;

            TrashOrDeleteLocal(fileName);   // 先清掉同名旧文件，避免被引擎改名
            try
            {
                return EngineRpc.AddMediaFile(fileName, data == null ? new byte[0] : data);
            }
            catch (EngineException e) { throw Wrap(e); }
        }

        /// <summary>旧接口名，等价于 <see cref="StoreMediaFile"/>。</summary>
        public static string AddMediaFile(string fileName, byte[] data)
        {
            return StoreMediaFile(fileName, data);
        }

        /// <summary>
        /// 读回媒体文件。
        ///
        /// 引擎**没有**提供 read/retrieve 媒体文件的 RPC（媒体服务只有 check/add/trash 等），
        /// 所以这里直接读本地媒体文件夹 —— 本程序的媒体就在
        /// `&lt;数据目录&gt;\anki\collection.media` 下。文件不存在（比如还没从别的设备同步
        /// 下载下来）时返回 null，不抛异常。
        /// </summary>
        public static byte[] RetrieveMediaFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            try
            {
                string p = Path.Combine(MediaFolder(), fileName);
                if (!File.Exists(p))
                {
                    // 引擎落盘前会把媒体名规范化（小写/替换非法字符），再按小写名找一次
                    p = Path.Combine(MediaFolder(), fileName.ToLowerInvariant());
                    if (!File.Exists(p)) return null;
                }
                return File.ReadAllBytes(p);
            }
            catch { return null; }
        }

        /// <summary>
        /// 删掉媒体库里的一张图（换回 Gravatar 头像时用）。
        /// 优先走引擎的 trash_media_files（丢进 media.trash，可恢复）；
        /// 引擎不可用/没开库时退化成直接删本地文件。
        /// </summary>
        public static bool DeleteMediaFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            LastPayload = "trashMedia: " + fileName;
            try
            {
                EnsureOpen();
                Pb.Writer w = new Pb.Writer(fileName.Length + 8);
                w.Str(1, fileName);   // TrashMediaFilesRequest{ repeated string fnames = 1 }
                Engine.Call(EngineMethods.Media, MediaTrashFiles, w.ToBytes());
                return true;
            }
            catch (Exception)
            {
                try
                {
                    string p = Path.Combine(MediaFolder(), fileName);
                    if (!File.Exists(p)) p = Path.Combine(MediaFolder(), fileName.ToLowerInvariant());
                    if (File.Exists(p)) { File.Delete(p); return true; }
                }
                catch { }
                return false;
            }
        }

        /// <summary>把同名旧媒体丢进回收站；失败就删本地文件；再失败就算了（不影响新写入）。</summary>
        static void TrashOrDeleteLocal(string fileName)
        {
            try
            {
                Pb.Writer w = new Pb.Writer(fileName.Length + 8);
                w.Str(1, fileName);
                Engine.Call(EngineMethods.Media, MediaTrashFiles, w.ToBytes());
                return;
            }
            catch (Exception) { }

            try
            {
                string p = Path.Combine(MediaFolder(), fileName);
                if (!File.Exists(p)) p = Path.Combine(MediaFolder(), fileName.ToLowerInvariant());
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
        }
    }
}
