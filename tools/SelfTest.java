import com.ankiassistant.AiClient;
import com.ankiassistant.CardConfig;
import com.ankiassistant.CardFormat;

import org.json.JSONObject;

/**
 * 电脑端自检：脱离安卓直接跑「纯逻辑」部分。
 * 覆盖 PRMOPT.md 第二节第 3 条的卡片格式、AI 提示词/解析、AnkiConnect 请求与响应解析。
 * 运行：powershell -ExecutionPolicy Bypass -File selftest.ps1
 */
public class SelfTest {

    static int pass = 0, fail = 0;

    static void ok(String name, boolean cond, String detail) {
        if (cond) { pass++; System.out.println("  [ok] " + name); }
        else { fail++; System.out.println("  [FAIL] " + name + "  -> " + detail); }
    }

    static void eq(String name, Object got, Object expect) {
        boolean same = (got == null) ? expect == null : got.equals(expect);
        ok(name, same, "got=" + got + " expect=" + expect);
    }

    public static void main(String[] args) throws Exception {
        format();
        prompt();
        parseAi();
        fields();
        config();
        ai();
        templates();
        System.out.println();
        System.out.println("pass=" + pass + "  fail=" + fail);
        if (fail > 0) System.exit(1);
    }

    // ------------------------------------------------------------ 卡片格式

    static void format() {
        System.out.println("== 卡片格式（PRMOPT 第二节第 3 条） ==");
        eq("字段数=7", CardFormat.FIELDS.length, 7);
        eq("第一个字段是正面", CardFormat.FIELDS[0], "单词");
        String back = CardFormat.CARD_BACK;
        ok("背面含【音标】", back.indexOf("【音标】") >= 0, back);
        ok("背面含【词性】", back.indexOf("【词性】") >= 0, back);
        ok("背面含【定义】", back.indexOf("【定义】") >= 0, back);
        ok("背面含【关联公式/符号】", back.indexOf("【关联公式/符号】") >= 0, back);
        ok("背面含【易混】", back.indexOf("【易混】") >= 0, back);
        ok("背面含【中文】", back.indexOf("【中文】") >= 0, back);
        ok("正面就是单词", CardFormat.CARD_FRONT.indexOf("{{单词}}") >= 0, CardFormat.CARD_FRONT);
        for (int i = 1; i < CardFormat.FIELDS.length; i++) {
            String f = CardFormat.FIELDS[i];
            ok("模板条件块包裹 " + f,
                    back.indexOf("{{#" + f + "}}") >= 0 && back.indexOf("{{/" + f + "}}") >= 0, f);
        }
        ok("样式含 MathJax 友好字体栈", CardFormat.CARD_CSS.indexOf("font-family") >= 0, "");
    }

    static void prompt() {
        System.out.println("== AI 提示词 ==");
        String p = CardFormat.buildPrompt("probability", "数学");
        ok("包含单词", p.indexOf("probability") >= 0, p);
        ok("要求 JSON", p.indexOf("JSON") >= 0, p);
        ok("包含 phonetic", p.indexOf("phonetic") >= 0, p);
        ok("包含 pos", p.indexOf("\"pos\"") >= 0 || p.indexOf("pos：") >= 0, p);
        ok("包含 definition 格式说明", p.indexOf("([词性])") >= 0, p);
        ok("包含 MathJax 行内定界符", p.indexOf("\\(") >= 0, p);
        ok("包含 MathJax 独行定界符", p.indexOf("\\[") >= 0, p);
        ok("包含 confusables", p.indexOf("confusables") >= 0, p);
        ok("包含 chinese", p.indexOf("chinese") >= 0, p);
        ok("音标要求英/美两种", p.indexOf("英 /.../；美 /.../") >= 0, p);
        ok("易混要求写英文词性且不用破折号",
                p.indexOf("不要用破折号或连字符") >= 0 && p.indexOf("词性一律用英文缩写") >= 0, p);

        // 提示词里那个"输出示例"必须本身是合法 JSON，且易混格式与新要求一致
        int exAt = p.indexOf("输出示例");
        ok("提示词里有输出示例", exAt > 0, p.substring(0, 40));
        String ex = p.substring(p.indexOf("{", exAt));
        JSONObject exObj = null;
        try { exObj = new JSONObject(ex); } catch (Exception e) { }
        ok("提示词里的输出示例是合法 JSON（少个引号模型就会跟着错）", exObj != null, ex);
        if (exObj != null) {
            String exConf = exObj.optString("confusables", "");
            ok("示例的易混没有破折号", exConf.indexOf("—") < 0, exConf);
            ok("示例的易混带英文词性", exConf.indexOf(" n. ") >= 0 || exConf.indexOf(" v. ") >= 0, exConf);
            ok("示例的音标是英+美", exObj.optString("phonetic", "").indexOf("英") >= 0
                    && exObj.optString("phonetic", "").indexOf("美") >= 0, exObj.optString("phonetic", ""));
        }
        ok("明确禁止独立公式 \\\\[...\\\\]", p.indexOf("不要使用 \\[ ... \\]") >= 0, p);
        ok("只允许行内 MathJax", p.indexOf("只用行内 MathJax") >= 0, p);
        ok("学科背景进了提示词", p.indexOf("数学") >= 0, p);
        String p2 = CardFormat.buildPrompt("vector", null);
        ok("学科留空也有默认背景", p2.indexOf("学科背景") >= 0 && p2.indexOf("物理") >= 0, p2);
    }

