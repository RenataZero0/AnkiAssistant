using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>
    /// AnkiConnect 客户端 —— Windows 版通过它和**本机正在运行的 Anki 桌面端**说话。
    ///
    /// 为什么走这条路（而不是像 Android 版那样把引擎打进包里）：
    ///   · 电脑上本来就有 Anki，读写它的库最稳，不会出现两套库打架
    ///   · 笔记类型 / 模板 / 媒体 / AnkiWeb 同步全由 Anki 自己管，我们只当编辑器
    ///   · 代价是 Anki 必须开着，且装了 AnkiConnect 插件（插件号 2055492159）
    ///
    /// 协议：HTTP POST 到 http://127.0.0.1:8765，body = {"action":..., "version":6, "params":{...}}
    /// </summary>
    public static class AnkiConn
    {
        public const string AddonCode = "2055492159";

        /// <summary>默认端口。AnkiConnect 的默认值就是 8765。</summary>
        public static string Endpoint
        {
            get { return Store.Get("anki.endpoint", "http://127.0.0.1:8765"); }
            set { Store.Set("anki.endpoint", value); }
        }

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        /// <summary>诊断用：最近一次真正发出去的 JSON 报文。</summary>
        public static string LastPayload = "";

        public class AnkiException : Exception
        {
            public AnkiException(string msg) : base(msg) { }
        }

        // ===== 底层调用 =====
        static object Invoke(string action, Dictionary<string, object> param, int timeout)
        {
            var body = new Dictionary<string, object>();
            body["action"] = action;
            body["version"] = 6;
            body["params"] = param ?? new Dictionary<string, object>();

            string payload = Json.Serialize(body);
            LastPayload = payload;   // 诊断用：最近一次真正发出去的报文
            byte[] data = Encoding.UTF8.GetBytes(payload);

            var req = (HttpWebRequest)WebRequest.Create(Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            req.ContentLength = data.Length;
            req.Proxy = null;   // 本机地址别走系统代理，否则有的环境会被拦

            string text;
            try
            {
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (WebResponse resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    text = sr.ReadToEnd();
            }
            catch (WebException e)
            {
                throw new AnkiException(Explain(e));
            }
            catch (Exception e)
            {
                throw new AnkiException("连不上 Anki：" + e.Message);
            }

            Dictionary<string, object> map = null;
            try { map = Json.DeserializeObject(text) as Dictionary<string, object>; }
            catch { throw new AnkiException("AnkiConnect 返回的不是 JSON：" + Head(text)); }
            if (map == null) throw new AnkiException("AnkiConnect 返回的内容看不懂：" + Head(text));

            object err;
            if (map.TryGetValue("error", out err) && err != null)
                throw new AnkiException(Convert.ToString(err));
            object result;
            return map.TryGetValue("result", out result) ? result : null;
        }

        static object Invoke(string action) { return Invoke(action, null, 20000); }
        static object Invoke(string action, Dictionary<string, object> p) { return Invoke(action, p, 20000); }

        static string Head(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        static string Explain(WebException e)
        {
            // 只有"连接被拒绝"才等于 Anki 没开 / 没装插件。
            // 超时是"端口在听但没回话"（Anki 卡住、或者被防火墙吞了），别混为一谈。
            if (e.Status == WebExceptionStatus.ConnectFailure ||
                e.Status == WebExceptionStatus.NameResolutionFailure ||
                e.Status == WebExceptionStatus.KeepAliveFailure)
                return "Anki 没有在运行，或者没装 AnkiConnect 插件（插件代码 " + AddonCode + "）。";
            if (e.Status == WebExceptionStatus.Timeout)
                return "AnkiConnect 没有在 20 秒内回话 —— Anki 可能正忙，稍后再点一次。";
            if (e.Response is HttpWebResponse)
                return "AnkiConnect 返回 HTTP " + (int)((HttpWebResponse)e.Response).StatusCode + "。";
            return "连不上 Anki：连接被中断（" + e.Status + "）。";
        }

        // ===== 基本查询 =====
        /// <summary>AnkiConnect 版本号；连不上抛异常。</summary>
        public static int Version()
        {
            object r = Invoke("version");
            int v;
            return int.TryParse(Convert.ToString(r), out v) ? v : 0;
        }

        /// <summary>探活：连得上返回 true，不抛异常。</summary>
        public static bool Ping()
        {
            try { Version(); return true; }
            catch { return false; }
        }

        public static List<string> DeckNames()
        {
            return Strings(Invoke("deckNames"));
        }

        /// <summary>
        /// 把 AnkiConnect 的英文报错翻译成能看懂的中文。没命中就原样返回。
        /// </summary>
        public static string Humanize(string err)
        {
            if (string.IsNullOrEmpty(err)) return err;
            string e = err.Trim();
            if (e.StartsWith("deck was not found", StringComparison.OrdinalIgnoreCase))
                return err + "\n\n牌组不存在，而且自动创建也没成功。请先在 Anki 里建一个同名牌组，再用「选择牌组」挑它。";
            if (e.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
                return err + "\n\n这张卡和牌组里已有的某张重复了，Anki 拒绝了它。改一下内容或换张牌组再存。";
            if (e.StartsWith("model was not found", StringComparison.OrdinalIgnoreCase))
                return err + "\n\n笔记类型不存在，请到「设置 → 输出格式」里改一下当前格式的笔记类型名。";
            if (e.IndexOf("collection is not available", StringComparison.OrdinalIgnoreCase) >= 0)
                return err + "\n\nAnki 里的收藏库没打开（或正被同步/备份占用），把 Anki 窗口打开再试。";
            return err;
        }

        /// <summary>
        /// 牌组是否存在（大小写不敏感）。
        /// 注意：AnkiConnect 的 addNote **不会**自动建牌组，牌组名不存在会直接
        /// 报 "deck was not found"。所以写入前必须自己确认一遍。
        /// </summary>
        public static bool DeckExists(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            List<string> names = DeckNames();
            foreach (string n in names)
                if (string.Equals(n, deck, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>建牌组；已存在时 AnkiConnect 会直接返回既有 id，不会重复建。</summary>
        public static bool CreateDeck(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            var p = new Dictionary<string, object>();
            p["deck"] = deck;
            Invoke("createDeck", p);
            return true;
        }

        /// <summary>确保牌组存在（不存在就建），返回是否可用。</summary>
        public static bool EnsureDeck(string deck)
        {
            if (string.IsNullOrEmpty(deck)) return false;
            if (DeckExists(deck)) return true;
            return CreateDeck(deck);
        }

        public static List<string> ModelNames()
        {
            return Strings(Invoke("modelNames"));
        }

        public static List<string> ModelFieldNames(string model)
        {
            var p = new Dictionary<string, object>();
            p["modelName"] = model;
            return Strings(Invoke("modelFieldNames", p));
        }

        public static List<string> ModelNamesAndIds()
        {
            var p = new Dictionary<string, object>();
            var out_ = new List<string>();
            object r = Invoke("modelNamesAndIds");
            var map = r as Dictionary<string, object>;
            if (map != null) foreach (KeyValuePair<string, object> kv in map) out_.Add(kv.Key + "\t" + kv.Value);
            return out_;
        }

        static List<string> Strings(object r)
        {
            var out_ = new List<string>();
            var arr = r as IEnumerable;
            if (arr == null) return out_;
            foreach (object o in arr) if (!(o is string) || true) out_.Add(Convert.ToString(o));
            return out_;
        }

        /// <summary>按 Anki 查询语法找笔记 id（deck: tag: 都能用）。</summary>
        public static List<long> FindNotes(string query)
        {
            var p = new Dictionary<string, object>();
            p["query"] = query ?? "";
            var out_ = new List<long>();
            var arr = Invoke("findNotes", p) as IEnumerable;
            if (arr != null)
                foreach (object o in arr)
                {
                    long v;
                    if (long.TryParse(Convert.ToString(o), out v)) out_.Add(v);
                }
            return out_;
        }

        public class Note
        {
            public long Id;
            public string Model = "";
            public List<string> Tags = new List<string>();
            public Dictionary<string, string> Fields = new Dictionary<string, string>();
            public int CardCount;
        }

        /// <summary>取笔记详情（字段按 Anki 里的顺序，用 order 排序）。</summary>
        public static List<Note> NotesInfo(List<long> ids)
        {
            var out_ = new List<Note>();
            if (ids == null || ids.Count == 0) return out_;
            var p = new Dictionary<string, object>();
            p["notes"] = ids;

            var arr = Invoke("notesInfo", p) as IEnumerable;
            if (arr == null) return out_;
            foreach (object o in arr)
            {
                var map = o as Dictionary<string, object>;
                if (map == null) continue;
                var n = new Note();
                object v;
                if (map.TryGetValue("noteId", out v)) long.TryParse(Convert.ToString(v), out n.Id);
                if (map.TryGetValue("modelName", out v)) n.Model = Convert.ToString(v);
                if (map.TryGetValue("tags", out v))
                    foreach (string t in Strings(v)) n.Tags.Add(t);

                object fo;
                if (map.TryGetValue("fields", out fo))
                {
                    var fm = fo as Dictionary<string, object>;
                    if (fm != null)
                    {
                        // 每个字段是 {value, order}
                        var ordered = new List<KeyValuePair<int, string>>();
                        foreach (KeyValuePair<string, object> kv in fm)
                        {
                            var detail = kv.Value as Dictionary<string, object>;
                            string val = "";
                            int order = ordered.Count;
                            if (detail != null)
                            {
                                object vv;
                                if (detail.TryGetValue("value", out vv)) val = Convert.ToString(vv);
                                if (detail.TryGetValue("order", out vv))
                                {
                                    int.TryParse(Convert.ToString(vv), out order);
                                }
                            }
                            ordered.Add(new KeyValuePair<int, string>(order, kv.Key));
                        }
                        ordered.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b)
                        { return a.Key.CompareTo(b.Key); });
                        foreach (KeyValuePair<int, string> kv in ordered)
                        {
                            var detail = fm[kv.Value] as Dictionary<string, object>;
                            object vv;
                            string val = "";
                            if (detail != null && detail.TryGetValue("value", out vv)) val = Convert.ToString(vv);
                            n.Fields[kv.Value] = val;
                        }
                    }
                }
                if (map.TryGetValue("cards", out v))
                {
                    var cs = v as IEnumerable;
                    if (cs != null) foreach (object c in cs) { if (c is string) continue; n.CardCount++; }
                }
                out_.Add(n);
            }
            return out_;
        }

        // ===== 写操作 =====
        public static bool NoteTypeExists(string model)
        {
            foreach (string m in ModelNames()) if (m == model) return true;
            return false;
        }

        /// <summary>建笔记类型（模型）；已存在返回 false，不做任何改动。</summary>
        public static bool CreateModel(string model, List<string> fields, string front, string back, string css)
        {
            if (NoteTypeExists(model)) return false;
            var p = new Dictionary<string, object>();
            p["modelName"] = model;
            p["inOrderFields"] = fields;
            p["css"] = css ?? "";
            var cardTemplates = new List<object>();
            var t = new Dictionary<string, object>();
            t["Name"] = "Card 1";
            t["Front"] = front;
            t["Back"] = back;
            cardTemplates.Add(t);
            p["cardTemplates"] = cardTemplates;

            // createModel 的 params 是**一层**的：modelName / inOrderFields / css / cardTemplates 直接放在里面。
            Invoke("createModel", p);
            return true;
        }

        /// <summary>加一条笔记，返回 noteId。</summary>
        public static long AddNote(string deck, string model, Dictionary<string, string> fields, List<string> tags)
        {
            var note = new Dictionary<string, object>();
            note["deckName"] = deck;
            note["modelName"] = model;
            note["fields"] = fields;
            note["tags"] = tags ?? new List<string>();
            var opts = new Dictionary<string, object>();
            opts["allowDuplicate"] = true;
            opts["duplicateScope"] = "deck";
            note["options"] = opts;

            // addNote 的 params 只有一层：整条笔记必须包在 "note" 里
            // （官方动作表：params = {"note": {deckName, modelName, fields, options, tags}}）。
            var p = new Dictionary<string, object>();
            p["note"] = note;

            object r = Invoke("addNote", p);
            long id;
            long noteId = long.TryParse(Convert.ToString(r), out id) ? id : 0;
            // 有些 AnkiConnect 版本 addNote 里写的是 collection.addNote(ankiNote) —— 没把牌组
            // 传下去，Anki 就按「当前选中的牌组」落卡（本机实测三张卡全落进了「系统默认」，
            // note 里的 deckName 被完全忽略）。补一次 changeDeck 才能保证卡片真的进对牌组。
            if (noteId > 0) MoveCardsToDeck(noteId, deck);
            return noteId;
        }

        /// <summary>
        /// 把一条笔记的卡片挪到指定牌组（addNote 之后的补救，见上面的注释）。
        /// </summary>
        public static void MoveCardsToDeck(long noteId, string deck)
        {
            if (noteId <= 0 || string.IsNullOrEmpty(deck)) return;
            var q = new Dictionary<string, object>();
            q["query"] = "nid:" + noteId;
            var cards = new List<long>();
            var arr = Invoke("findCards", q) as IEnumerable;
            if (arr != null)
                foreach (object o in arr)
                {
                    long v;
                    if (long.TryParse(Convert.ToString(o), out v)) cards.Add(v);
                }
            if (cards.Count == 0) return;
            var p = new Dictionary<string, object>();
            p["cards"] = cards;
            p["deck"] = deck;
            Invoke("changeDeck", p);
        }

        public static void DeleteNotes(List<long> ids)
        {
            if (ids == null || ids.Count == 0) return;
            var p = new Dictionary<string, object>();
            p["notes"] = ids;
            Invoke("deleteNotes", p);
        }

        /// <summary>触发 Anki 自己同步到 AnkiWeb（等同于在 Anki 里点同步）。</summary>
        public static void Sync()
        {
            Invoke("sync");
        }

        /// <summary>卡片到期数量（首页看板用得上）。</summary>
        public static int DueCount(string deck)
        {
            string q = string.IsNullOrEmpty(deck) ? "is:due" : "deck:\"" + deck + "\" is:due";
            return FindNotes(q).Count;
        }

        /// <summary>把一张本地图片塞进 Anki 的媒体库，返回文件名（头像同步用）。</summary>
        public static string StoreMediaFile(string fileName, byte[] data)
        {
            var p = new Dictionary<string, object>();
            p["filename"] = fileName;
            p["data"] = Convert.ToBase64String(data);
            object r = Invoke("storeMediaFile", p);
            return Convert.ToString(r);
        }

        public static byte[] RetrieveMediaFile(string fileName)
        {
            var p = new Dictionary<string, object>();
            p["filename"] = fileName;
            try
            {
                object r = Invoke("retrieveMediaFile", p);
                string b64 = Convert.ToString(r);
                if (string.IsNullOrEmpty(b64) || b64 == "False" || b64 == "false") return null;
                return Convert.FromBase64String(b64);
            }
            catch { return null; }
        }
    }
}
