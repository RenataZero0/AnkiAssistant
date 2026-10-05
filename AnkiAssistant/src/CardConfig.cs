using System;
using System.Collections;
using System.Collections.Generic;

namespace AnkiAssistant
{
    // ============================================================ 字段

    /// <summary>
    /// 卡片上的一个字段。
    ///
    /// name  卡片上的字段名（同时也是 Anki 笔记类型的字段名）
    /// key   AI 返回的 JSON 键
    /// hint  编辑框里的灰色提示（可空）
    /// latex 该字段按公式处理（把 $..$ 归一成 \(..\)、给裸 LaTeX 包上 \( \)）
    /// </summary>
    public class Field
    {
        public string Name;
        public string Key;
        public string Hint;
        public bool Latex;

        public Field(string name, string key, string hint, bool latex)
        {
            this.Name = name;
            this.Key = key;
            this.Hint = hint;
            this.Latex = latex;
        }
    }

    // ============================================================ 输出格式 config

    /// <summary>
    /// 一套「输出格式 config」。
    ///
    /// 作用：决定 **AI 按什么格式产出** 以及 **卡片有哪些字段**。
    ///   · 内置的默认 config 是英语词汇（词 + 音标 + 中文 + 例句，4 个字段）
    ///   · 另有 A Level 数学/物理术语格式（7 个字段）
    ///   · 用户可以新建自己的 config：起个名字、写字段清单、写提示词，然后选用它
    ///
    /// 第一个字段约定为卡片的「正面」，由用户输入（AI 不用产出它）。
    ///
    /// 本文件是 Android 版 com.ankiassistant.CardConfig 的 C# 移植版，
    /// 提示词 / 字段名 / 提示文字 / CSS / 卡片模板与原版逐字一致，不要翻译或改写。
    /// </summary>
    public class CardConfig
    {
        // ------------------------------------------------------------ 基础

        /// <summary>内置格式 id（英语词汇，也是默认格式）</summary>
        public const string BuiltinId = "default";

        /// <summary>内置格式 id：英语词汇（默认）</summary>
        public const string IdVocabDefault = "default";

        /// <summary>内置格式 id：A Level Maths / Phy</summary>
        public const string IdAlevel = "alevel";

        /// <summary>用户可见的 config 名（也用作自定义笔记类型的名字）</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>写进 Anki 的笔记类型名</summary>
        public string NoteType = "";
        /// <summary>模板样式（写进笔记类型，桌面端渲染用的是同一段 CSS）</summary>
        public string Css = "";
        /// <summary>系统提示词（会话里 role=system 的那条）</summary>
        public string SystemPrompt = "";
        /// <summary>格式说明提示词，可含 {word} 与 {subject} 占位符</summary>
        public string Prompt = "";
        /// <summary>字段清单（第 0 个是卡片正面，由用户输入）</summary>
        public List<Field> Fields = new List<Field>();

        /// <summary>
        /// 这套 config 自己的默认值（不同 config 可以不一样）：
        /// 留空表示沿用应用级默认，这样老用户升级后行为不变。
        /// </summary>
        public string DefaultDeck = "";
        public string DefaultTags = "";
        public string Subject = "";

        // ------------------------------------------------------------ 应用级默认值（内联自 Android 的 Store.java）

        // Store.java:241  public static final String DEFAULT_DECK = "A Level Pure Mathematics";
        /// <summary>应用级默认牌组（内联自 Store.java 的 DEFAULT_DECK）</summary>
        public const string AppDefaultDeck = "A Level Pure Mathematics";

        // Store.java:244  public String defaultTags() { return sp.getString("defaultTags", "ALevel::Maths"); }
        /// <summary>应用级默认标签（内联自 Store.java 的 defaultTags() 默认值）</summary>
        public const string AppDefaultTags = "ALevel::Maths";

        // Store.java:233  public static final String DEFAULT_SUBJECT = "A Level 数学与物理（纯数 / 力学 / 概率统计）";
        /// <summary>应用级默认学科背景（内联自 Store.java 的 DEFAULT_SUBJECT）</summary>
        public const string AppDefaultSubject = "A Level 数学与物理（纯数 / 力学 / 概率统计）";

