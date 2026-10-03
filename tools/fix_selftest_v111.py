"""更新自检：默认格式现在是「英语格式」，另外两个内置格式也要断言。"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# --- SelfTest：重写 config() 用例 ---
p = APP + r"\tools\SelfTest.java"
s = io.open(p, encoding="utf-8").read()
new_test = '''    static void config() throws Exception {
        System.out.println("== 输出格式 ==");
        java.util.List<CardConfig> builtins = CardConfig.builtins();
        eq("内置格式个数", builtins.size(), 3);
        eq("第一个是英语格式", builtins.get(0).name, "英语格式");
        eq("第二个是 A Level", builtins.get(1).name, "A Level Maths / Phy");
        eq("第三个是英语词汇", builtins.get(2).name, "英语词汇");
        eq("默认格式 = 英语格式", CardConfig.defaultConfig().name, "英语格式");

        CardConfig en = builtins.get(0);
        eq("英语格式字段数", en.fields.size(), 6);
        ok("英语格式正面用第一个字段", en.cardFront().indexOf("{{单词}}") >= 0, en.cardFront());
        ok("英语格式背面含音标行", en.cardBack().indexOf("【音标】") >= 0, "back");
        ok("英语格式提示词带词", en.buildPrompt("apple", "通用英语").indexOf("apple") >= 0, "p");
        ok("英语格式要求 JSON", en.buildPrompt("apple", "x").indexOf("JSON") >= 0, "p");
        eq("英语格式自带默认牌组", en.deckOr("APP"), "英语词汇");

        CardConfig al = builtins.get(1);
        eq("A Level 字段数", al.fields.size(), 7);
        eq("A Level 笔记类型", al.noteType, "专业术语卡");
        ok("A Level 背面含公式行", al.cardBack().indexOf("【关联公式/符号】") >= 0, "back");
        ok("A Level 提示词禁止独立公式", al.buildPrompt("x", "y").indexOf("不要使用 \\\\[ ... \\\\]") >= 0, "p");
        eq("A Level 沿用应用级默认牌组", al.deckOr("APP-DECK"), "APP-DECK");
        ok("A Level 用应用级默认值", CardConfig.usesAppDefaults(al.id), "flag");

        CardConfig vo = builtins.get(2);
        eq("英语词汇字段数", vo.fields.size(), 4);

        JSONObject ai = new JSONObject();
        ai.put("phonetic", "英 /ˈæpl/");
        ai.put("pos", "n");
        ai.put("definition", "(n) a round fruit");
        ai.put("example", "He ate an apple.");
        ai.put("chinese", "苹果");
        JSONObject f = CardFormat.noteFieldsFor("apple", ai, en);
        eq("笔记字段数 = 格式字段数", f.length(), 6);
        eq("正面=用户输入", f.optString("单词", ""), "apple");
        eq("释义映射", f.optString("释义", ""), "(n) a round fruit");
        eq("例句映射", f.optString("例句", ""), "He ate an apple.");
        eq("中文映射", f.optString("中文", ""), "苹果");

        // 自定义格式：JSON 往返 + 模板按自己的字段走
        CardConfig custom = new CardConfig();
        custom.id = "cfgtest";
        custom.name = "雅思词汇";
        custom.noteType = "雅思词汇";
        custom.prompt = "给 {word} 出词卡，学科 {subject}，只输出 JSON：\\nterm：词\\nmeaning：释义";
        custom.fields.add(new CardConfig.Field("词", "", "", false));
        custom.fields.add(new CardConfig.Field("释义", "meaning", "中文释义", false));
        CardConfig round = CardConfig.fromJson(custom.toJson());
        eq("自定义往返：名字", round.name, "雅思词汇");
        eq("自定义往返：字段数", round.fields.size(), 2);
        eq("自定义往返：第二个键", round.fields.get(1).key, "meaning");
        ok("自定义背面含释义行", round.cardBack().indexOf("【释义】") >= 0, "back");
        JSONObject ai2 = new JSONObject();
        ai2.put("meaning", "剑桥");
        JSONObject f2 = CardFormat.noteFieldsFor("cambridge", ai2, round);
        eq("自定义正面=用户输入", f2.optString("词", ""), "cambridge");
        eq("自定义释义来自 AI", f2.optString("释义", ""), "剑桥");
    }

'''
s = re.sub(r"(?s)    static void config\(\) throws Exception \{.*?\n    \}\n", new_test, s, count=1)
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("SelfTest.config() 已重写")

# --- SettingsView：补上 attachScrollSpy 调用 ---
p2 = APP + r"\src\com\ankiassistant\SettingsView.java"
s2 = io.open(p2, encoding="utf-8").read()
if "attachScrollSpy();" not in s2:
    s2 = s2.replace("        loadValues();\n    }", "        loadValues();\n        attachScrollSpy();\n    }", 1)
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("attachScrollSpy 已调用")
