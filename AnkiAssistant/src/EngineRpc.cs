using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>
    /// 内置引擎的 service / method 编号。
    ///
    /// **依据**：`D:\Anki-Android-Backend\anki\out\pylib\anki\_backend_generated.py`
    /// （克隆 commit：外层 39e72a117ee1e3f165bff4e15b1a8bcc531f1d3d，anki 子模块 29bb700b951e3f0c0cb69b77c0180fc1fe33e6ba）。
    /// 该文件里每个 `def xxx_raw(self, message: bytes)` 的方法体就是
    /// `return self._run_command(service, method, message)`，编号是显式数字，注释里的 `line=` 是那个 def 的行号。
    ///
    /// 注意：**编号不在 .proto 里**（proto 只有 service 名和空的 `service BackendXxxService {}` 占位），
    /// 所以升级后端时必须回到这个生成文件重新核对。
    /// </summary>
    public static class EngineMethods
    {
        // svc = 1  SyncService
        public const int Sync = 1;
        public const int SyncMedia = 0;              // _backend_generated.py:45
        public const int SyncLogin = 3;              // :79
        public const int SyncStatus = 4;             // :89
        public const int SyncCollection = 5;         // :99
        public const int FullUploadOrDownload = 6;   // :109

        // svc = 3  CollectionService
        public const int Collection = 3;
        public const int OpenCollection = 0;         // :139
        public const int CloseCollection = 1;        // :149

        // svc = 7  DecksService
        public const int Decks = 7;
        public const int AddDeck = 1;                // :351
        public const int GetDeckIdByName = 7;        // :411
        public const int GetDeckNames = 13;          // :471

        // svc = 9  ConfigService
        public const int Config = 9;
        public const int GetConfigJson = 0;          // :581
        public const int SetConfigJson = 1;          // :591

        // svc = 23  NotetypesService
        public const int Notetypes = 23;
        public const int AddNotetypeLegacy = 2;      // :1265
        public const int GetNotetypeNames = 8;       // :1325
        public const int GetNotetypeIdByName = 10;   // :1345
        public const int GetFieldNames = 16;         // :1405

        // svc = 25  NotesService
        public const int Notes = 25;
        public const int AddNote = 1;                // :1445
        public const int GetNote = 6;                // :1495
        public const int RemoveNotes = 7;            // :1505
        public const int FieldNamesForNotes = 10;    // :1535

        // svc = 29  SearchService
        public const int Search = 29;
        public const int SearchNotes = 2;            // :1755

        // svc = 41  MediaService
        public const int Media = 41;
        public const int AddMediaFile = 2;           // :2071
    }

    /// <summary>`deck_names` / `notetype_names` 里的一项（proto DeckNameId / NotetypeNameId：id=1, name=2）。</summary>
    public sealed class AaNameId
    {
        public long Id;
        public string Name = "";
        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    /// <summary>回读一张卡（引擎侧叫 note，不是 card）。字段名单独通过 FieldNamesForNotes 取。</summary>
    public sealed class AaNoteInfo
    {
        public long Id;
        public string Guid = "";
        public long NotetypeId;
        public uint MtimeSecs;
        public int Usn;
        public string[] Tags = new string[0];
        public string[] Fields = new string[0];
        public string[] FieldNames = new string[0];

        /// <summary>按字段名取值；找不到返回 null。</summary>
        public string Field(string name)
        {
            if (name == null) return null;
            for (int i = 0; i < FieldNames.Length && i < Fields.Length; i++)
            {
                if (string.Equals(FieldNames[i], name, StringComparison.Ordinal)) return Fields[i];
            }
            return null;
        }
    }

    /// <summary>登录成功后拿到的同步凭证（proto SyncAuth，sync.proto:29-33）。</summary>
    public sealed class AaSyncAuth
    {
        public string HKey = "";
        public string Endpoint = "";
        public uint IoTimeoutSecs;
        public bool HasEndpoint;
        public bool HasIoTimeout;
    }

    /// <summary>同步状态（proto SyncStatusResponse，sync.proto:41-49）。</summary>
    public sealed class AaSyncStatus
    {
        public const int NoChanges = 0;
        public const int NormalSync = 1;
        public const int FullSync = 2;

        public int Required;
        public string NewEndpoint = "";
        public bool HasNewEndpoint;

        public string RequiredText()
        {
            switch (Required)
            {
                case NoChanges: return "无需同步";
                case NormalSync: return "普通同步";
                case FullSync: return "需要全量同步";
                default: return "未知(" + Required + ")";
            }
        }
    }

    /// <summary>同步结果（proto SyncCollectionResponse，sync.proto:56-72）。</summary>
    public sealed class AaSyncCollection
    {
        public const int NoChanges = 0;
        public const int NormalSync = 1;
        public const int FullSync = 2;
        public const int FullDownload = 3;
        public const int FullUpload = 4;

        public uint HostNumber;
        public string ServerMessage = "";
        public int Required;
        public string NewEndpoint = "";
        public bool HasNewEndpoint;
        public int ServerMediaUsn;

        public string RequiredText()
        {
            switch (Required)
            {
                case NoChanges: return "无需同步";
                case NormalSync: return "普通同步";
                case FullSync: return "需要全量同步";
                case FullDownload: return "需要全量下载";
                case FullUpload: return "需要全量上传";
                default: return "未知(" + Required + ")";
            }
        }
    }

    /// <summary>
    /// 内置 Anki 引擎的强类型 RPC 层：每个方法都用 Pb 手工组请求、手工解响应。
    ///
    /// 调用顺序固定为：`Engine.Open(...)`（内部 = aa_open(空 BackendInit) + RPC 3/0）
    /// → 各种业务调用 → `EngineRpc.CloseCollection()` / `Engine.Close()`。
    ///
    /// 每个方法上方都写了字段号依据（proto 文件 + 行号），改后端时照着核对。
    /// </summary>
    public static class EngineRpc
    {
        // ---------------- 收藏库 ----------------

        /// <summary>
        /// 打开收藏库。等价于 `Engine.Open`，这里再包一层是为了让上层只 import 一个类型。
        /// OpenCollectionRequest{collection_path=1, media_folder_path=2, media_db_path=3}
        /// —— collection.proto:43-47，rpc 见 collection.proto:29。
        /// </summary>
        public static void OpenCollection(string collectionPath, string mediaFolder, string mediaDb)
        {
            Engine.Open(collectionPath, mediaFolder, mediaDb);
        }

        /// <summary>CloseCollectionRequest{downgrade_to_schema11=1}，留空即可（collection.proto:49-51，rpc :30）。</summary>
        public static void CloseCollection()
        {
            if (!Engine.Opened) return;
            try
            {
                Engine.Call(EngineMethods.Collection, EngineMethods.CloseCollection, new byte[0]);
            }
            finally
            {
                Engine.Close();
            }
        }

        // ---------------- 牌组 ----------------

        /// <summary>
        /// 列出所有牌组。GetDeckNamesRequest{skip_empty_default=1, include_filtered=2}
        /// （decks.proto:196-200）→ DeckNames{repeated DeckNameId entries=1}（:202-204），
        /// DeckNameId{id=1, name=2}（:206-209）。rpc 见 decks.proto:27。
        /// </summary>
        public static List<AaNameId> DeckNames()
        {
            Pb.Writer w = new Pb.Writer(8);
            w.Bool(1, false);   // skip_empty_default = false：连空牌组一起要
            w.Bool(2, true);    // include_filtered = true：过滤牌组也算牌组
            byte[] resp = Engine.Call(EngineMethods.Decks, EngineMethods.GetDeckNames, w.ToBytes());
            return ParseNameIdEntries(resp);
        }

        /// <summary>
        /// 新建牌组。请求就是 **Deck 消息本身**（decks.proto:15 `rpc AddDeck(Deck)`，**没有外层包装**）。
        /// Deck{id=1, name=2, mtime_secs=3, usn=4, common=5, oneof kind{normal=6, filtered=7}}（decks.proto:134-144）。
        /// **必须显式写 normal（字段 6，空消息即可）**，否则后端报 `missing kind`
        /// （Rust 侧 decks/mod.rs 的 `Deck::normal()` 对非 Normal 的 kind 直接 invalid_input）。
        /// 返回 OpChangesWithId{changes=1, int64 id=2}（collection.proto:90-93）里的新牌组 id
        /// —— decks/service.rs 的 add_deck 用 `deck.id` 填这个字段。
        /// </summary>
        public static long AddDeck(string name)
        {
            if (name == null || name.Length == 0) throw new EngineException("新建牌组失败：牌组名为空");

            Pb.Writer deck = new Pb.Writer(64);
            deck.Str(2, name);                          // Deck.name = 2
            deck.Msg(6, new Pb.Writer());               // Deck.normal = 6（空 Normal{}，oneof 必须被设置）

            byte[] resp = Engine.Call(EngineMethods.Decks, EngineMethods.AddDeck, deck.ToBytes());
            return ReadInt64Field(resp, 2);             // OpChangesWithId.id
        }

        /// <summary>
        /// 按名字查牌组 id。请求 generic.String{val=1}（generic.proto:24-26），
        /// 响应 DeckId{did=1}（decks.proto:46-48），rpc 见 decks.proto:21。
        /// 找不到时后端返回 NOT_FOUND 错误（decks/service.rs 的 `or_not_found`），
        /// 这里吞掉异常返回 -1，和安卓端 AnkiEngine 的行为一致。
        /// </summary>
        public static long DeckIdByName(string name)
        {
            if (name == null) name = "";
            Pb.Writer w = new Pb.Writer(32);
            w.Str(1, name);
            try
            {
                byte[] resp = Engine.Call(EngineMethods.Decks, EngineMethods.GetDeckIdByName, w.ToBytes());
                return ReadInt64Field(resp, 1);         // DeckId.did
            }
            catch (EngineException)
            {
                return -1L;
            }
        }

        // ---------------- 笔记类型 ----------------

        /// <summary>
        /// 列出所有笔记类型。请求是空消息（notetypes.proto:23 `GetNotetypeNames(generic.Empty)`）
        /// → NotetypeNames{repeated NotetypeNameId entries=1}（:170-172），
        /// NotetypeNameId{id=1, name=2}（:178-181）。
        /// </summary>
        public static List<AaNameId> NotetypeNames()
        {
            byte[] resp = Engine.Call(EngineMethods.Notetypes, EngineMethods.GetNotetypeNames, new byte[0]);
            return ParseNameIdEntries(resp);
        }

        /// <summary>
        /// 按名字查笔记类型 id。请求 generic.String{val=1}（generic.proto:24-26），
        /// 响应 NotetypeId{ntid=1}（notetypes.proto:43-45），rpc 见 notetypes.proto:25。
        /// 找不到返回 -1（同安卓端）。
        /// </summary>
        public static long NotetypeIdByName(string name)
        {
            if (name == null) name = "";
            Pb.Writer w = new Pb.Writer(32);
            w.Str(1, name);
            try
            {
                byte[] resp = Engine.Call(EngineMethods.Notetypes, EngineMethods.GetNotetypeIdByName, w.ToBytes());
                return ReadInt64Field(resp, 1);         // NotetypeId.ntid
            }
            catch (EngineException)
            {
                return -1L;
            }
        }

        /// <summary>
        /// 用「legacy JSON」建一个笔记类型。入参就是 `generic.Json{bytes json=1}`
        /// （generic.proto:28-30；rpc 见 notetypes.proto:16，Rust 侧 notetype/service.rs 直接
        /// `serde_json::from_slice::<NotetypeSchema11>(&input.json)`）。
        /// 返回 OpChangesWithId.id = 新笔记类型 id（notetype/service.rs 用 notetype.id 填）。
        /// </summary>
        public static long AddNotetypeLegacy(string json)
        {
            if (json == null) throw new EngineException("新建笔记类型失败：JSON 为空");
            Pb.Writer w = new Pb.Writer(json.Length + 8);
            w.Str(1, json);
            byte[] resp = Engine.Call(EngineMethods.Notetypes, EngineMethods.AddNotetypeLegacy, w.ToBytes());
            return ReadInt64Field(resp, 2);
        }

        /// <summary>
        /// 按 legacy（schema11）结构生成笔记类型 JSON。
        ///
        /// 结构与必填项依据 `anki\rslib\src\notetype\schema11.rs` 里的 `NotetypeSchema11`
        /// （camelCase；必填 id/name/type/mod/usn/sortf/tmpls/flds）：
        ///   id      = 0 表示「新建」（后端自己分配 id）
        ///   type    = 0 标准 / 1 填空
        ///   req     = [[card_ord, "any|all|none", [field_ord...]]]（CardRequirementsSchema11 是 tuple 序列化）
        /// 未知字段会被 `#[serde(flatten)] other` 吃掉，不会报错。
        /// </summary>
        public static string BuildNotetypeJson(string name, string[] fieldNames, long deckId,
                                               string frontTemplate, string backTemplate, string css)
        {
            if (name == null || name.Length == 0) throw new EngineException("生成笔记类型 JSON 失败：名字为空");
            if (fieldNames == null || fieldNames.Length == 0)
                throw new EngineException("生成笔记类型 JSON 失败：至少要有 1 个字段");

            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            List<object> flds = new List<object>();
            for (int i = 0; i < fieldNames.Length; i++)
            {
                Dictionary<string, object> f = new Dictionary<string, object>();
                f["name"] = fieldNames[i];
                f["ord"] = i;
                f["sticky"] = false;
                f["rtl"] = false;
                f["font"] = "Arial";
                f["size"] = 20;
                f["id"] = i + 1;
                flds.Add(f);
            }

            string qfmt = frontTemplate;
            if (qfmt == null || qfmt.Length == 0) qfmt = "{{" + fieldNames[0] + "}}";
            string afmt = backTemplate;
            if (afmt == null || afmt.Length == 0)
            {
                afmt = "{{FrontSide}}<hr id=answer>";
                if (fieldNames.Length > 1) afmt += "{{" + fieldNames[1] + "}}";
                else afmt += "{{" + fieldNames[0] + "}}";
            }

            Dictionary<string, object> tmpl = new Dictionary<string, object>();
            tmpl["name"] = "Card 1";
            tmpl["ord"] = 0;
            tmpl["qfmt"] = qfmt;
            tmpl["afmt"] = afmt;
            tmpl["bqfmt"] = "";
            tmpl["bafmt"] = "";
            tmpl["bfont"] = "";
            tmpl["bsize"] = 0;
            tmpl["id"] = 1;

            List<object> tmpls = new List<object>();
            tmpls.Add(tmpl);

            List<object> req1 = new List<object>();
            req1.Add(0);              // card_ord
            req1.Add("any");          // FieldRequirementKindSchema11::Any
            List<object> ords = new List<object>();
            for (int i = 0; i < fieldNames.Length; i++) ords.Add(i);
            req1.Add(ords);
            List<object> req = new List<object>();
            req.Add(req1);

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["id"] = 0;
            root["name"] = name;
            root["type"] = 0;
            root["mod"] = now;
            root["usn"] = -1;
            root["sortf"] = 0;
            root["did"] = deckId;
            root["css"] = css == null ? "" : css;
            root["latexPre"] = "\\documentclass[12pt]{article}\n\\special{papersize=3in,5in}\n\\usepackage[utf8]{inputenc}\n\\usepackage{amssymb,amsmath}\n\\pagestyle{empty}\n\\setlength{\\parindent}{0in}\n\\begin{document}\n";
            root["latexPost"] = "\\end{document}";
            root["latexsvg"] = false;
            root["flds"] = flds;
            root["tmpls"] = tmpls;
            root["req"] = req;

            return Json.Serialize(root);
        }

        /// <summary>某个笔记类型的字段名列表。请求 NotetypeId{ntid=1}（notetypes.proto:43-45），
        /// 响应 generic.StringList{repeated string vals=1}（generic.proto:36-38），rpc 见 notetypes.proto:33。</summary>
        public static string[] FieldNames(long notetypeId)
        {
            Pb.Writer w = new Pb.Writer(16);
            w.Varint(1, notetypeId);
            byte[] resp = Engine.Call(EngineMethods.Notetypes, EngineMethods.GetFieldNames, w.ToBytes());
            return ReadRepeatedString(resp, 1);
        }

        // ---------------- 笔记 ----------------

        /// <summary>
        /// 写一张卡。
        ///
        /// 请求 AddNoteRequest{Note note=1; int64 deck_id=2}（notes.proto:56-59），
        /// Note{int64 id=1; string guid=2; int64 notetype_id=3; uint32 mtime_secs=4; int32 usn=5;
        ///      repeated string tags=6; repeated string fields=7}（notes.proto:46-54）。
        /// rpc 见 notes.proto:17；`_backend_generated.py:1448` 的签名 `add_note(*, note, deck_id)`
        /// 也印证了 deck_id 是**独立字段**。
        ///
        /// **卡片落在哪个牌组完全由这里的 deck_id 决定**（外加 Note.notetype_id），
        /// 不像 AnkiConnect 那样需要事后搬牌组。deckName 不存在就先 AddDeck 建一个。
        /// 返回新 note id（AddNoteResponse.note_id，notes.proto:61-64 的字段 2）。
        /// </summary>
        public static long AddNote(string deckName, string modelName, string[] fields, string[] tags)
        {
            if (deckName == null || deckName.Length == 0)
                throw new EngineException("写卡失败：牌组名为空");

            long did = DeckIdByName(deckName);
            if (did < 0)
            {
                AddDeck(deckName);
                did = DeckIdByName(deckName);
                if (did < 0) throw new EngineException("写卡失败：无法创建/找到牌组「" + deckName + "」");
            }

            long ntid = NotetypeIdByName(modelName);
            if (ntid < 0) throw new EngineException("写卡失败：找不到笔记类型「" + modelName + "」");

            Pb.Writer note = new Pb.Writer(128);
            note.Varint(3, ntid);                                  // Note.notetype_id
            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] != null && tags[i].Length > 0) note.Str(6, tags[i]);
                }
            }
            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++) note.Str(7, fields[i] == null ? "" : fields[i]);
            }

            Pb.Writer req = new Pb.Writer(note.Length + 16);
            req.Msg(1, note.ToBytes());                            // AddNoteRequest.note
            req.Varint(2, did);                                    // AddNoteRequest.deck_id

            byte[] resp = Engine.Call(EngineMethods.Notes, EngineMethods.AddNote, req.ToBytes());
            return ReadInt64Field(resp, 2);                         // AddNoteResponse.note_id
        }

        /// <summary>
        /// 单独取一张笔记。请求 NoteId{nid=1}（notes.proto:38-40），响应 Note（:46-54），
        /// rpc 见 notes.proto:22。字段名不在这里返回，要另行 FieldNamesForNotes。
        /// </summary>
        public static AaNoteInfo GetNote(long id)
        {
            Pb.Writer w = new Pb.Writer(16);
            w.Varint(1, id);
            byte[] resp = Engine.Call(EngineMethods.Notes, EngineMethods.GetNote, w.ToBytes());

            AaNoteInfo n = new AaNoteInfo();
            n.Id = id;
            Pb.Reader r = new Pb.Reader(resp);
            List<string> tags = new List<string>();
            List<string> fields = new List<string>();
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireVarint) n.Id = r.Varint();
                else if (f == 2 && wt == Pb.WireLength) n.Guid = r.Str();
                else if (f == 3 && wt == Pb.WireVarint) n.NotetypeId = r.Varint();
                else if (f == 4 && wt == Pb.WireVarint) n.MtimeSecs = (uint)r.Varint();
                else if (f == 5 && wt == Pb.WireVarint) n.Usn = r.Int32();
                else if (f == 6 && wt == Pb.WireLength) tags.Add(r.Str());
                else if (f == 7 && wt == Pb.WireLength) fields.Add(r.Str());
                else r.Skip();
            }
            n.Tags = tags.ToArray();
            n.Fields = fields.ToArray();
            return n;
        }

        /// <summary>
        /// 一批笔记的字段名。请求 FieldNamesForNotesRequest{repeated int64 nids=1}（notes.proto:104-106），
        /// 响应 FieldNamesForNotesResponse{repeated string fields=1}（:108-110），rpc 见 notes.proto:27。
        ///
        /// **警告：返回的顺序不是 Note.fields 的顺序，别按下标配对。**
        /// 实测（engine_probe，2026-10-05）字段 Front/Back 的笔记，这里返回 `[Back, Front]`
        /// （看着像按名字排序），而 Note.fields 是 `[Front 的值, Back 的值]`。
        /// 要按下标取字段值，请用 <see cref="FieldNames"/>（23/16），它是按 notetype 的 ord 顺序的。
        /// 这个方法只适合「想看看有哪些字段名」这种集合语义的用途。
        /// </summary>
        public static string[] FieldNamesForNotes(long[] ids)
        {
            if (ids == null || ids.Length == 0) return new string[0];
            Pb.Writer w = new Pb.Writer(ids.Length * 4);
            w.Packed(1, ids);
            byte[] resp = Engine.Call(EngineMethods.Notes, EngineMethods.FieldNamesForNotes, w.ToBytes());
            return ReadRepeatedString(resp, 1);
        }

        /// <summary>
        /// 批量回读笔记（引擎没有 AnkiConnect 那种 notesInfo，得自己把 get_note + 字段名拼起来）。
        /// 每个 id 一次 GetNote；字段名按 notetype 缓存一次（同一个 notetype 只查一次）。
        ///
        /// 这里刻意**不用** FieldNamesForNotes：它的顺序跟 Note.fields 对不上（见那个方法的注释）。
        /// 用 FieldNames(NotetypeId) 才能保证 `FieldNames[i]` 与 `Fields[i]` 是同一个字段。
        /// </summary>
        public static List<AaNoteInfo> NotesInfo(long[] ids)
        {
            List<AaNoteInfo> list = new List<AaNoteInfo>();
            if (ids == null || ids.Length == 0) return list;

            Dictionary<long, string[]> byNotetype = new Dictionary<long, string[]>();

            for (int i = 0; i < ids.Length; i++)
            {
                AaNoteInfo n = GetNote(ids[i]);
                string[] names;
                if (!byNotetype.TryGetValue(n.NotetypeId, out names))
                {
                    try { names = FieldNames(n.NotetypeId); }
                    catch (EngineException) { names = new string[0]; }
                    byNotetype[n.NotetypeId] = names;
                }
                n.FieldNames = names;
                list.Add(n);
            }
            return list;
        }

        /// <summary>
        /// 删笔记。请求 RemoveNotesRequest{repeated int64 note_ids=1; repeated int64 card_ids=2}
        /// （notes.proto:89-92），响应 OpChangesWithCount{changes=1, count=2}（collection.proto:85-88），
        /// rpc 见 notes.proto:23。返回真正删掉的数量。
        /// </summary>
        public static int RemoveNotes(long[] ids)
        {
            if (ids == null || ids.Length == 0) return 0;
            Pb.Writer w = new Pb.Writer(ids.Length * 4 + 8);
            w.Packed(1, ids);
            byte[] resp = Engine.Call(EngineMethods.Notes, EngineMethods.RemoveNotes, w.ToBytes());
            return (int)ReadInt64Field(resp, 2);
        }

        // ---------------- 搜索 ----------------

        /// <summary>
        /// 搜索笔记。请求 SearchRequest{string search=1; SortOrder order=2}（search.proto:113-116，
        /// order 留空即默认排序），响应 SearchResponse{repeated int64 ids=1}（:118-120，proto3 默认 packed，
        /// 但这里两种写法都能读），rpc 见 search.proto:16。
        /// </summary>
        public static long[] SearchNotes(string query)
        {
            if (query == null) query = "";
            Pb.Writer w = new Pb.Writer(query.Length + 8);
            w.Str(1, query);
            byte[] resp = Engine.Call(EngineMethods.Search, EngineMethods.SearchNotes, w.ToBytes());

            List<long> ids = new List<long>();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1) Pb.ReadRepeatedInt64(r, wt, ids);
                else r.Skip();
            }
            return ids.ToArray();
        }

        // ---------------- 配置 ----------------

        /// <summary>
        /// 读一条配置。请求 generic.String{val=1}（generic.proto:24-26），
        /// 响应 generic.Json{bytes json=1}（:28-30），rpc 见 config.proto:14。
        /// 返回原始 JSON 文本。
        /// </summary>
        public static string GetConfigJson(string key)
        {
            if (key == null) key = "";
            Pb.Writer w = new Pb.Writer(key.Length + 8);
            w.Str(1, key);
            byte[] resp = Engine.Call(EngineMethods.Config, EngineMethods.GetConfigJson, w.ToBytes());

            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireLength)
                {
                    byte[] b = r.Bytes();
                    return b.Length == 0 ? "" : System.Text.Encoding.UTF8.GetString(b);
                }
                r.Skip();
            }
            return "";
        }

        /// <summary>
        /// 写一条配置。请求 SetConfigJsonRequest{string key=1; bytes value_json=2; bool undoable=3}
        /// （config.proto:99-103），响应 OpChanges，rpc 见 config.proto:15。
        /// valueJson 必须是一段合法 JSON 文本（比如 `"abc"`、`42`、`{"a":1}`）。
        /// </summary>
        public static void SetConfigJson(string key, string valueJson)
        {
            if (key == null) key = "";
            if (valueJson == null) valueJson = "null";

            Pb.Writer w = new Pb.Writer(key.Length + valueJson.Length + 16);
            w.Str(1, key);
            w.Str(2, valueJson);      // bytes value_json：直接放 UTF-8 字节，走 Str 是一样的效果
            w.Bool(3, true);          // undoable
            Engine.Call(EngineMethods.Config, EngineMethods.SetConfigJson, w.ToBytes());
        }

        // ---------------- 媒体 ----------------

        /// <summary>
        /// 添加媒体文件。请求 AddMediaFileRequest{string desired_name=1; bytes data=2}（media.proto:44-47），
        /// 响应 generic.String{val=1} = 实际落盘的文件名（可能被去重改名），rpc 见 media.proto:15。
        /// </summary>
        public static string AddMediaFile(string fileName, byte[] data)
        {
            if (fileName == null || fileName.Length == 0)
                throw new EngineException("添加媒体失败：文件名为空");
            if (data == null) data = new byte[0];

            Pb.Writer w = new Pb.Writer(fileName.Length + data.Length + 16);
            w.Str(1, fileName);
            w.Bytes(2, data);
            byte[] resp = Engine.Call(EngineMethods.Media, EngineMethods.AddMediaFile, w.ToBytes());

            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireLength) return r.Str();
                r.Skip();
            }
            return "";
        }

        // ---------------- 同步 ----------------

        /// <summary>上一次同步调用返回的新端点（AnkiWeb 迁移时会换域名，要记住它）。</summary>
        public static string LastEndpoint = "";

        /// <summary>
        /// 登录。请求 SyncLoginRequest{username=1; password=2; optional endpoint=3}（sync.proto:35-39），
        /// 响应 SyncAuth{hkey=1; optional endpoint=2; optional io_timeout_secs=3}（:29-33），
        /// rpc 见 sync.proto:21，编号 1/3 见 _backend_generated.py:79。
        /// </summary>
        public static AaSyncAuth SyncLogin(string user, string pass, string endpoint)
        {
            Pb.Writer w = new Pb.Writer(64);
            w.Str(1, user == null ? "" : user);
            w.Str(2, pass == null ? "" : pass);
            if (endpoint != null && endpoint.Length > 0) w.Str(3, endpoint);

            byte[] resp = Engine.Call(EngineMethods.Sync, EngineMethods.SyncLogin, w.ToBytes());
            AaSyncAuth a = ParseSyncAuth(resp);
            if (a.HasEndpoint && a.Endpoint.Length > 0) LastEndpoint = a.Endpoint;
            return a;
        }

        /// <summary>
        /// 查同步状态。请求 SyncAuth（sync.proto:29-33），
        /// 响应 SyncStatusResponse{required=1; optional new_endpoint=4}（:41-49），rpc 见 sync.proto:22。
        /// </summary>
        public static AaSyncStatus SyncStatus(AaSyncAuth auth)
        {
            byte[] resp = Engine.Call(EngineMethods.Sync, EngineMethods.SyncStatus, EncodeSyncAuth(auth));
            AaSyncStatus s = new AaSyncStatus();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireVarint) s.Required = (int)r.Varint();
                else if (f == 4 && wt == Pb.WireLength) { s.NewEndpoint = r.Str(); s.HasNewEndpoint = true; }
                else r.Skip();
            }
            if (s.HasNewEndpoint && s.NewEndpoint.Length > 0) LastEndpoint = s.NewEndpoint;
            return s;
        }

        /// <summary>
        /// 普通同步。请求 SyncCollectionRequest{SyncAuth auth=1; bool sync_media=2}（sync.proto:51-54），
        /// 响应 SyncCollectionResponse{host_number=1; server_message=2; required=3;
        /// optional new_endpoint=4; server_media_usn=5}（:56-72），rpc 见 sync.proto:23。
        /// </summary>
        public static AaSyncCollection SyncCollection(AaSyncAuth auth, bool syncMedia)
        {
            Pb.Writer w = new Pb.Writer(64);
            w.Msg(1, EncodeSyncAuth(auth));
            w.Bool(2, syncMedia);
            byte[] resp = Engine.Call(EngineMethods.Sync, EngineMethods.SyncCollection, w.ToBytes());

            AaSyncCollection c = new AaSyncCollection();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireVarint) c.HostNumber = (uint)r.Varint();
                else if (f == 2 && wt == Pb.WireLength) c.ServerMessage = r.Str();
                else if (f == 3 && wt == Pb.WireVarint) c.Required = (int)r.Varint();
                else if (f == 4 && wt == Pb.WireLength) { c.NewEndpoint = r.Str(); c.HasNewEndpoint = true; }
                else if (f == 5 && wt == Pb.WireVarint) c.ServerMediaUsn = r.Int32();
                else r.Skip();
            }
            if (c.HasNewEndpoint && c.NewEndpoint.Length > 0) LastEndpoint = c.NewEndpoint;
            return c;
        }

        /// <summary>
        /// 强制全量上传 / 下载。请求 FullUploadOrDownloadRequest{SyncAuth auth=1; bool upload=2;
        /// optional int32 server_usn=3}（sync.proto:85-90），响应 empty，rpc 见 sync.proto:24。
        /// upload=true 是「本地上传覆盖服务器」，false 是「从服务器下载覆盖本地」——都会丢数据，调用方自己确认。
        /// </summary>
        public static void FullUploadOrDownload(AaSyncAuth auth, bool upload)
        {
            Pb.Writer w = new Pb.Writer(64);
            w.Msg(1, EncodeSyncAuth(auth));
            w.Bool(2, upload);
            Engine.Call(EngineMethods.Sync, EngineMethods.FullUploadOrDownload, w.ToBytes());
        }

        // ---------------- 内部解析 ----------------

        /// <summary>SyncAuth 编码（sync.proto:29-33）。</summary>
        static byte[] EncodeSyncAuth(AaSyncAuth a)
        {
            if (a == null) throw new EngineException("同步凭证为空：先 SyncLogin");
            Pb.Writer w = new Pb.Writer(64);
            w.Str(1, a.HKey == null ? "" : a.HKey);
            if (a.HasEndpoint && a.Endpoint != null && a.Endpoint.Length > 0) w.Str(2, a.Endpoint);
            if (a.HasIoTimeout) w.UInt32(3, a.IoTimeoutSecs);
            return w.ToBytes();
        }

        static AaSyncAuth ParseSyncAuth(byte[] resp)
        {
            AaSyncAuth a = new AaSyncAuth();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireLength) a.HKey = r.Str();
                else if (f == 2 && wt == Pb.WireLength) { a.Endpoint = r.Str(); a.HasEndpoint = true; }
                else if (f == 3 && wt == Pb.WireVarint) { a.IoTimeoutSecs = (uint)r.Varint(); a.HasIoTimeout = true; }
                else r.Skip();
            }
            return a;
        }

        /// <summary>解 DeckNames / NotetypeNames 那种 `{repeated X entries = 1}`，X 里 id=1、name=2。</summary>
        static List<AaNameId> ParseNameIdEntries(byte[] resp)
        {
            List<AaNameId> list = new List<AaNameId>();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireLength)
                {
                    Pb.Reader sub = r.Msg();
                    AaNameId e = new AaNameId();
                    int sf, swt;
                    while (sub.Next(out sf, out swt))
                    {
                        if (sf == 1 && swt == Pb.WireVarint) e.Id = sub.Varint();
                        else if (sf == 2 && swt == Pb.WireLength) e.Name = sub.Str();
                        else sub.Skip();
                    }
                    list.Add(e);
                }
                else r.Skip();
            }
            return list;
        }

        /// <summary>取一个 int64 字段（第一个匹配的）。OpChangesWithId.id / DeckId.did / AddNoteResponse.note_id 都靠它。</summary>
        static long ReadInt64Field(byte[] resp, int field)
        {
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == field && wt == Pb.WireVarint) return r.Varint();
                r.Skip();
            }
            return 0L;
        }

        /// <summary>取一个 repeated string 字段（generic.StringList.vals=1）。</summary>
        static string[] ReadRepeatedString(byte[] resp, int field)
        {
            List<string> list = new List<string>();
            Pb.Reader r = new Pb.Reader(resp);
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == field) Pb.ReadRepeatedString(r, wt, list);
                else r.Skip();
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// 工程里原来只有 AnkiConn 那种手写 JSON，这里把 JavaScriptSerializer 包一层，
    /// 让 EngineRpc 组 legacy 笔记类型 JSON 时不用到处 new。
    /// </summary>
    static class Json
    {
        public static string Serialize(object o)
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            return s.Serialize(o);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            if (json == null) throw new EngineException("JSON 为空");
            JavaScriptSerializer s = new JavaScriptSerializer();
            object o = s.DeserializeObject(json);
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) throw new EngineException("JSON 顶层不是对象：" + Clip(json));
            return d;
        }

        static string Clip(string s)
        {
            if (s == null) return "";
            if (s.Length > 120) return s.Substring(0, 120) + "...";
            return s;
        }
    }
}
