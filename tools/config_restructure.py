"""输出格式重构：
   三个内置格式（都不可修改）：
     1. 英语格式      —— 默认，最通用（6 字段）
     2. A Level Maths / Phy —— 原来的 A Level 格式（7 字段，沿用应用级默认值）
     3. 英语词汇      —— 轻量记忆卡（4 字段）
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")
path = APP + r"\src\com\ankiassistant\CardConfig.java"
src = io.open(path, encoding="utf-8").read()

BUILTINS = '''
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
        c.fields.add(new Field("关联公式/符号", "formula", "行内 MathJax，写成 \\\\( … \\\\)", true));
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
            "请为「{word}」生成一张英语词卡。\\n"
            + "学科背景：{subject}。\\n"
            + "\\n"
            + "只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：\\n"
            + "phonetic：音标，英式与美式都给，写成 英 /.../；美 /.../ ；只有一种读音时两边写一样。\\n"
            + "pos：词性缩写，多个用 / 连接（n / v / adj / adv 等），不要加点号。\\n"
            + "definition：英文释义，简洁准确；有多个义项时用 ; 分隔。\\n"
            + "example：一个完整、地道的英文例句，必须真的包含这个词；句尾加句号。\\n"
            + "chinese：中文释义，多个义项用「；」分隔。\\n"
            + "\\n"
            + "示例：\\n"
            + "{\\"phonetic\\":\\"英 /ˈkæmbrɪdʒ/；美 /ˈkeɪmbrɪdʒ/\\",\\"pos\\":\\"n\\","
            + "\\"definition\\":\\"a city in eastern England, famous for its university\\","
            + "\\"example\\":\\"She studied mathematics at Cambridge.\\","
            + "\\"chinese\\":\\"剑桥（英国城市）；剑桥大学\\"}";

    /** 英语词汇的提示词（更短，只要词义与例句） */
    public static final String VOCAB_PROMPT =
            "请为「{word}」生成一张英语词汇卡。\\n"
            + "学科背景：{subject}。\\n"
            + "\\n"
            + "只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：\\n"
            + "phonetic：音标，英式与美式都给（英 /.../；美 /.../）。\\n"
            + "chinese：中文释义，简洁，多个义项用「；」分隔。\\n"
            + "example：一个完整、地道的英文例句，必须包含这个词；句尾加句号。\\n"
            + "\\n"
            + "示例：\\n"
            + "{\\"phonetic\\":\\"英 /ˈæpl/；美 /ˈæpl/\\",\\"chinese\\":\\"苹果\\","
            + "\\"example\\":\\"He ate an apple for breakfast.\\"}";

'''
anchor = "    // ------------------------------------------------------------ 内置默认"
if "builtins()" in src:
    print("已存在，跳过")
else:
    src = src.replace(anchor, BUILTINS + anchor, 1)
    # defaultConfig 指向英语格式
    src = re.sub(r"(?s)    /\*\* 默认 config：.*?\n    public static CardConfig defaultConfig\(\) \{.*?\n        return c;\n    \}",
                 "    /** 默认格式 = 第一个内置格式（英语格式） */\n"
                 "    public static CardConfig defaultConfig() {\n"
                 "        return englishConfig();\n"
                 "    }", src, count=1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(src)
    print("CardConfig 已加入三个内置格式")

# --- Store：内置格式不可改、不再支持覆盖 ---
p = APP + r"\src\com\ankiassistant\Store.java"
s = io.open(p, encoding="utf-8").read()
if "CardConfig.builtins()" not in s:
    s = re.sub(r"(?s)    public java\.util\.List<CardConfig> configs\(\) \{.*?\n    \}",
               '''    /** 所有格式：内置三个（不可改）+ 用户自建的 */
    public java.util.List<CardConfig> configs() {
        java.util.List<CardConfig> out = new java.util.ArrayList<CardConfig>();
        out.addAll(CardConfig.builtins());
        try {
            org.json.JSONArray arr = new org.json.JSONArray(sp.getString("configs", "[]"));
            for (int i = 0; i < arr.length(); i++) {
                org.json.JSONObject o = arr.optJSONObject(i);
                if (o == null) continue;
                CardConfig c = CardConfig.fromJson(o);
                if (c.id == null || c.id.length() == 0) continue;
                if (isBuiltinId(c.id)) continue;   // 内置的那几个不从这里来
                out.add(c);
            }
        } catch (Exception ignored) { }
        return out;
    }

    private static boolean isBuiltinId(String id) {
        for (CardConfig c : CardConfig.builtins()) {
            if (c.id.equals(id)) return true;
        }
        return false;
    }''', s, count=1)
    s = s.replace("    public void setActiveConfigId(String id) {\n        put(\"activeConfigId\", id == null ? CardConfig.BUILTIN_ID : id);",
                  "    public void setActiveConfigId(String id) {\n        put(\"activeConfigId\", id == null ? CardConfig.ID_ENGLISH : id);")
    s = s.replace("    public CardConfig activeConfig() {\n        String id = sp.getString(\"activeConfigId\", CardConfig.BUILTIN_ID);",
                  "    public CardConfig activeConfig() {\n        String id = sp.getString(\"activeConfigId\", CardConfig.ID_ENGLISH);")
    s = re.sub(r"(?s)    /\*\* 新增或更新一条 config.*?\n    \}",
               '''    /** 新增或更新一条自定义格式（内置三个不允许改） */
    public void saveConfig(CardConfig c) {
        if (c == null || c.id == null || c.id.length() == 0) return;
        if (isBuiltinId(c.id)) return;
        org.json.JSONArray arr = new org.json.JSONArray();
        boolean replaced = false;
        for (CardConfig x : configs()) {
            if (isBuiltinId(x.id)) continue;
            if (x.id.equals(c.id)) { arr.put(c.toJson()); replaced = true; }
            else arr.put(x.toJson());
        }
        if (!replaced) arr.put(c.toJson());
        put("configs", arr.toString());
    }''', s, count=1)
    s = re.sub(r"(?s)    /\*\* 删除自定义 config.*?\n    \}",
               '''    /** 删除自定义格式（内置三个不能删） */
    public void deleteConfig(String id) {
        if (id == null || isBuiltinId(id)) return;
        org.json.JSONArray arr = new org.json.JSONArray();
        for (CardConfig x : configs()) {
            if (isBuiltinId(x.id) || x.id.equals(id)) continue;
            arr.put(x.toJson());
        }
        put("configs", arr.toString());
        if (id.equals(sp.getString("activeConfigId", ""))) setActiveConfigId(CardConfig.ID_ENGLISH);
    }''', s, count=1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("Store 已改为三个内置 + 自定义")
