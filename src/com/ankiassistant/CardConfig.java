package com.ankiassistant;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * 一套「输出格式 config」。
 *
 * 作用：决定 **AI 按什么格式产出** 以及 **卡片有哪些字段**。
 *   · 内置的默认 config 就是原来的 A Level 数学/物理术语格式（7 个字段）
 *   · 用户可以新建自己的 config：起个名字、写字段清单、写提示词，然后选用它
 *
 * 字段（{@link Field}）三件事：
 *   name 卡片上的字段名（同时也是 Anki 笔记类型的字段名）
 *   key  AI 返回的 JSON 键
 *   hint 编辑框里的灰色提示（可空）
 *   latex 该字段按公式处理（把 $..$ 归一成 \(..\)、给裸 LaTeX 包上 \( \)）
 *
 * 第一个字段约定为卡片的「正面」，由用户输入（AI 不用产出它）。
 */
public class CardConfig {

    public static class Field {
        public String name;
        public String key;
        public String hint;
        public boolean latex;

        public Field(String name, String key, String hint, boolean latex) {
            this.name = name;
            this.key = key;
            this.hint = hint;
            this.latex = latex;
        }
    }

    public static final String BUILTIN_ID = "default";

    public String id;
    /** 用户可见的 config 名（也用作自定义笔记类型的名字） */
    public String name;
    /** 写进 Anki 的笔记类型名 */
    public String noteType;
    /** 格式说明提示词，可含 {word} 与 {subject} 占位符 */
    public String prompt;
    public List<Field> fields = new ArrayList<Field>();
    public boolean builtin;

    /**
     * 这套 config 自己的默认值（不同 config 可以不一样）：
     * 留空表示沿用应用级默认，这样老用户升级后行为不变。
     */
    public String defaultDeck = "";
    public String defaultTags = "";
    public String subject = "";


    // ------------------------------------------------------------ 三个内置格式（都不可修改）

    /** 内置格式 id */
    public static final String ID_ENGLISH = "default";   // 英语格式（默认）
    public static final String ID_ALEVEL = "alevel";      // A Level Maths / Phy
    public static final String ID_VOCAB = "vocab";        // 英语词汇

    /** A Level 那套是否用应用级默认值（是：它沿用原来的默认牌组/标签/学科背景） */
    public static boolean usesAppDefaults(String id) {
        return ID_ALEVEL.equals(id);
    }

    /** 全部内置格式，**顺序就是界面里的顺序**（英语格式在最上面） */
    public static List<CardConfig> builtins() {
        List<CardConfig> out = new ArrayList<CardConfig>();
        out.add(englishConfig());
        out.add(alevelConfig());
        out.add(vocabConfig());
        return out;
    }

    /** 英语格式：最通用的一套（默认使用） */
    public static CardConfig englishConfig() {
        CardConfig c = new CardConfig();
        c.id = ID_ENGLISH;
        c.name = "英语格式";
        c.noteType = "英语格式卡";
        c.builtin = true;
        c.fields.add(new Field("单词", "", "", false));
        c.fields.add(new Field("音标", "phonetic", "英 /…/；美 /…/", false));
        c.fields.add(new Field("词性", "pos", "n / v / adj（多个用 / 连接）", false));
        c.fields.add(new Field("释义", "definition", "英文释义", false));
        c.fields.add(new Field("例句", "example", "一句包含该词的英文例句", false));
        c.fields.add(new Field("中文", "chinese", "中文释义，多个用「；」分隔", false));
        c.defaultDeck = "英语词汇";
        c.defaultTags = "English::Vocab";
        c.subject = "通用英语（日常与学术都适用）";
        c.prompt = ENGLISH_PROMPT;
        return c;
    }

