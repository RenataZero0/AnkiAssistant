"""内置格式收尾：两个内置格式，默认 = 英语词汇（删掉多加的「英语格式」）。"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

p = APP + r"\src\com\ankiassistant\CardConfig.java"
s = io.open(p, encoding="utf-8").read()

# 1) builtins() → 英语词汇 + A Level
s = re.sub(r"(?s)    /\*\* 全部内置格式.*?\n    \}",
'''    /** 全部内置格式，**顺序就是界面里的顺序**（英语词汇在最上面，是默认格式） */
    public static List<CardConfig> builtins() {
        List<CardConfig> out = new ArrayList<CardConfig>();
        out.add(vocabConfig());
        out.add(alevelConfig());
        return out;
    }''', s, count=1)

# 2) 默认 = 英语词汇
s = re.sub(r"(?s)    /\*\* 默认格式 = 第一个内置格式（英语格式） \*/\n    public static CardConfig defaultConfig\(\) \{.*?\n    \}",
'''    /** 默认格式 = 英语词汇 */
    public static CardConfig defaultConfig() {
        return vocabConfig();
    }''', s, count=1)

# 3) 删掉 englishConfig() 与 ENGLISH_PROMPT
s = re.sub(r"(?s)    /\*\* 英语格式：最通用的一套（默认使用） \*/\n    public static CardConfig englishConfig\(\) \{.*?\n    \}\n\n", "", s, count=1)
s = re.sub(r"(?s)    /\*\* 英语格式的提示词（通用） \*/\n    public static final String ENGLISH_PROMPT =.*?\n\n", "", s, count=1)

# 4) id 常量整理
s = s.replace('    public static final String ID_ENGLISH = "default";   // 英语格式（默认）\n',
              '    public static final String ID_VOCAB_DEFAULT = "default";   // 英语词汇（默认）\n')
s = s.replace('c.id = ID_VOCAB;\n        c.name = "英语词汇";', 'c.id = ID_VOCAB_DEFAULT;\n        c.name = "英语词汇";')
s = s.replace('CardConfig.ID_ENGLISH', 'CardConfig.ID_VOCAB_DEFAULT')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("CardConfig 处理完成")

p2 = APP + r"\src\com\ankiassistant\Store.java"
s2 = io.open(p2, encoding="utf-8").read().replace("CardConfig.ID_ENGLISH", "CardConfig.ID_VOCAB_DEFAULT")
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("Store 处理完成")

# 5) 自检里的断言同步改（英语格式 → 英语词汇）
p3 = APP + r"\tools\SelfTest.java"
s3 = io.open(p3, encoding="utf-8").read()
s3 = s3.replace('eq("内置格式个数", builtins.size(), 3);', 'eq("内置格式个数", builtins.size(), 2);')
s3 = s3.replace('eq("第一个是英语格式", builtins.get(0).name, "英语格式");', 'eq("第一个是英语词汇（默认）", builtins.get(0).name, "英语词汇");')
s3 = s3.replace('eq("第二个是 A Level", builtins.get(1).name, "A Level Maths / Phy");',
                'eq("第二个是 A Level", builtins.get(1).name, "A Level Maths / Phy");')
s3 = s3.replace('eq("第三个是英语词汇", builtins.get(2).name, "英语词汇");', '')
s3 = s3.replace('eq("默认格式 = 英语格式", CardConfig.defaultConfig().name, "英语格式");',
                'eq("默认格式 = 英语词汇", CardConfig.defaultConfig().name, "英语词汇");')
# 原「英语格式」那组断言（6 字段）删掉，A Level 那组改用 builtins.get(1)
s3 = re.sub(r"(?s)        CardConfig en = builtins\.get\(0\);.*?eq\(\"英语格式自带默认牌组\", en\.deckOr\(\"APP\"\), \"英语词汇\"\);\n\n", "", s3, count=1)
s3 = s3.replace('CardConfig al = builtins.get(1);', 'CardConfig al = builtins.get(1);')
s3 = s3.replace('CardConfig vo = builtins.get(2);\n        eq("英语词汇字段数", vo.fields.size(), 4);',
                'CardConfig vo = builtins.get(0);\n        eq("英语词汇字段数", vo.fields.size(), 4);')
s3 = s3.replace('JSONObject f = CardFormat.noteFieldsFor("apple", ai, en);', 'JSONObject f = CardFormat.noteFieldsFor("apple", ai, vo);')
s3 = re.sub(r"(?s)        JSONObject ai = new JSONObject\(\);\n        ai\.put\(\"phonetic\", \"英 /ˈæpl/\"\);\n.*?eq\(\"中文映射\", f\.optString\(\"中文\", \"\"\), \"苹果\"\);\n",
            '''        JSONObject ai = new JSONObject();
        ai.put("phonetic", "英 /ˈæpl/");
        ai.put("chinese", "苹果");
        JSONObject f = CardFormat.noteFieldsFor("apple", ai, vo);
        eq("笔记字段数 = 格式字段数", f.length(), 4);
        eq("正面=用户输入", f.optString("单词", ""), "apple");
        eq("中文映射", f.optString("中文", ""), "苹果");
''', s3, count=1)
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("SelfTest 处理完成")