        // ------------------------------------------------------------ 共用样式与系统提示词

        /// <summary>模板样式（写进笔记类型，桌面端渲染用的是同一段 CSS）</summary>
        public const string BuiltinCss =
            ".card{font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,"
            + "'PingFang SC','Microsoft YaHei',sans-serif;font-size:20px;line-height:1.65;"
            + "text-align:left;color:#1b2432;background:#ffffff;padding:4px;}\n"
            + ".word{font-size:30px;font-weight:700;letter-spacing:.5px;}\n"
            + "hr#answer{border:none;border-top:2px solid #e5e9f0;margin:14px 0;}\n"
            + ".row{margin:9px 0;}\n"
            + ".lbl{color:#3568e8;font-weight:700;margin-right:8px;white-space:nowrap;}\n"
            + ".val{color:#1b2432;}\n"
            + "code{background:#f3f5f9;border-radius:4px;padding:1px 5px;}\n";

        /// <summary>系统提示词：只让它吐一个 JSON 对象</summary>
        public const string BuiltinSystemPrompt =
            "你是资深的英汉词典编辑，熟悉 A Level 数学与物理的术语与符号。"
            + "你只输出一个 JSON 对象，不输出任何解释、注释或 Markdown 代码块。";

        // ------------------------------------------------------------ 三个内置格式（都不可修改）

        /// <summary>A Level 那套是否用应用级默认值（是：它沿用原来的默认牌组/标签/学科背景）</summary>
        public static bool UsesAppDefaults(string id)
        {
            return IdAlevel.Equals(id);
        }

        /// <summary>全部内置格式，**顺序就是界面里的顺序**（英语词汇在最上面，是默认格式）</summary>
        public static List<CardConfig> Builtins()
        {
            List<CardConfig> outp = new List<CardConfig>();
            outp.Add(VocabConfig());
            outp.Add(AlevelConfig());
            return outp;
        }

        /// <summary>A Level Maths / Phy：原来的七字段术语卡（默认值沿用应用级设置）</summary>
        public static CardConfig AlevelConfig()
        {
            CardConfig c = new CardConfig();
            c.Id = IdAlevel;
            c.Name = "A Level Maths / Phy";
            c.NoteType = "专业术语卡";
            c.Fields.Add(new Field("单词", "", "", false));
            c.Fields.Add(new Field("音标", "phonetic", "英 /…/；美 /…/", false));
            c.Fields.Add(new Field("词性", "pos", "n / adj / v（多个用 / 连接）", false));
            c.Fields.Add(new Field("定义", "definition", "(n) 英文释义; (v) 另一个义项", false));
            c.Fields.Add(new Field("关联公式/符号", "formula", "行内 MathJax，写成 \\( … \\)", true));
            c.Fields.Add(new Field("易混", "confusables", "每行一个：词 /音标/ 词性 中文释义", false));
            c.Fields.Add(new Field("中文", "chinese", "多个义项用「；」分隔", false));
            // defaultDeck / defaultTags / subject 留空 → 用应用级默认值（与升级前行为一致）
            c.Prompt = DefaultPrompt;
            c.Css = BuiltinCss;
            c.SystemPrompt = BuiltinSystemPrompt;
            return c;
        }

        /// <summary>英语词汇：轻量记忆卡（词 + 音标 + 中文 + 例句）</summary>
        public static CardConfig VocabConfig()
        {
            CardConfig c = new CardConfig();
            c.Id = IdVocabDefault;
            c.Name = "英语词汇";
            c.NoteType = "英语词汇卡";
            c.Fields.Add(new Field("单词", "", "", false));
            c.Fields.Add(new Field("音标", "phonetic", "英 /…/；美 /…/", false));
            c.Fields.Add(new Field("中文", "chinese", "中文释义", false));
            c.Fields.Add(new Field("例句", "example", "一句包含该词的英文例句", false));
            c.DefaultDeck = "英语词汇";
            c.DefaultTags = "English::Vocab";
            c.Subject = "通用英语（日常与学术都适用）";
            c.Prompt = VocabPrompt;
            c.Css = BuiltinCss;
            c.SystemPrompt = BuiltinSystemPrompt;
            return c;
        }