    /** A Level Maths / Phy：原来的七字段术语卡（默认值沿用应用级设置） */
    public static CardConfig alevelConfig() {
        CardConfig c = new CardConfig();
        c.id = ID_ALEVEL;
        c.name = "A Level Maths / Phy";
        c.noteType = "专业术语卡";
        c.builtin = true;
        c.fields.add(new Field("单词", "", "", false));
        c.fields.add(new Field("音标", "phonetic", "英 /…/；美 /…/", false));
        c.fields.add(new Field("词性", "pos", "n / adj / v（多个用 / 连接）", false));
        c.fields.add(new Field("定义", "definition", "(n) 英文释义; (v) 另一个义项", false));
        c.fields.add(new Field("关联公式/符号", "formula", "行内 MathJax，写成 \\( … \\)", true));
        c.fields.add(new Field("易混", "confusables", "每行一个：词 /音标/ 词性 中文释义", false));
        c.fields.add(new Field("中文", "chinese", "多个义项用「；」分隔", false));
        // defaultDeck / defaultTags / subject 留空 → 用应用级默认值（与升级前行为一致）
        c.prompt = DEFAULT_PROMPT;
        return c;
    }

    /** 英语词汇：轻量记忆卡（词 + 音标 + 中文 + 例句） */
    public static CardConfig vocabConfig() {
        CardConfig c = new CardConfig();
        c.id = ID_VOCAB;
        c.name = "英语词汇";
        c.noteType = "英语词汇卡";
        c.builtin = true;
        c.fields.add(new Field("单词", "", "", false));
        c.fields.add(new Field("音标", "phonetic", "英 /…/；美 /…/", false));
        c.fields.add(new Field("中文", "chinese", "中文释义", false));
        c.fields.add(new Field("例句", "example", "一句包含该词的英文例句", false));
        c.defaultDeck = "英语词汇";
        c.defaultTags = "English::Vocab";
        c.subject = "通用英语（日常与学术都适用）";
        c.prompt = VOCAB_PROMPT;
        return c;
    }

    /** 英语格式的提示词（通用） */
    public static final String ENGLISH_PROMPT =
            "请为「{word}」生成一张英语词卡。\n"
            + "学科背景：{subject}。\n"
            + "\n"
            + "只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：\n"
            + "phonetic：音标，英式与美式都给，写成 英 /.../；美 /.../ ；只有一种读音时两边写一样。\n"
            + "pos：词性缩写，多个用 / 连接（n / v / adj / adv 等），不要加点号。\n"
            + "definition：英文释义，简洁准确；有多个义项时用 ; 分隔。\n"
            + "example：一个完整、地道的英文例句，必须真的包含这个词；句尾加句号。\n"
            + "chinese：中文释义，多个义项用「；」分隔。\n"
            + "\n"
            + "示例：\n"
            + "{\"phonetic\":\"英 /ˈkæmbrɪdʒ/；美 /ˈkeɪmbrɪdʒ/\",\"pos\":\"n\","
            + "\"definition\":\"a city in eastern England, famous for its university\","
            + "\"example\":\"She studied mathematics at Cambridge.\","
            + "\"chinese\":\"剑桥（英国城市）；剑桥大学\"}";

    /** 英语词汇的提示词（更短，只要词义与例句） */
    public static final String VOCAB_PROMPT =
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

    // ------------------------------------------------------------ 内置默认

    /** 默认格式 = 第一个内置格式（英语格式） */
    public static CardConfig defaultConfig() {
        return englishConfig();
    }

    /** 默认提示词：与用户 Anki 里已有卡片一致（英/美音标、([词性]) 释义、行内 MathJax、≠ 易混） */
    public static final String DEFAULT_PROMPT =
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

    // ------------------------------------------------------------ 读写

    public static CardConfig fromJson(JSONObject o) {
        CardConfig c = new CardConfig();
        c.id = o.optString("id", "");
        c.name = o.optString("name", "");
        c.noteType = o.optString("noteType", c.name);
        c.prompt = o.optString("prompt", DEFAULT_PROMPT);
        c.builtin = o.optBoolean("builtin", false);
        c.defaultDeck = o.optString("defaultDeck", "");
        c.defaultTags = o.optString("defaultTags", "");
        c.subject = o.optString("subject", "");
        JSONArray fs = o.optJSONArray("fields");
        if (fs != null) {
            for (int i = 0; i < fs.length(); i++) {
                JSONObject f = fs.optJSONObject(i);
                if (f == null) continue;
                c.fields.add(new Field(f.optString("name", "字段" + (i + 1)),
                        f.optString("key", "f" + i),
                        f.optString("hint", ""),
                        f.optBoolean("latex", false)));
            }
        }
        if (c.fields.isEmpty()) c.fields = defaultConfig().fields;
        if (c.noteType == null || c.noteType.trim().length() == 0) c.noteType = c.name;
        return c;
    }

