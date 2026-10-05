using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>
    /// 输出格式（卡片配置）的存放处：内置两套 + 用户自建，存在 settings.ini 的 configs.json 里。
    ///
    /// 内置格式也允许改：改过的内置会用 JSON 里那份覆盖出厂值（记在 _overridden 里），
    /// 想还原就用 <see cref="Reset"/> 丢掉覆盖。自建的直接删。
    /// </summary>
    public static class Configs
    {
        static List<CardConfig> _all;
        static readonly HashSet<string> _overridden = new HashSet<string>();
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        /// <summary>这个 id 是不是内置格式。</summary>
        public static bool IsBuiltin(string id)
        {
            foreach (CardConfig b in CardConfig.Builtins()) if (b.Id == id) return true;
            return false;
        }

        /// <summary>内置格式是否被用户改过（改过才能「还原」）。</summary>
        public static bool IsOverridden(string id)
        {
            Ensure();
            return _overridden.Contains(id);
        }

        static void Ensure()
        {
            if (_all != null) return;
            _all = CardConfig.Builtins();

            string raw = Store.ConfigsJson;
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                var arr = Json.DeserializeObject(raw) as System.Collections.IEnumerable;
                if (arr == null) return;
                foreach (object o in arr)
                {
                    if (o is string) continue;
                    var map = o as Dictionary<string, object>;
                    if (map == null) continue;
                    CardConfig c = CardConfig.FromJson(map);
                    if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                    int idx = _all.FindIndex(delegate(CardConfig x) { return x.Id == c.Id; });
                    if (idx >= 0) { _all[idx] = c; _overridden.Add(c.Id); }   // 覆盖内置
                    else _all.Add(c);                                          // 自建
                }
            }
            catch { }
        }

        /// <summary>全部格式：内置的在前，用户自建的在后。</summary>
        public static List<CardConfig> All()
        {
            Ensure();
            return _all;
        }

        static void Flush()
        {
            var arr = new List<object>();
            foreach (CardConfig c in _all)
            {
                bool builtin = IsBuiltin(c.Id);
                if (builtin && !_overridden.Contains(c.Id)) continue;
                arr.Add(c.ToJson());
            }
            Store.ConfigsJson = Json.Serialize(arr);
        }

        public static CardConfig Get(string id)
        {
            foreach (CardConfig c in All()) if (c.Id == id) return c;
            return null;
        }

        /// <summary>当前使用的格式。存的是 id，取不到就回到第一个（默认）。</summary>
        public static CardConfig Active
        {
            get
            {
                string id = Store.ActiveConfigId;
                CardConfig c = string.IsNullOrEmpty(id) ? null : Get(id);
                if (c == null)
                {
                    c = All()[0];
                    if (Store.ActiveConfigId != c.Id) Store.ActiveConfigId = c.Id;
                }
                return c;
            }
            set
            {
                if (value != null) Store.ActiveConfigId = value.Id;
            }
        }

        public static void Save(CardConfig c)
        {
            if (c == null || string.IsNullOrEmpty(c.Id)) return;
            List<CardConfig> list = All();
            int idx = list.FindIndex(delegate(CardConfig x) { return x.Id == c.Id; });
            if (idx >= 0) list[idx] = c; else list.Add(c);
            if (IsBuiltin(c.Id)) _overridden.Add(c.Id);
            Flush();
        }

        /// <summary>内置格式还原出厂；自建的等于删除。</summary>
        public static void Reset(string id)
        {
            List<CardConfig> list = All();
            int idx = list.FindIndex(delegate(CardConfig x) { return x.Id == id; });
            if (idx < 0) return;
            foreach (CardConfig b in CardConfig.Builtins())
            {
                if (b.Id == id)
                {
                    list[idx] = b;
                    _overridden.Remove(id);
                    Flush();
                    return;
                }
            }
            Delete(id);
        }

        /// <summary>删自建格式（内置的不给删，只能还原）。</summary>
        public static void Delete(string id)
        {
            if (IsBuiltin(id)) return;
            List<CardConfig> list = All();
            list.RemoveAll(delegate(CardConfig x) { return x.Id == id; });
            Flush();
            if (Store.ActiveConfigId == id) Store.ActiveConfigId = All()[0].Id;
        }

        /// <summary>新建一个空壳格式（id 用时间戳，不会和内置撞车）。</summary>
        public static CardConfig NewBlank(string name)
        {
            var c = new CardConfig();
            c.Id = "u" + DateTime.Now.ToString("yyMMddHHmmss");
            c.Name = string.IsNullOrEmpty(name) ? "新格式" : name;
            c.NoteType = "专业术语卡";
            c.Css = CardConfig.BuiltinCss;
            c.SystemPrompt = CardConfig.BuiltinSystemPrompt;
            c.DefaultDeck = "";
            c.DefaultTags = "";
            c.Subject = "";
            c.Fields = new List<Field>();
            c.Fields.Add(new Field("单词", "word", "要制卡的那个词", false));
            c.Fields.Add(new Field("中文", "cn", "中文释义", false));
            return c;
        }
    }
}