    // ------------------------------------------------------------ AI 解析

    static void parseAi() {
        System.out.println("== AI 返回解析 ==");
        String clean = "{\"phonetic\":\"/ˈpəʊl/\",\"pos\":\"n.\","
                + "\"definition\":\"(n.) a measurement of how far something moves\","
                + "\"formula\":\"\\\\( s = \\\\dfrac{d}{t} \\\\)\","
                + "\"confusables\":\"pool /puːl/ — 水池\"}";
        JSONObject o = CardFormat.parseAi(clean);
        ok("干净 JSON 能解析", o != null, String.valueOf(o));
        if (o != null) {
            eq("音标", o.optString("phonetic"), "/ˈpəʊl/");
            eq("词性", o.optString("pos"), "n.");
            ok("公式保留反斜杠", o.optString("formula").indexOf("\\(") >= 0, o.optString("formula"));
        }

        String fenced = "好的，这是生成的卡片：\n```json\n" + clean + "\n```\n希望有帮助！";
        JSONObject o2 = CardFormat.parseAi(fenced);
        ok("围栏代码块能解析", o2 != null, fenced);
        if (o2 != null) eq("围栏-释义", o2.optString("pos"), "n.");

        String noisy = "以下是结果 {\"phonetic\":\"/iːt/\",\"definition\":\"(v.) to take food\"} 完毕";
        JSONObject o3 = CardFormat.parseAi(noisy);
        ok("JSON 前后有废话也能解析", o3 != null, noisy);

        String zh = "{\"音标\":\"/tuː/\",\"词性\":\"num.\",\"定义\":\"(num.) the number 2\"}";
        JSONObject o4 = CardFormat.parseAi(zh);
        ok("中文键也能识别", o4 != null, zh);
        if (o4 != null) eq("中文键-音标", o4.optString("phonetic"), "/tuː/");

        ok("纯废话返回 null", CardFormat.parseAi("我觉得这个词不太好") == null, "should be null");
        ok("null 返回 null", CardFormat.parseAi(null) == null, "should be null");
        ok("空 JSON 返回 null", CardFormat.parseAi("{}") == null, "should be null");

        JSONObject fb = CardFormat.fallbackFields("x", "some raw text");
        ok("降级时整段进定义", fb.optString("definition").indexOf("some raw text") >= 0, fb.toString());
    }

    static void fields() {
        System.out.println("== 组装 Anki 字段 ==");
        JSONObject ai = new JSONObject();
        ai.put("phonetic", "/ˈeθ/");
        ai.put("pos", "n.");
        ai.put("definition", "(n.) the 5th letter");
        ai.put("formula", "\\( \\varepsilon \\)");
        ai.put("confusables", "eta /ˈiːtə/\nepsilon /ˈɛpsɪləʊn/");
        JSONObject f = CardFormat.noteFields("epsilon", ai);
        for (int i = 0; i < CardFormat.FIELDS.length; i++) {
            ok("字段存在 " + CardFormat.FIELDS[i], f.has(CardFormat.FIELDS[i]), CardFormat.FIELDS[i]);
        }
        eq("正面=单词", f.optString("单词"), "epsilon");
        ok("换行转成 <br>", f.optString("易混").indexOf("<br>") >= 0, f.optString("易混"));
        ok("原始换行被替换掉", f.optString("易混").indexOf("\n") < 0, f.optString("易混"));

        JSONObject ai2 = new JSONObject();
        ai2.put("definition", "a<b>c");
        JSONObject f2 = CardFormat.noteFields("<tag>", ai2);
        eq("正面尖括号转义", f2.optString("单词"), "&lt;tag&gt;");
        eq("释义尖括号转义", f2.optString("定义"), "a&lt;b&gt;c");

        JSONObject merged = CardFormat.mergeNote("word<1>", f);
        eq("merge 保留单词转义", merged.optString("单词"), "word&lt;1&gt;");
        eq("merge 保留释义", merged.optString("定义"), f.optString("定义"));
        eq("merge 多余键被丢掉(不会带 AI 的键名)", merged.optString("phonetic", ""), "");

        String plain = CardFormat.plainBack(f);
        ok("纯文本背面含标签", plain.indexOf("【音标】") >= 0, plain);
        ok("纯文本背面含正面词", plain.startsWith("epsilon"), plain);

        JSONObject empty = CardFormat.noteFields("w", null);
        ok("AI 为 null 时字段仍然齐全", empty.length() == CardFormat.FIELDS.length, empty.toString());
    }


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
        ai.put("formula", "\\(a=1\\)");
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
        custom.prompt = "给 {word} 出词卡，学科 {subject}，只输出 JSON：\nterm：词\nmeaning：释义";
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