    public JSONObject toJson() {
        JSONObject o = new JSONObject();
        try {
            o.put("id", id);
            o.put("name", name);
            o.put("noteType", noteType);
            o.put("prompt", prompt);
            o.put("builtin", builtin);
            o.put("defaultDeck", defaultDeck == null ? "" : defaultDeck);
            o.put("defaultTags", defaultTags == null ? "" : defaultTags);
            o.put("subject", subject == null ? "" : subject);
            JSONArray fs = new JSONArray();
            for (Field f : fields) {
                JSONObject x = new JSONObject();
                x.put("name", f.name);
                x.put("key", f.key);
                x.put("hint", f.hint);
                x.put("latex", f.latex);
                fs.put(x);
            }
            o.put("fields", fs);
        } catch (Exception ignored) { }
        return o;
    }

    // ------------------------------------------------------------ 生成卡片相关

    public String[] fieldNames() {
        String[] out = new String[fields.size()];
        for (int i = 0; i < out.length; i++) out[i] = fields.get(i).name;
        return out;
    }

    /** AI 需要产出的键（第 0 个字段是用户输入的正面，不算） */
    public String[] aiKeys() {
        List<String> keys = new ArrayList<String>();
        for (int i = 1; i < fields.size(); i++) {
            if (fields.get(i).key != null && fields.get(i).key.length() > 0) {
                keys.add(fields.get(i).key);
            }
        }
        return keys.toArray(new String[0]);
    }

    /** 生效的默认牌组（本 config 没写就沿用应用级默认） */
    public String deckOr(String appDefault) {
        return (defaultDeck != null && defaultDeck.trim().length() > 0) ? defaultDeck.trim() : appDefault;
    }

    public String tagsOr(String appDefault) {
        return (defaultTags != null && defaultTags.trim().length() > 0) ? defaultTags.trim() : appDefault;
    }

    public String subjectOr(String appDefault) {
        return (subject != null && subject.trim().length() > 0) ? subject.trim() : appDefault;
    }

    public String cardFront() {
        return "<div class=\"word\">{{" + fields.get(0).name + "}}</div>";
    }

    /** 背面：每个字段一行【字段名】值，空字段连标签一起隐藏（与默认格式视觉一致） */
    public String cardBack() {
        StringBuilder sb = new StringBuilder(cardFront());
        sb.append("\n<hr id=\"answer\">\n<div class=\"body\">\n");
        for (int i = 1; i < fields.size(); i++) {
            String n = fields.get(i).name;
            sb.append("{{#").append(n).append("}}<div class=\"row\">")
              .append("<span class=\"lbl\">【").append(n).append("】</span>")
              .append("<span class=\"val\">{{").append(n).append("}}</span></div>{{/").append(n).append("}}\n");
        }
        sb.append("</div>");
        return sb.toString();
    }

    /** 把 config 的提示词模板填上实际值 */
    public String buildPrompt(String word, String subject) {
        String subj = (subject == null || subject.trim().length() == 0)
                ? "数学、物理学科术语（若是学科术语请给出精确定义）" : subject.trim();
        String p = prompt == null ? "" : prompt;
        return p.replace("{word}", word == null ? "" : word.trim())
                .replace("{subject}", subj);
    }

    /** 重试用的提示词：再要一次，明确不许有思考过程或代码块 */
    public String buildPromptStrict(String word, String subject) {
        return buildPrompt(word, subject)
                + "\n\n重要：不要输出任何思考过程、解释或 Markdown 代码块，"
                + "只输出上面那个 JSON 对象本身，且必须是可以直接解析的完整 JSON。";
    }
}
