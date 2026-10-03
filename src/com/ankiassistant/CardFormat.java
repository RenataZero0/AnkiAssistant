package com.ankiassistant;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * 卡片格式规范 —— 本应用的"唯一事实来源"。
 *
 * 需求（PRMOPT.md 第二节第 3 条）：
 *   正面：[输入的单词]
 *   背面：
 *     【音标】[此单词的音标]
 *     【词性】[此单词的词性]
 *     【定义】([词性]) [英文释义]; ([若有多个词性]) [此词性对应的释义]
 *     【关联公式/符号】[相关公式]（使用 Anki 内置 MathJax 格式）
 *     【易混】[相近单词]（列出读音与意义相近的词）
 *
 * 这里同时负责三件事：
 *   1) buildPrompt()  —— 生成喂给 AI 的提示词
 *   2) parseAi()      —— 把 AI 的返回（可能带 ```json 围栏、可能啰嗦）解析成字段
 *   3) noteFields()   —— 组装成 Anki 的笔记字段
 * 卡片模板 / 样式也定义在这里，保证「桌面端渲染」与「应用内预览」完全一致。
 *
 * 注意：这个类只依赖 org.json 与 java.*，所以能在电脑 JVM 上直接跑自检（tools/SelfTest.java）。
 */
public class CardFormat {

    public static final String MODEL_NAME = CardConfig.defaultConfig().noteType;

    /** 笔段顺序（也是背面从上到下的顺序）。中文行是参考用户现有卡片补的，可留空 */
    public static final String[] FIELDS = CardConfig.defaultConfig().fieldNames();

    public static final String SYSTEM =
            "你是资深的英汉词典编辑，熟悉 A Level 数学与物理的术语与符号。"
            + "你只输出一个 JSON 对象，不输出任何解释、注释或 Markdown 代码块。";

    /** 卡片正面模板 */
    public static final String CARD_FRONT = CardConfig.defaultConfig().cardFront();

    /**
     * 按牌组自动带出的标签：数学类四个牌组 → ALevel::Maths，物理 → ALevel::Physics。
     * 其它牌组返回 null（不动用户自己填的标签）。
     */
    public static String tagsForDeck(String deck) {
        if (deck == null) return null;
        String d = deck.trim();
        if (d.length() == 0) return null;
        if (d.equalsIgnoreCase("A Level Physics")) return "ALevel::Physics";
        if (d.startsWith("A Level Mathematics")
                || d.startsWith("A Level Further Mathematics")
                || d.startsWith("A Level Pure Mathematics")
                || d.startsWith("A Level Statistics")) {
            return "ALevel::Maths";
        }
        return null;
    }

    /** 卡片背面模板（默认 config）：每个字段一行，空字段连标签一起隐藏 */
    public static final String CARD_BACK = CardConfig.defaultConfig().cardBack();

    /** 模板样式（写进笔记类型，桌面端渲染用的是同一段 CSS） */
    public static final String CARD_CSS =
            ".card{font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,"
            + "'PingFang SC','Microsoft YaHei',sans-serif;font-size:20px;line-height:1.65;"
            + "text-align:left;color:#1b2432;background:#ffffff;padding:4px;}\n"
            + ".word{font-size:30px;font-weight:700;letter-spacing:.5px;}\n"
            + "hr#answer{border:none;border-top:2px solid #e5e9f0;margin:14px 0;}\n"
            + ".row{margin:9px 0;}\n"
            + ".lbl{color:#3568e8;font-weight:700;margin-right:8px;white-space:nowrap;}\n"
            + ".val{color:#1b2432;}\n"
            + "code{background:#f3f5f9;border-radius:4px;padding:1px 5px;}\n";

    // ---------------------------------------------------------------- 提示词

    /** 用户提示词（默认 config 的版本；多格式请用 CardConfig.buildPrompt） */
    public static String buildPrompt(String word, String subject) {
        return CardConfig.defaultConfig().buildPrompt(word, subject);
    }

    /**
     * 重试用的提示词：第一次没解析出 JSON 时再要一次。
     * 思考型模型在长推理后容易"发挥"（加解释、加代码块、甚至把 JSON 写截断），
     * 这里明确要求不要思考、不要解释。
     */
    public static String buildPromptStrict(String word, String subject) {
        return CardConfig.defaultConfig().buildPromptStrict(word, subject);
    }

    // ------------------------------------------------------------ AI 返回解析

    /**
     * 解析 AI 的原始返回。容忍：```json 围栏、前后缀废话、中文键名。
     * 解析不出来返回 null（调用方自行降级）。
     */
    public static JSONObject parseAi(String raw) {
        if (raw == null) return null;
        String s = stripFences(raw.trim());
        JSONObject o = tryObject(s);
        if (o == null) {
            int a = s.indexOf('{'), b = s.lastIndexOf('}');
            if (a >= 0 && b > a) o = tryObject(s.substring(a, b + 1));
        }
        // 模型常见的"写坏 JSON"：中文值前面用全角冒号、少个引号。实测开/关思考都会出现。
        if (o == null) o = tryObject(repairJson(s));
        if (o == null) {
            int a = s.indexOf('{'), b = s.lastIndexOf('}');
            if (a >= 0 && b > a) o = tryObject(repairJson(s.substring(a, b + 1)));
        }
        // 还是不行就按字段名硬抠（不依赖 JSON 语法）
        if (o == null) o = looseExtract(s);
        if (o == null) return null;
        JSONObject out = new JSONObject();
        put(out, "phonetic", o, "phonetic", "音标", "pronunciation", "ipa");
        put(out, "pos", o, "pos", "词性", "partOfSpeech", "partsOfSpeech", "speech");
        put(out, "definition", o, "definition", "def", "释义", "定义", "meaning", "english");
        put(out, "formula", o, "formula", "math", "symbol", "公式", "关联公式", "关联公式/符号");
        put(out, "confusables", o, "confusables", "confusable", "similar", "易混", "易混词", "近义词");
        put(out, "chinese", o, "chinese", "cn", "中文", "中文释义", "translation", "meaningCn");
        if (isNullAll(out)) return null;
        return out;
    }

    /**
     * 修补模型偶尔写坏的 JSON：
     *   "chinese"："速度"   → 全角冒号
     *   "chinese：速度"     → 键后面少了引号
     *   “chinese”           → 弯引号
     * 只动"键和冒号"这一小段，不碰值里面的中文标点（值里的「，」「：」是正常文案）。
     */
    public static String repairJson(String s) {
        if (s == null) return "";
        String t = s.replace('\u201C', '"').replace('\u201D', '"');
        // "键"： 后面紧跟引号 → 只把全角冒号换成半角
        t = t.replaceAll("\"\\s*\uFF1A\\s*(?=\")", "\":");
        // "键"：后面不是引号 → 补上值的起始引号
        t = t.replaceAll("\"\\s*\uFF1A\\s*", "\":\"");
        // "键：值" → 键的收尾引号丢了，补上（键只含 ASCII 字母/下划线）
        t = t.replaceAll("\"([A-Za-z][A-Za-z0-9_]*)\\s*\uFF1A\\s*(?=\")", "\"$1\":\"");
        t = t.replaceAll("\"([A-Za-z][A-Za-z0-9_]*)\\s*\uFF1A\\s*", "\"$1\":\"");
        return t;
    }

    /** 最后的兜底：JSON 语法实在修不好时，按字段名把值抠出来 */
    private static JSONObject looseExtract(String s) {
        if (s == null) return null;
        String[][] groups = {
                {"phonetic", "音标", "pronunciation", "ipa"},
                {"pos", "词性", "partOfSpeech", "partsOfSpeech"},
                {"definition", "def", "释义", "定义", "meaning", "english"},
                {"formula", "math", "symbol", "公式", "关联公式/符号"},
                {"confusables", "confusable", "similar", "易混", "易混词", "近义词"},
                {"chinese", "cn", "中文", "中文释义", "translation"},
        };
        JSONObject out = new JSONObject();
        int found = 0;
        for (String[] g : groups) {
            String val = null;
            for (String key : g) {
                val = grabValue(s, key);
                if (val != null && val.length() > 0) break;
            }
            if (val != null && val.length() > 0) found++;
            try {
                out.put(g[0], val == null ? "" : val);
            } catch (JSONException ignored) { }
        }
        return found >= 2 ? out : null;
    }

    /** 抓 "键" 后面的值：从键后面的冒号开始，到下一个键或结尾为止 */
    private static String grabValue(String s, String key) {
        java.util.regex.Pattern p = java.util.regex.Pattern.compile(
                "\"?" + java.util.regex.Pattern.quote(key) + "\"?\\s*[:\uFF1A]\\s*");
        java.util.regex.Matcher m = p.matcher(s);
        if (!m.find()) return null;
        int from = m.end();
        int to = s.length();
        java.util.regex.Matcher next = java.util.regex.Pattern
                .compile("\"?[A-Za-z\\u4e00-\\u9fff/]+\"?\\s*[:\uFF1A]\\s*").matcher(s);
        next.region(from, s.length());
        if (next.find()) to = next.start();
        String v = s.substring(from, to).trim();
        // 先去尾部的逗号/大括号/空白，再去掉值的引号（顺序不能反，否则 "测试" } 会留下一个引号）
        v = v.replaceAll("[,\\s}]+$", "").trim();
        v = v.replaceAll("^\"+", "").replaceAll("\"+$", "");
        return v.trim();
    }

    /** AI 完全没按格式输出时的降级：整段文字当成释义 */
    public static JSONObject fallbackFields(String word, String raw) {
        JSONObject o = new JSONObject();
        String text = raw == null ? "" : raw.trim();
        try {
            o.put("phonetic", "");
            o.put("pos", "");
            o.put("definition", text.replace('\n', ' ').replace('\r', ' '));
            o.put("formula", "");
            o.put("confusables", "");
            o.put("chinese", "");
        } catch (JSONException ignored) { }
        return o;
    }

    private static JSONObject tryObject(String s) {
        try {
            JSONObject o = new JSONObject(s);
            return o.length() == 0 ? null : o;
        } catch (JSONException e) {
            return null;
        }
    }

    private static String stripFences(String s) {
        if (s.indexOf("```") < 0) return s;
        int start = s.indexOf("```");
        int end = s.lastIndexOf("```");
        if (end <= start) return s;
        String inner = s.substring(start + 3, end);
        int nl = inner.indexOf('\n');
        if (nl >= 0) {
            String first = inner.substring(0, nl).trim();
            if (first.length() == 0 || first.toLowerCase().matches("[a-z0-9_+-]+")) {
                inner = inner.substring(nl + 1);
            }
        }
        return inner.trim();
    }

    private static void put(JSONObject out, String key, JSONObject src, String... aliases) {
        try {
            for (int i = 0; i < aliases.length; i++) {
                if (src.has(aliases[i]) && !src.isNull(aliases[i])) {
                    String v = src.get(aliases[i]) instanceof String
                            ? src.getString(aliases[i])
                            : String.valueOf(src.get(aliases[i]));
                    if (v != null && v.trim().length() > 0) {
                        out.put(key, v.trim());
                        return;
                    }
                }
            }
            out.put(key, "");
        } catch (JSONException ignored) { }
    }

    private static boolean isNullAll(JSONObject o) {
        String[] keys = {"phonetic", "pos", "definition", "formula", "confusables", "chinese"};
        for (int i = 0; i < keys.length; i++) {
            String v = o.optString(keys[i], "");
            if (v != null && v.trim().length() > 0) return false;
        }
        return true;
    }

    // ------------------------------------------------------------ 组装 Anki 字段

    /**
     * 把 AI 字段组装成 Anki 笔记字段（键名 = 笔记类型字段名）。
     * 新行转成 &lt;br&gt;，尖括号转义，防止 AI 输出的 HTML 破坏卡片。
     */
    public static JSONObject noteFields(String word, JSONObject ai) {
        return noteFieldsFor(word, ai, CardConfig.defaultConfig());
    }

    // ------------------------------------------------------- config 版本（多格式）

    /** 按指定 config 把 AI 结果组装成笔记字段（键名 = 该 config 的字段名） */
    public static JSONObject noteFieldsFor(String word, JSONObject ai, CardConfig cfg) {
        JSONObject f = new JSONObject();
        if (cfg == null) cfg = CardConfig.defaultConfig();
        try {
            for (int i = 0; i < cfg.fields.size(); i++) {
                CardConfig.Field fd = cfg.fields.get(i);
                if (i == 0) {   // 第一个字段是正面：用户输入的词
                    f.put(fd.name, escape(word == null ? "" : word.trim()));
                    continue;
                }
                String v = ai == null ? "" : ai.optString(fd.key, "");
                f.put(fd.name, nl(fd.latex ? normalizeFormula(v) : v));
            }
        } catch (JSONException ignored) { }
        return f;
    }

    /** 按 config 合并（编辑器里用户改过的背面字段为准） */
    public static JSONObject mergeNoteFor(String word, JSONObject back, CardConfig cfg) {
        JSONObject f = new JSONObject();
        if (cfg == null) cfg = CardConfig.defaultConfig();
        try {
            for (int i = 0; i < cfg.fields.size(); i++) {
                String name = cfg.fields.get(i).name;
                if (i == 0) f.put(name, escape(word == null ? "" : word.trim()));
                else f.put(name, back == null ? "" : back.optString(name, ""));
            }
        } catch (JSONException ignored) { }
        return f;
    }

    /** 按 config 生成一张空白卡（手工填充用） */
    public static JSONObject emptyFieldsFor(String word, CardConfig cfg) {
        return noteFieldsFor(word, null, cfg);
    }
    /** 供「手工填充」使用的空字段 */
    public static JSONObject emptyFields(String word) {
        return noteFields(word, null);
    }

    /**
     * 把编辑器里读回的背面字段 + 输入框里的单词，合成一张完整笔记的字段表。
     * 编辑器里的内容是用户手动改过的，以它为准（覆盖 AI 的原始输出）。
     */
    public static JSONObject mergeNote(String word, JSONObject back) {
        return mergeNoteFor(word, back, CardConfig.defaultConfig());
    }

    public static String escape(String s) {
        if (s == null) return "";
        return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
    }

    /** 多行文本 -> Anki 字段（换行保留为 <br>） */
    private static String nl(String s) {
        if (s == null) return "";
        return escape(s).replace("\r\n", "\n").replace("\n", "<br>");
    }

    /**
     * 公式字段兜底：模型偶尔会忘了写 MathJax 定界符（返回裸的 b^2-4ac），
     * 那样在 Anki 里不会渲染成公式。这里只在"看起来是纯公式"（没有定界符、也没有中文说明）
     * 时给它补上 \( ... \)。
     */
    public static String normalizeFormula(String s) {
        if (s == null) return "";
        String v = s.trim();
        if (v.length() == 0) return "";
        if (v.contains("\\(") || v.contains("\\[")) return v;
        // 模型有时用 $...$ / $$...$$ 写公式，Anki 的 MathJax 不一定认单美元
        if (v.indexOf('$') >= 0) {
            v = v.replaceAll("\\$\\$([^$]+)\\$\\$", "\\\\[$1\\\\]");
            v = v.replaceAll("\\$([^$]+)\\$", "\\\\($1\\\\)");
            if (v.contains("\\(") || v.contains("\\[")) return v.trim();
        }
        for (int i = 0; i < v.length(); i++) {
            char c = v.charAt(i);
            if (c >= 0x2E80) return v;      // 有中文/日文等说明文字，原样保留
        }
        return "\\( " + v + " \\)";
    }

    // ------------------------------------------------------------ 自检用

    /** 背面字段的纯文本（不含标签），用于自检 */
    public static String plainBack(JSONObject fields) {
        StringBuilder sb = new StringBuilder();
        sb.append(fields.optString("单词", "")).append('\n');
        String[] rest = {"音标", "词性", "定义", "关联公式/符号", "易混", "中文"};
        for (int i = 0; i < rest.length; i++) {
            String v = fields.optString(rest[i], "");
            if (v.length() > 0) sb.append("【").append(rest[i]).append("】").append(v.replace("<br>", "\n")).append('\n');
        }
        return sb.toString();
    }
}