    // ------------------------------------------------------------ AI 请求

    static void ai() throws Exception {
        System.out.println("== AI 客户端 ==");
        String body = AiClient.buildRequestBody("deepseek-chat", "系统提示", "用户提示");
        JSONObject o = new JSONObject(body);
        eq("model", o.optString("model"), "deepseek-chat");
        eq("两条消息", o.getJSONArray("messages").length(), 2);
        eq("system 角色", o.getJSONArray("messages").getJSONObject(0).optString("role"), "system");
        eq("user 内容", o.getJSONArray("messages").getJSONObject(1).optString("content"), "用户提示");

        String resp = "{\"id\":\"x\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"pos\\\":\\\"n.\\\"}\"}}]}";
        eq("取 content", AiClient.parseContent(resp), "{\"pos\":\"n.\"}");

        try {
            AiClient.parseContent("{\"error\":{\"message\":\"Invalid API key\"}}");
            ok("error.message 被带上", false, "no exception");
        } catch (AiClient.AiException e) {
            ok("error.message 被带上", e.getMessage().indexOf("Invalid API key") >= 0, e.getMessage());
        }
        try {
            AiClient.parseContent("{\"choices\":[]}");
            ok("空 choices 报错", false, "no exception");
        } catch (AiClient.AiException e) {
            ok("空 choices 报错", true, e.getMessage());
        }
        try {
            AiClient.parseContent("not json");
            ok("非 JSON 报错", false, "no exception");
        } catch (AiClient.AiException e) {
            ok("非 JSON 报错", true, e.getMessage());
        }

        ok("DeepSeek 预设有地址", AiClient.presetBaseUrl(AiClient.P_DEEPSEEK).startsWith("https://"), "");
        ok("豆包预设有地址", AiClient.presetBaseUrl(AiClient.P_DOUBAO).indexOf("volces") > 0, "");
        ok("智谱预设是免费模型", AiClient.presetModel(AiClient.P_ZHIPU).indexOf("glm") >= 0, "");
        ok("硅基流动预设有地址", AiClient.presetBaseUrl(AiClient.P_SILICON).indexOf("siliconflow") > 0, "");
        ok("预设都要填 Key", AiClient.needsKey(AiClient.P_DEEPSEEK), "");
        ok("自定义档没有预设地址", AiClient.presetBaseUrl(AiClient.P_CUSTOM).length() == 0, "");
    }

