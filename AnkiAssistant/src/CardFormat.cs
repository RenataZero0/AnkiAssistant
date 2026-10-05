// 注意：本类里有一个 System() 属性（对应 Java 的常量 SYSTEM），它会遮住 System 命名空间，
// 所以这里用别名 CsSystem 指代 System，凡是命名空间前缀都写成 CsSystem.xxx
using CsSystem = System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    // ============================================================ 卡片格式规范

    /// <summary>
    /// 卡片格式规范 —— 本应用的"唯一事实来源"。
    ///
    /// 需求（PRMOPT.md 第二节第 3 条）：
    ///   正面：[输入的单词]
    ///   背面：
    ///     【音标】[此单词的音标]
    ///     【词性】[此单词的词性]
    ///     【定义】([词性]) [英文释义]; ([若有多个词性]) [此词性对应的释义]
    ///     【关联公式/符号】[相关公式]（使用 Anki 内置 MathJax 格式）
    ///     【易混】[相近单词]（列出读音与意义相近的词）
    ///
    /// 这里同时负责三件事：
    ///   1) 解析 AI 的返回（parseAi / repairJson）—— 可能带 ```json 围栏、可能啰嗦
    ///   2) 组装成 Anki 的笔记字段（noteFieldsFor / mergeNoteFor / emptyFieldsFor）
    ///   3) 公式与 HTML 的归一化（normalizeFormula / escape）
    /// 卡片模板 / 样式在 CardConfig 里按这套 config 生成，保证「桌面端渲染」与「应用内预览」完全一致。
    ///
    /// 本文件是 Android 版 com.ankiassistant.CardFormat 的 C# 移植版。
    /// Android 用的是 org.json，这里换成 Dictionary&lt;string, object&gt; +
    /// System.Web.Script.Serialization（解析一律 try/catch，兜底行为与原版一致）。
    /// </summary>
    public static class CardFormat
    {
        // ------------------------------------------------------------ 常量

        /// <summary>写进 Anki 的笔记类型名（默认 config）</summary>
        public static string ModelName()
        {
            return CardConfig.DefaultConfig().NoteType;
        }

        /// <summary>字段顺序（也是背面从上到下的顺序）。中文行是参考用户现有卡片补的，可留空</summary>
        public static string[] Fields()
        {
            return CardConfig.DefaultConfig().FieldNames();
        }

        /// <summary>系统提示词（默认 config 的版本；多格式请用 CardConfig.SystemPrompt）</summary>
        public static string System()
        {
            return CardConfig.BuiltinSystemPrompt;
        }

        /// <summary>卡片正面模板（默认 config）</summary>
        public static string CardFront()
        {
            return CardConfig.DefaultConfig().CardFront();
        }

        /// <summary>卡片背面模板（默认 config）：每个字段一行，空字段连标签一起隐藏</summary>
        public static string CardBack()
        {
            return CardConfig.DefaultConfig().CardBack();
        }

        /// <summary>模板样式（写进笔记类型，桌面端渲染用的是同一段 CSS）</summary>
        public static string CardCss()
        {
            return CardConfig.BuiltinCss;
        }

        /// <summary>
        /// 按牌组自动带出的标签：数学类四个牌组 → ALevel::Maths，物理 → ALevel::Physics。
        /// 其它牌组返回 null（不动用户自己填的标签）。
        /// </summary>
        public static string TagsForDeck(string deck)
        {
            if (deck == null) return null;
            string d = deck.Trim();
            if (d.Length == 0) return null;
            if (d.Equals("A Level Physics", CsSystem.StringComparison.OrdinalIgnoreCase)) return "ALevel::Physics";
            if (d.StartsWith("A Level Mathematics", CsSystem.StringComparison.Ordinal)
                    || d.StartsWith("A Level Further Mathematics", CsSystem.StringComparison.Ordinal)
                    || d.StartsWith("A Level Pure Mathematics", CsSystem.StringComparison.Ordinal)
                    || d.StartsWith("A Level Statistics", CsSystem.StringComparison.Ordinal))
            {
                return "ALevel::Maths";
            }
            return null;
        }

        // ------------------------------------------------------------ 提示词

        /// <summary>用户提示词（默认 config 的版本；多格式请用 CardConfig.BuildPrompt）</summary>
        public static string BuildPrompt(string word, string subject)
        {
            return CardConfig.DefaultConfig().BuildPrompt(word, subject);
        }

        /// <summary>
        /// 重试用的提示词：第一次没解析出 JSON 时再要一次。
        /// 思考型模型在长推理后容易"发挥"（加解释、加代码块、甚至把 JSON 写截断），
        /// 这里明确要求不要思考、不要解释。
        /// </summary>
        public static string BuildPromptStrict(string word, string subject)
        {
            return CardConfig.DefaultConfig().BuildPromptStrict(word, subject);
        }

        // ------------------------------------------------------------ AI 返回解析

        /// <summary>
        /// 解析 AI 的原始返回。容忍：```json 围栏、前后缀废话、中文键名。
        /// 解析不出来返回 null（调用方自行降级）。
        /// </summary>
        public static Dictionary<string, object> ParseAi(string raw)
        {
            if (raw == null) return null;
            string s = StripFences(raw.Trim());
            Dictionary<string, object> o = TryObject(s);
            if (o == null)
            {
                int a = s.IndexOf('{'), b = s.LastIndexOf('}');
                if (a >= 0 && b > a) o = TryObject(s.Substring(a, b - a + 1));
            }
            // 模型常见的"写坏 JSON"：中文值前面用全角冒号、少个引号。实测开/关思考都会出现。
            if (o == null) o = TryObject(RepairJson(s));
            if (o == null)
            {
                int a = s.IndexOf('{'), b = s.LastIndexOf('}');
                if (a >= 0 && b > a) o = TryObject(RepairJson(s.Substring(a, b - a + 1)));
            }
            // 还是不行就按字段名硬抠（不依赖 JSON 语法）
            if (o == null) o = LooseExtract(s);
            if (o == null) return null;
            Dictionary<string, object> outp = new Dictionary<string, object>();
            Put(outp, "phonetic", o, new string[] { "phonetic", "音标", "pronunciation", "ipa" });
            Put(outp, "pos", o, new string[] { "pos", "词性", "partOfSpeech", "partsOfSpeech", "speech" });
            Put(outp, "definition", o, new string[] { "definition", "def", "释义", "定义", "meaning", "english" });
            Put(outp, "formula", o, new string[] { "formula", "math", "symbol", "公式", "关联公式", "关联公式/符号" });
            Put(outp, "confusables", o, new string[] { "confusables", "confusable", "similar", "易混", "易混词", "近义词" });
            Put(outp, "chinese", o, new string[] { "chinese", "cn", "中文", "中文释义", "translation", "meaningCn" });
            // 原版的 put 无论取没取到值都会写一个键进去（取不到就写空串），所以即使六个规范字段
            // 全空，返回的也不是空对象。这里补齐同样的键，保证与原版逐字节一致。
            EnsureKeys(outp, new string[] { "phonetic", "pos", "definition", "formula", "confusables", "chinese" });
            if (IsNullAll(outp)) return null;
            // 上面那六个键是「A Level 术语卡」的规范字段（做了别名归一）。但自定义 config
            // 可以用任意 AI 键（内置的「英语词汇」就用 example 取例句，不在那六个里面），
            // 所以把 AI 返回里其余的键原样带上；同名的以规范化的值为准。
            foreach (KeyValuePair<string, object> kv in o)
            {
                if (!outp.ContainsKey(kv.Key)) outp[kv.Key] = kv.Value;
            }
            return outp;
        }

        /// <summary>
        /// 修补模型偶尔写坏的 JSON：
        ///   "chinese"："速度"   → 全角冒号
        ///   "chinese：速度"     → 键后面少了引号
        ///   “chinese”           → 弯引号
        /// 只动"键和冒号"这一小段，不碰值里面的中文标点（值里的「，」「：」是正常文案）。
        /// </summary>
        public static string RepairJson(string s)
        {
            if (s == null) return "";
            string t = s.Replace('\u201C', '"').Replace('\u201D', '"');
            // "键"： 后面紧跟引号 → 只把全角冒号换成半角
            t = Regex.Replace(t, "\"\\s*\uFF1A\\s*(?=\")", "\":");
            // "键"：后面不是引号 → 补上值的起始引号
            t = Regex.Replace(t, "\"\\s*\uFF1A\\s*", "\":\"");
            // "键：值" → 键的收尾引号丢了，补上（键只含 ASCII 字母/下划线）
            t = Regex.Replace(t, "\"([A-Za-z][A-Za-z0-9_]*)\\s*\uFF1A\\s*(?=\")", "\"$1\":\"");
            t = Regex.Replace(t, "\"([A-Za-z][A-Za-z0-9_]*)\\s*\uFF1A\\s*", "\"$1\":\"");
            return t;
        }

        /// <summary>最后的兜底：JSON 语法实在修不好时，按字段名把值抠出来</summary>
        private static Dictionary<string, object> LooseExtract(string s)
        {
            if (s == null) return null;
            string[][] groups = new string[][] {
                    new string[] { "phonetic", "音标", "pronunciation", "ipa" },
                    new string[] { "pos", "词性", "partOfSpeech", "partsOfSpeech" },
                    new string[] { "definition", "def", "释义", "定义", "meaning", "english" },
                    new string[] { "formula", "math", "symbol", "公式", "关联公式/符号" },
                    new string[] { "confusables", "confusable", "similar", "易混", "易混词", "近义词" },
                    new string[] { "chinese", "cn", "中文", "中文释义", "translation" },
            };
            Dictionary<string, object> outp = new Dictionary<string, object>();
            int found = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                string[] g = groups[i];
                string val = null;
                for (int j = 0; j < g.Length; j++)
                {
                    val = GrabValue(s, g[j]);
                    if (val != null && val.Length > 0) break;
                }
                if (val != null && val.Length > 0) found++;
                outp[g[0]] = val == null ? "" : val;
            }
            return found >= 2 ? outp : null;
        }

        /// <summary>抓 "键" 后面的值：从键后面的冒号开始，到下一个键或结尾为止</summary>
        private static string GrabValue(string s, string key)
        {
            Regex p = new Regex("\"?" + Regex.Escape(key) + "\"?\\s*[:\uFF1A]\\s*");
            Match m = p.Match(s);
            if (!m.Success) return null;
            int from = m.Index + m.Length;
            int to = s.Length;
            Match next = new Regex("\"?[A-Za-z\\u4e00-\\u9fff/]+\"?\\s*[:\uFF1A]\\s*").Match(s, from);
            if (next.Success) to = next.Index;
            string v = s.Substring(from, to - from).Trim();
            // 先去尾部的逗号/大括号/空白，再去掉值的引号（顺序不能反，否则 "测试" } 会留下一个引号）
            v = Regex.Replace(v, "[,\\s}]+$", "").Trim();
            v = Regex.Replace(v, "^\"+", "");
            v = Regex.Replace(v, "\"+$", "");
            return v.Trim();
        }

        /// <summary>AI 完全没按格式输出时的降级：整段文字当成释义</summary>
        public static Dictionary<string, object> FallbackFields(string word, string raw)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            string text = raw == null ? "" : raw.Trim();
            o["phonetic"] = "";
            o["pos"] = "";
            o["definition"] = text.Replace('\n', ' ').Replace('\r', ' ');
            o["formula"] = "";
            o["confusables"] = "";
            o["chinese"] = "";
            return o;
        }

        // ------------------------------------------------------------ 解析辅助

        /// <summary>严格解析一个 JSON 对象；空对象算失败（对应原版 tryObject）</summary>
        private static Dictionary<string, object> TryObject(string s)
        {
            if (s == null) return null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> o = js.Deserialize<Dictionary<string, object>>(s);
                if (o == null) return null;
                return o.Count == 0 ? null : o;
            }
            catch (CsSystem.Exception)
            {
                return null;
            }
        }

        /// <summary>剥掉 ```json ... ``` 代码围栏（对应原版 stripFences）</summary>
        private static string StripFences(string s)
        {
            if (s.IndexOf("```", CsSystem.StringComparison.Ordinal) < 0) return s;
            int start = s.IndexOf("```", CsSystem.StringComparison.Ordinal);
            int end = s.LastIndexOf("```", CsSystem.StringComparison.Ordinal);
            if (end <= start) return s;
            string inner = s.Substring(start + 3, end - start - 3);
            int nl = inner.IndexOf('\n');
            if (nl >= 0)
            {
                string first = inner.Substring(0, nl).Trim();
                if (first.Length == 0 || Regex.IsMatch(first.ToLowerInvariant(), "^[a-z0-9_+-]+$"))
                {
                    inner = inner.Substring(nl + 1);
                }
            }
            return inner.Trim();
        }

        /// <summary>
        /// 取一个键的值：只认完全一致（区分大小写）的键名。
        ///
        /// 为什么不用 ContainsKey：JavaScriptSerializer 反序列化出来的 Dictionary 用的是
        /// OrdinalIgnoreCase 比较器，默认「Chinese」也会命中「chinese」，而 Android 的
        /// JSONObject 是大小写敏感的 —— 实测原版对 {"Chinese":"速度"} 的 parseAi 返回
        /// 「六个键全空」（值被丢掉）。这里刻意复刻原版，保证两边逐字节一致。
        /// </summary>
        private static bool TryGetExact(Dictionary<string, object> src, string key, out object val)
        {
            val = null;
            if (src == null || key == null) return false;
            foreach (KeyValuePair<string, object> kv in src)
            {
                if (string.Equals(kv.Key, key, CsSystem.StringComparison.Ordinal))
                {
                    val = kv.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按别名表取一个字段值（对应原版 put(out, key, src, aliases...)）</summary>
        private static void Put(Dictionary<string, object> outp, string key,
                Dictionary<string, object> src, string[] aliases)
        {
            try
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    object raw;
                    if (TryGetExact(src, aliases[i], out raw) && raw != null)
                    {
                        string v = raw is string ? (string)raw : CsSystem.Convert.ToString(raw,
                                CsSystem.Globalization.CultureInfo.InvariantCulture);
                        if (v != null && v.Trim().Length > 0)
                        {
                            outp[key] = v.Trim();
                            return;
                        }
                    }
                }
                outp[key] = "";
            }
            catch (CsSystem.Exception)
            {
                // 与原版一致：出错就保持已经装好的部分
            }
        }

        /// <summary>
        /// 补齐缺失的键（值写空串），对应原版 put 一定写键的行为。
        /// 写键之前先把字典换成**区分大小写**的比较器：JavaScriptSerializer 反序列化出来的
        /// Dictionary 用的是 OrdinalIgnoreCase（!= .NET 默认的 EqualityComparer&lt;string&gt;.Default），
        /// 这样 ContainsKey("chinese") 会把 AI 回成 "Chinese" 的键也算命中，
        /// 和 Android 的 org.json.JSONObject（键精确匹配）行为不一致。
        /// 换成 Ordinal 之后 ContainsKey / 索引器 与 Java 对齐，只要有一个规范键真的取到了值，
        /// 这个字典就会整体按 Ordinal 往外走（含自定义 config 需要的 example 等 extras）。
        /// </summary>
        private static void EnsureKeys(Dictionary<string, object> o, string[] keys)
        {
            bool hasAny = false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (o.ContainsKey(keys[i])) { hasAny = true; break; }
            }
            if (hasAny && !object.ReferenceEquals(o.Comparer, CsSystem.StringComparer.Ordinal))
            {
                Dictionary<string, object> m = new Dictionary<string, object>(CsSystem.StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> kv in o) m[kv.Key] = kv.Value;
                o.Clear();
                foreach (KeyValuePair<string, object> kv in m) o[kv.Key] = kv.Value;
            }
            for (int i = 0; i < keys.Length; i++)
            {
                if (!o.ContainsKey(keys[i])) o[keys[i]] = "";
            }
        }

        /// <summary>六个字段是不是全空（对应原版 isNullAll）</summary>
        private static bool IsNullAll(Dictionary<string, object> o)
        {
            string[] keys = new string[] { "phonetic", "pos", "definition", "formula", "confusables", "chinese" };
            for (int i = 0; i < keys.Length; i++)
            {
                string v = CardConfig.GetString(o, keys[i], "");
                if (v != null && v.Trim().Length > 0) return false;
            }
            return true;
        }

        // ------------------------------------------------------------ 组装 Anki 字段

        /// <summary>
        /// 把 AI 字段组装成 Anki 笔记字段（键名 = 笔记类型字段名）。
        /// 新行转成 &lt;br&gt;，尖括号转义，防止 AI 输出的 HTML 破坏卡片。
        /// </summary>
        public static Dictionary<string, object> NoteFields(string word, Dictionary<string, object> ai)
        {
            return NoteFieldsFor(word, ai, CardConfig.DefaultConfig());
        }

        // ------------------------------------------------------- config 版本（多格式）

        /// <summary>按指定 config 把 AI 结果组装成笔记字段（键名 = 该 config 的字段名）</summary>
        public static Dictionary<string, object> NoteFieldsFor(string word, Dictionary<string, object> ai, CardConfig cfg)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            if (cfg == null) cfg = CardConfig.DefaultConfig();
            for (int i = 0; i < cfg.Fields.Count; i++)
            {
                Field fd = cfg.Fields[i];
                if (i == 0)
                {
                    // 第一个字段是正面：用户输入的词
                    f[fd.Name] = Escape(word == null ? "" : word.Trim());
                    continue;
                }
                string v = ai == null ? "" : CardConfig.GetString(ai, fd.Key, "");
                f[fd.Name] = Nl(fd.Latex ? NormalizeFormula(v) : v);
            }
            return f;
        }

        /// <summary>按 config 合并（编辑器里用户改过的背面字段为准）</summary>
        public static Dictionary<string, object> MergeNoteFor(string word, Dictionary<string, object> back, CardConfig cfg)
        {
            Dictionary<string, object> f = new Dictionary<string, object>();
            if (cfg == null) cfg = CardConfig.DefaultConfig();
            for (int i = 0; i < cfg.Fields.Count; i++)
            {
                string name = cfg.Fields[i].Name;
                if (i == 0) f[name] = Escape(word == null ? "" : word.Trim());
                else f[name] = back == null ? "" : CardConfig.GetString(back, name, "");
            }
            return f;
        }

        /// <summary>按 config 生成一张空白卡（手工填充用）</summary>
        public static Dictionary<string, object> EmptyFieldsFor(string word, CardConfig cfg)
        {
            return NoteFieldsFor(word, null, cfg);
        }

        /// <summary>供「手工填充」使用的空字段（默认 config）</summary>
        public static Dictionary<string, object> EmptyFields(string word)
        {
            return NoteFields(word, null);
        }

        /// <summary>
        /// 把编辑器里读回的背面字段 + 输入框里的单词，合成一张完整笔记的字段表。
        /// 编辑器里的内容是用户手动改过的，以它为准（覆盖 AI 的原始输出）。
        /// </summary>
        public static Dictionary<string, object> MergeNote(string word, Dictionary<string, object> back)
        {
            return MergeNoteFor(word, back, CardConfig.DefaultConfig());
        }

        // ------------------------------------------------------------ 转义与公式

        /// <summary>HTML 转义（&amp; &lt; &gt;），防止 AI 输出的 HTML 破坏卡片</summary>
        public static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>多行文本 → Anki 字段（换行保留为 &lt;br&gt;）</summary>
        private static string Nl(string s)
        {
            if (s == null) return "";
            return Escape(s).Replace("\r\n", "\n").Replace("\n", "<br>");
        }

        /// <summary>
        /// 公式字段兜底：模型偶尔会忘了写 MathJax 定界符（返回裸的 b^2-4ac），
        /// 那样在 Anki 里不会渲染成公式。这里只在"看起来是纯公式"（没有定界符、也没有中文说明）
        /// 时给它补上 \( ... \)。
        /// </summary>
        public static string NormalizeFormula(string s)
        {
            if (s == null) return "";
            string v = s.Trim();
            if (v.Length == 0) return "";
            if (v.IndexOf("\\(", CsSystem.StringComparison.Ordinal) >= 0
                    || v.IndexOf("\\[", CsSystem.StringComparison.Ordinal) >= 0) return v;
            // 模型有时用 $...$ / $$...$$ 写公式，Anki 的 MathJax 不一定认单美元
            // 注意：C# 与 Java 不同，Regex.Replace 的替换串里反斜杠不是转义字符，
            // 所以 Java 的 "\\\\[$1\\\\]" 在这里要写成 "\\[$1\\]"（两个字符 \ 加 $1）。
            if (v.IndexOf('$') >= 0)
            {
                v = Regex.Replace(v, "\\$\\$([^$]+)\\$\\$", "\\[$1\\]");
                v = Regex.Replace(v, "\\$([^$]+)\\$", "\\($1\\)");
                if (v.IndexOf("\\(", CsSystem.StringComparison.Ordinal) >= 0
                        || v.IndexOf("\\[", CsSystem.StringComparison.Ordinal) >= 0) return v.Trim();
            }
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (c >= 0x2E80) return v;      // 有中文/日文等说明文字，原样保留
            }
            return "\\( " + v + " \\)";
        }

        // ------------------------------------------------------------ 自检用

        /// <summary>背面字段的纯文本（不含标签），用于自检</summary>
        public static string PlainBack(Dictionary<string, object> fields)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(CardConfig.GetString(fields, "单词", "")).Append('\n');
            string[] rest = new string[] { "音标", "词性", "定义", "关联公式/符号", "易混", "中文" };
            for (int i = 0; i < rest.Length; i++)
            {
                string v = CardConfig.GetString(fields, rest[i], "");
                if (v != null && v.Length > 0)
                {
                    sb.Append("【").Append(rest[i]).Append("】")
                      .Append(v.Replace("<br>", "\n")).Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