        // ------------------------------------------------------------ 内置提示词

        /// <summary>英语词汇的提示词（更短，只要词义与例句）</summary>
        public const string VocabPrompt =
            "请为「{word}」生成一张英语词汇卡。\n"
            + "学科背景：{subject}。\n"
            + "\n"
            + "只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：\n"
            + "phonetic：音标，英式与美式都给（英 /.../；美 /.../）。\n"
            + "chinese：中文释义，简洁，多个义项用「；」分隔。\n"
            + "example：一个完整、地道的英文例句，必须包含这个词；句尾加句号。\n"
            + "\n"
            + "示例：\n"
            + "{\"phonetic\":\"英 /ˈæpl/；美 /ˈæpl/\",\"chinese\":\"苹果\","
            + "\"example\":\"He ate an apple for breakfast.\"}";

        /// <summary>默认提示词：与用户 Anki 里已有卡片一致（英/美音标、([词性]) 释义、行内 MathJax、≠ 易混）</summary>
        public const string DefaultPrompt =
            "请为「{word}」生成一张词卡。\n"
            + "学科背景：{subject}。\n"
            + "\n"
            + "只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：\n"
            + "phonetic：音标，英式与美式都给，写成 英 /.../；美 /.../ ；只有一种读音时两边写一样。\n"
            + "pos：词性缩写，多个用 / 连接，例如 adj/n、v、n，不要加点号。\n"
            + "definition：英文释义。严格按这个格式：([词性]) 英文释义; ([词性]) 另一个释义\n"
            + "        例如：(adj) relating to the second power of a variable; (n) a quadratic polynomial of degree 2\n"
            + "        若是数学/物理术语，用英文给出教材级别的精确定义。\n"
            + "formula：与该词相关的公式或符号表示。**只用行内 MathJax \\( ... \\)**，"
            + "不要使用 \\[ ... \\] 这种独立成行的公式；同一行多个式子用 \\quad 或逗号分隔。"
            + "没有相关公式就留空字符串。\n"
            + "confusables：2-3 个读音或意义相近、容易混淆的词，每个写成一行，格式固定为：\n"
            + "        词 /音标/ 词性缩写 中文释义\n"
            + "        词性一律用英文缩写（n. / v. / adj. / adv. / prep. 等），"
            + "并且**不要用破折号或连字符**把词和释义连起来。\n"
            + "chinese：该词（或该术语）的中文释义，多个用「；」分隔。\n"
            + "\n"
            + "输出示例（注意：confusables 每行是「词 /音标/ 词性 中文释义」，没有破折号）：\n"
            + "{\"phonetic\":\"英 /æbˈsɪsə/；美 /æbˈsɪsə/\",\"pos\":\"n\","
            + "\"definition\":\"(n) the horizontal coordinate of a point in a coordinate system\","
            + "\"formula\":\"\\\\(x\\\\neq 0,\\\\ y=0\\\\)\","
            + "\"confusables\":\"ordinate /ˈɔːdɪnət/ n. 纵坐标，竖直方向的坐标\\n"
            + "coordinate /kəʊˈɔːdɪnət/ n. 坐标，用来定位的一对数值\","
            + "\"chinese\":\"横坐标\"}";

        // ------------------------------------------------------------ 内置默认

        /// <summary>默认格式 = 英语词汇</summary>
        public static CardConfig DefaultConfig()
        {
            return VocabConfig();
        }

        // ------------------------------------------------------------ 读写