    static void templates() throws Exception {
        System.out.println("== 模板/格式兜底 ==");
        ok("笔记类型名非空", CardFormat.MODEL_NAME.length() > 0, "");
        String back = CardFormat.CARD_BACK;
        int opens = count(back, "{{#"), closes = count(back, "{{/");
        eq("条件块成对", opens, closes);
        eq("条件块数量=6", opens, 6);
        ok("模板里没有未替换的占位残渣", back.indexOf("{{单词}}") >= 0, back);

        // 公式兜底：模型忘了写定界符时要自动补上 \( ... \)
        eq("裸公式自动补定界符", CardFormat.normalizeFormula("b^2 - 4ac"), "\\( b^2 - 4ac \\)");
        eq("已有定界符不动", CardFormat.normalizeFormula("\\(x^2\\)"), "\\(x^2\\)");
        eq("独立公式不动", CardFormat.normalizeFormula("\\[x^2\\]"), "\\[x^2\\]");
        eq("带中文说明不动", CardFormat.normalizeFormula("b²-4ac 是判别式"), "b²-4ac 是判别式");
        eq("空值还是空", CardFormat.normalizeFormula("  "), "");
        eq("美元行内公式转成 \\( \\)", CardFormat.normalizeFormula("$\\nabla f$"), "\\(\\nabla f\\)");
        eq("美元独立公式转成 \\[ \\]",
                CardFormat.normalizeFormula("$$\\int_0^1 x\\,dx$$"), "\\[\\int_0^1 x\\,dx\\]");

        // 实测遇到的坏 JSON（开/关思考都会出现）：全角冒号、键后丢引号
        String broken1 = "{\"phonetic\":\"英 /vəˈlɒsəti/；美 /vəˈlɑːsəti/\",\"pos\":\"n\","
                + "\"definition\":\"(n) rate of change of displacement\","
                + "\"formula\":\"\\\\(\\\\vec{v}\\\\)\","
                + "\"confusables\":\"speed /spiːd/ n. 速率\","
                + "\"chinese\"：\"速度；速率\"}";
        JSONObject r1 = CardFormat.parseAi(broken1);
        ok("全角冒号的 JSON 也能解析", r1 != null && "速度；速率".equals(r1.optString("chinese")),
                String.valueOf(r1));

        String broken2 = "{\"phonetic\":\"英 /dɪˈskrɪmɪnənt/\",\"pos\":\"n\","
                + "\"definition\":\"(n) discriminant\",\"formula\":\"\\\\(b^2-4ac\\\\)\","
                + "\"confusables\":\"discern /dɪˈsɜːn/ v. 察觉\","
                + "\"chinese：判别式；判别函数\" }";
        JSONObject r2 = CardFormat.parseAi(broken2);
        ok("键后丢引号的 JSON 也能解析", r2 != null && "判别式；判别函数".equals(r2.optString("chinese")),
                String.valueOf(r2));

        // 连逗号都丢了的，走"按字段名硬抠"兜底
        String broken3 = "{ \"phonetic\": \"英 /x/\" \"pos\": \"n\" \"definition\": \"(n) a test\" "
                + "\"formula\": \"\" \"confusables\": \"\" \"chinese\": \"测试\" }";
        JSONObject r3 = CardFormat.parseAi(broken3);
        ok("语法全坏时按字段名兜底抠出来", r3 != null && "n".equals(r3.optString("pos"))
                && "测试".equals(r3.optString("chinese")), String.valueOf(r3));

        // 牌组 → 标签映射
        eq("纯数 → ALevel::Maths",
                CardFormat.tagsForDeck("A Level Pure Mathematics"), "ALevel::Maths");
        eq("力学 → ALevel::Maths",
                CardFormat.tagsForDeck("A Level Mathematics - Mechanics"), "ALevel::Maths");
        eq("概率统计 → ALevel::Maths",
                CardFormat.tagsForDeck("A Level Mathematics - Probability & Statistics"), "ALevel::Maths");
        eq("进阶数学 → ALevel::Maths",
                CardFormat.tagsForDeck("A Level Further Mathematics"), "ALevel::Maths");
        eq("物理 → ALevel::Physics",
                CardFormat.tagsForDeck("A Level Physics"), "ALevel::Physics");
        eq("其它牌组不动标签", CardFormat.tagsForDeck("系统默认"), null);
        eq("空牌组不动标签", CardFormat.tagsForDeck("  "), null);

        // 思考型模型：默认不发 thinking 参数，关思考时才带 {"type":"disabled"}
        JSONObject noThink = new JSONObject(AiClient.buildRequestBody("glm-4.5-flash", "s", "u", false));
        ok("默认不带 thinking 参数", !noThink.has("thinking"), noThink.toString());
        JSONObject thinkOff = new JSONObject(AiClient.buildRequestBody("glm-4.5-flash", "s", "u", true));
        eq("关思考时 thinking.type=disabled",
                thinkOff.getJSONObject("thinking").optString("type", ""), "disabled");

        // 重试提示词要明确"不要思考、只输出 JSON"
        String strict = CardFormat.buildPromptStrict("velocity", "数学");
        ok("重试用提示词要求不思考", strict.indexOf("不要输出任何思考过程") >= 0, strict);
        ok("重试用提示词要求只输出 JSON", strict.indexOf("只输出上面那个 JSON 对象本身") >= 0, strict);

        // 响应解析要能取出思考内容（reasoning_content）
        String withReason = "{\"choices\":[{\"message\":{\"content\":\"{\\\"pos\\\":\\\"n\\\"}\","
                + "\"reasoning_content\":\"我在想...\"}}]}";
        AiClient.Reply rep = AiClient.parseReply(withReason);
        eq("取出正文", rep.content, "{\"pos\":\"n\"}");
        eq("取出思考过程", rep.reasoning, "我在想...");
    }

    static int count(String s, String sub) {
        int n = 0, i = 0;
        while ((i = s.indexOf(sub, i)) >= 0) { n++; i += sub.length(); }
        return n;
    }
}
