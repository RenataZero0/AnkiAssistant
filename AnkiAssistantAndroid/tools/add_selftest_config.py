"""给 SelfTest 加 config 用例。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

path = APP + r"\tools\SelfTest.java"
src = io.open(path, encoding="utf-8").read()

METHOD = '''
    // ------------------------------------------------------------ 输出格式 config

    static void config() throws Exception {
        System.out.println("== 输出格式 config ==");
        CardConfig def = CardConfig.defaultConfig();
        eq("默认 config 字段数", def.fields.size(), 7);
        eq("默认笔记类型", def.noteType, "专业术语卡");
        ok("正面模板用第一个字段", def.cardFront().indexOf("{{单词}}") >= 0, def.cardFront());
        ok("背面含音标行", def.cardBack().indexOf("【音标】") >= 0, "back");
        ok("背面含公式行", def.cardBack().indexOf("【关联公式/符号】") >= 0, "back");
        ok("提示词带上了词", def.buildPrompt("epsilon", "物理").indexOf("epsilon") >= 0, "prompt");
        ok("提示词带上了学科", def.buildPrompt("epsilon", "物理").indexOf("物理") >= 0, "prompt");
        ok("提示词要求只输出 JSON", def.buildPrompt("x", "y").indexOf("JSON") >= 0, "prompt");

        // AI 字段 -> 笔记字段（默认 config）
        JSONObject ai = new JSONObject();
        ai.put("phonetic", "英 /x/");
        ai.put("pos", "n");
        ai.put("definition", "(n) something");
        ai.put("formula", "\\\\(a=1\\\\)");
        ai.put("confusables", "a /x/ n. 甲");
        ai.put("chinese", "乙");
        JSONObject f = CardFormat.noteFieldsFor("epsilon", ai, def);
        eq("笔记字段数 = config 字段数", f.length(), 7);
        eq("正面字段=单词", f.optString("单词", ""), "epsilon");
        eq("音标映射", f.optString("音标", ""), "英 /x/");
        eq("中文映射", f.optString("中文", ""), "乙");
        eq("正面的键不来自 AI", f.optString("单词", ""), "epsilon");

        // 自定义 config：JSON 往返 + 模板/提示词按自己的字段走
        CardConfig custom = new CardConfig();
        custom.id = "cfgtest";
        custom.name = "雅思词汇";
        custom.noteType = "雅思词汇";
        custom.prompt = "给 {word} 出词卡，学科 {subject}，只输出 JSON：\\nterm：词\\nmeaning：释义";
        custom.fields.add(new CardConfig.Field("词", "", "", false));
        custom.fields.add(new CardConfig.Field("释义", "meaning", "中文释义", false));
        CardConfig round = CardConfig.fromJson(custom.toJson());
        eq("config 往返：名字", round.name, "雅思词汇");
        eq("config 往返：字段数", round.fields.size(), 2);
        eq("config 往返：第二个字段的键", round.fields.get(1).key, "meaning");
        ok("自定义正面模板", round.cardFront().indexOf("{{词}}") >= 0, round.cardFront());
        ok("自定义背面含释义行", round.cardBack().indexOf("【释义】") >= 0, "back");
        ok("自定义提示词替换 {word}", round.buildPrompt("cambridge", "英语").indexOf("cambridge") >= 0, "p");
        JSONObject ai2 = new JSONObject();
        ai2.put("term", "忽略");
        ai2.put("meaning", "剑桥");
        JSONObject f2 = CardFormat.noteFieldsFor("cambridge", ai2, round);
        eq("自定义字段数", f2.length(), 2);
        eq("自定义正面=用户输入", f2.optString("词", ""), "cambridge");
        eq("自定义释义来自 AI", f2.optString("释义", ""), "剑桥");
    }

'''

anchor = "    // ------------------------------------------------------------ AI 请求"
if "static void config()" in src:
    print("已存在，跳过")
else:
    src = src.replace(anchor, METHOD + anchor, 1)
    # 调用它
    src = src.replace("        anki();", "        config();") if "        anki();" in src else src
    if "        config();" not in src:
        # 找到 main 里其它调用点
        for cand in ("        ai();", "        collection();", "        cardformat();", "        formulas();"):
            if cand in src:
                src = src.replace(cand, "        config();\n" + cand, 1)
                break
    src = src.replace("import com.ankiassistant.CardFormat;",
                      "import com.ankiassistant.CardConfig;\nimport com.ankiassistant.CardFormat;")
    io.open(path, "w", encoding="utf-8", newline="\n").write(src)
    print("已加入 config 用例")

# selftest.ps1：把 CardConfig.java 加进编译列表
sp = APP + r"\selftest.ps1"
s = io.open(sp, encoding="utf-8").read()
if "CardConfig.java" not in s:
    s = s.replace('"$here\\src\\com\\ankiassistant\\CardFormat.java",',
                  '"$here\\src\\com\\ankiassistant\\CardConfig.java",\n    "$here\\src\\com\\ankiassistant\\CardFormat.java",')
    io.open(sp, "w", encoding="utf-8", newline="\n").write(s)
    print("selftest.ps1 已加入 CardConfig.java")