        /// <summary>从 JSON 对象还原一套 config（字段缺失时用与原版相同的兜底值）</summary>
        public static CardConfig FromJson(Dictionary<string, object> o)
        {
            CardConfig c = new CardConfig();
            if (o == null) return c;
            c.Id = GetString(o, "id", "");
            c.Name = GetString(o, "name", "");
            c.NoteType = GetString(o, "noteType", c.Name);
            c.Prompt = GetString(o, "prompt", DefaultPrompt);
            c.Css = GetString(o, "css", BuiltinCss);
            c.SystemPrompt = GetString(o, "systemPrompt", BuiltinSystemPrompt);
            c.DefaultDeck = GetString(o, "defaultDeck", "");
            c.DefaultTags = GetString(o, "defaultTags", "");
            c.Subject = GetString(o, "subject", "");
            List<object> fs = AsList(o.ContainsKey("fields") ? o["fields"] : null);
            if (fs != null)
            {
                for (int i = 0; i < fs.Count; i++)
                {
                    Dictionary<string, object> f = AsObject(fs[i]);
                    if (f == null) continue;
                    c.Fields.Add(new Field(GetString(f, "name", "字段" + (i + 1)),
                            GetString(f, "key", "f" + i),
                            GetString(f, "hint", ""),
                            GetBool(f, "latex", false)));
                }
            }
            if (c.Fields.Count == 0)
            {
                // 用默认格式的字段（复制一份，别把内置实例的 List 挂到别人身上）
                List<Field> defs = DefaultConfig().Fields;
                for (int i = 0; i < defs.Count; i++) c.Fields.Add(defs[i]);
            }
            if (c.NoteType == null || c.NoteType.Trim().Length == 0) c.NoteType = c.Name;
            return c;
        }

        /// <summary>导出一套 config 的 JSON 对象（用 JavaScriptSerializer 序列化成文本）</summary>
        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            try
            {
                o["id"] = Id;
                o["name"] = Name;
                o["noteType"] = NoteType;
                o["prompt"] = Prompt;
                o["css"] = Css == null ? "" : Css;
                o["systemPrompt"] = SystemPrompt == null ? "" : SystemPrompt;
                o["defaultDeck"] = DefaultDeck == null ? "" : DefaultDeck;
                o["defaultTags"] = DefaultTags == null ? "" : DefaultTags;
                o["subject"] = Subject == null ? "" : Subject;
                List<object> fs = new List<object>();
                for (int i = 0; i < Fields.Count; i++)
                {
                    Field f = Fields[i];
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["name"] = f.Name;
                    x["key"] = f.Key;
                    x["hint"] = f.Hint;
                    x["latex"] = f.Latex;
                    fs.Add(x);
                }
                o["fields"] = fs;
            }
            catch (Exception)
            {
                // 与原版一致：序列化出错就交回已经装好的部分
            }
            return o;
        }

        // ------------------------------------------------------------ 生成卡片相关

        /// <summary>字段名数组（也就是 Anki 笔记类型的字段顺序）</summary>
        public string[] FieldNames()
        {
            string[] outp = new string[Fields.Count];
            for (int i = 0; i < outp.Length; i++) outp[i] = Fields[i].Name;
            return outp;
        }

        /// <summary>AI 需要产出的键（第 0 个字段是用户输入的正面，不算）</summary>
        public string[] AiKeys()
        {
            List<string> keys = new List<string>();
            for (int i = 1; i < Fields.Count; i++)
            {
                if (Fields[i].Key != null && Fields[i].Key.Length > 0)
                {
                    keys.Add(Fields[i].Key);
                }
            }
            return keys.ToArray();
        }

        /// <summary>生效的默认牌组（本 config 没写就沿用应用级默认）</summary>
        public string DeckOr(string appDefault)
        {
            return (DefaultDeck != null && DefaultDeck.Trim().Length > 0) ? DefaultDeck.Trim() : appDefault;
        }

        /// <summary>生效的默认标签（本 config 没写就沿用应用级默认）</summary>
        public string TagsOr(string appDefault)
        {
            return (DefaultTags != null && DefaultTags.Trim().Length > 0) ? DefaultTags.Trim() : appDefault;
        }

        /// <summary>生效的学科背景（本 config 没写就沿用应用级默认）</summary>
        public string SubjectOr(string appDefault)
        {
            return (Subject != null && Subject.Trim().Length > 0) ? Subject.Trim() : appDefault;
        }

        /// <summary>卡片正面模板</summary>
        public string CardFront()
        {
            return "<div class=\"word\">{{" + Fields[0].Name + "}}</div>";
        }

        /// <summary>背面：每个字段一行【字段名】值，空字段连标签一起隐藏（与默认格式视觉一致）</summary>
        public string CardBack()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(CardFront());
            sb.Append("\n<hr id=\"answer\">\n<div class=\"body\">\n");
            for (int i = 1; i < Fields.Count; i++)
            {
                string n = Fields[i].Name;
                sb.Append("{{#").Append(n).Append("}}<div class=\"row\">")
                  .Append("<span class=\"lbl\">【").Append(n).Append("】</span>")
                  .Append("<span class=\"val\">{{").Append(n).Append("}}</span></div>{{/").Append(n).Append("}}\n");
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>把 config 的提示词模板填上实际值</summary>
        public string BuildPrompt(string word, string subject)
        {
            string subj = (subject == null || subject.Trim().Length == 0)
                    ? "数学、物理学科术语（若是学科术语请给出精确定义）" : subject.Trim();
            string p = Prompt == null ? "" : Prompt;
            return p.Replace("{word}", word == null ? "" : word.Trim())
                    .Replace("{subject}", subj);
        }

        /// <summary>重试用的提示词：再要一次，明确不许有思考过程或代码块</summary>
        public string BuildPromptStrict(string word, string subject)
        {
            return BuildPrompt(word, subject)
                    + "\n\n重要：不要输出任何思考过程、解释或 Markdown 代码块，"
                    + "只输出上面那个 JSON 对象本身，且必须是可以直接解析的完整 JSON。";
        }

        // ------------------------------------------------------------ JSON 兼容小工具

        /// <summary>
        /// 取字符串值：兼容 JavaScriptSerializer 反序列化出来的各种类型
        /// （string / int / bool / null）。对应 Java 里的 JSONObject.optString。
        /// </summary>
        internal static string GetString(Dictionary<string, object> o, string key, string def)
        {
            object v;
            if (!TryGetExactKey(o, key, out v)) return def;
            if (v == null) return def;
            if (v is string) return (string)v;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>取布尔值，对应 Java 里的 JSONObject.optBoolean</summary>
        internal static bool GetBool(Dictionary<string, object> o, string key, bool def)
        {
            object v;
            if (!TryGetExactKey(o, key, out v)) return def;
            if (v == null) return def;
            if (v is bool) return (bool)v;
            string s = v as string;
            if (s != null)
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                return def;
            }
            try { return Convert.ToBoolean(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return def; }
        }

        /// <summary>
        /// 大小写敏感的取值：JavaScriptSerializer 反序列化出来的 Dictionary 用的是
        /// OrdinalIgnoreCase 比较器，而 Java 的 JSONObject 是大小写敏感的，所以要自己扫一遍。
        /// </summary>
        private static bool TryGetExactKey(Dictionary<string, object> o, string key, out object val)
        {
            val = null;
            if (o == null || key == null) return false;
            foreach (KeyValuePair<string, object> kv in o)
            {
                if (string.Equals(kv.Key, key, StringComparison.Ordinal))
                {
                    val = kv.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>把 JSON 里的值当成数组（ArrayList / object[] / List）取出来</summary>
        internal static List<object> AsList(object v)
        {
            if (v == null) return null;
            List<object> l = v as List<object>;
            if (l != null) return l;
            ArrayList a = v as ArrayList;
            if (a != null) return new List<object>(a.ToArray());
            object[] arr = v as object[];
            if (arr != null) return new List<object>(arr);
            return null;
        }

        /// <summary>把 JSON 里的值当成对象取出来；不是对象就返回 null</summary>
        internal static Dictionary<string, object> AsObject(object v)
        {
            return v as Dictionary<string, object>;
        }
    }
}
