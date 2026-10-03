"""浏览页详情改为复用编辑器（editor.html）的渲染管线：
   · WebView 载入 editorUrl()，等页面就绪后调用 showNoteFields(json)
   · 字段值先做老格式清理（自定义包壳 + 被切断的 LaTeX 合并）
   · 删掉自己那套 HTML/CSS 与 note.html 的做法，字体与排版就和制卡预览完全一致
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\BrowseView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 详情面板：WebView 载入编辑器页面，用 onPageFinished 置就绪标记
old_pane = '''        detailWeb = new WebView(getContext());
        detailWeb.getSettings().setJavaScriptEnabled(true);
        detailWeb.setBackgroundColor(0xFFFFFFFF);
        // 初始先给个空页面；每次打开详情时再把内容通过 note.html 输出过去。
        // 注意：这里**不能**在 onPageFinished 里再渲染一次，否则 loadUrl→onPageFinished→loadUrl 会无限重载（界面一直闪）
        detailWeb.setWebViewClient(new WebViewClient());
        detailWeb.loadUrl(act.assetBaseUrl() == null ? act.editorUrl()
                : act.assetBaseUrl() + "note.html");'''
new_pane = '''        detailWeb = new WebView(getContext());
        detailWeb.getSettings().setJavaScriptEnabled(true);
        detailWeb.setBackgroundColor(0xFFFFFFFF);
        // 详情直接复用制卡页的编辑器页面：字段样式、字号、行距与公式渲染都跟预览一模一样。
        // 载入完成后才允许注入内容（否则 JS 还没就绪）。
        detailWeb.setWebViewClient(new WebViewClient() {
            @Override public void onPageFinished(WebView view, String url) {
                detailWebReady = true;
                if (pendingNoteJson != null) pushNoteToWeb();
            }
        });
        detailWeb.loadUrl(act.editorUrl());'''
if old_pane in s:
    s = s.replace(old_pane, new_pane, 1)
    print("详情面板改为编辑器页面")
else:
    print("!! 详情面板未匹配")

# 2) 用 JS 注入替换掉原来的 HTML 生成 + loadUrl
start = s.find("    private String pendingHtml;")
end = s.find("    private void confirmDelete() {")
if start > 0 and end > start:
    new_block = '''    private String pendingNoteJson;
    private boolean detailWebReady;

    private void openDetail(JSONObject note) {
        currentNoteId = note.optLong("noteId", -1);
        pendingNoteJson = buildNoteJson(note);
        cardsPane.setVisibility(GONE);
        draftPane.setVisibility(GONE);
        detailPane.setVisibility(VISIBLE);
        pushNoteToWeb();
    }

    /** 页面就绪后把内容推进去（没就绪就等 onPageFinished 回调） */
    private void pushNoteToWeb() {
        if (detailWeb == null || pendingNoteJson == null || !detailWebReady) return;
        detailWeb.evaluateJavascript("showNoteFields(" + pendingNoteJson + ")", null);
    }

    private void closeDetail() {
        detailPane.setVisibility(GONE);
        cardsPane.setVisibility(VISIBLE);
        currentNoteId = -1;
    }

    /**
     * 组装给 editor.html 的 JSON：{model, tags, fields:[{name,value}]}
     * 字段值会先清掉老版本的自定义包壳、并把被切断的 LaTeX 接回去。
     */
    private String buildNoteJson(JSONObject note) {
        JSONObject out = new JSONObject();
        try {
            out.put("model", note.optString("modelName", ""));
            out.put("tags", tagString(note).replace("# ", ""));
            JSONArray list = new JSONArray();
            JSONObject fs = note.optJSONObject("fields");
            if (fs != null) {
                java.util.List<String> keys = new java.util.ArrayList<String>();
                java.util.Iterator<String> it = fs.keys();
                while (it.hasNext()) keys.add(it.next());
                final JSONObject fso = fs;
                java.util.Collections.sort(keys, new java.util.Comparator<String>() {
                    @Override public int compare(String a, String b) {
                        JSONObject oa = fso.optJSONObject(a);
                        JSONObject ob = fso.optJSONObject(b);
                        return (oa == null ? 999 : oa.optInt("order", 999))
                                - (ob == null ? 999 : ob.optInt("order", 999));
                    }
                });
                for (String k : keys) {
                    JSONObject f = fs.optJSONObject(k);
                    String v = f == null ? "" : f.optString("value", "");
                    v = LaTeX.clean(v);
                    if (v.replaceAll("(?s)<[^>]*>", "").trim().length() == 0) continue;
                    JSONObject one = new JSONObject();
                    one.put("name", k);
                    one.put("value", v);
                    list.put(one);
                }
            }
            out.put("fields", list);
        } catch (Exception ignored) { }
        return out.toString();
    }

'''
    s = s[:start] + new_block + s[end:]
    print("详情改为 JS 注入")
else:
    print("!! 详情区块定位失败 start=%d end=%d" % (start, end))

# 3) 清掉不再需要的 esc()/sanitizeField()（已挪到 LaTeX 工具类）
s = re.sub(r"(?s)    /\*\*\n     \* 老版本把整块内容包在.*?\n    \}\n\n", "", s, count=1)
s = re.sub(r"(?s)    private static String esc\(String s\) \{.*?\n    \}\n\n", "", s, count=1)

# 4) 去掉首屏渲染调用（原来 onPageFinished 里调 renderDetailHtml）
s = s.replace("    private void renderDetailHtml() {\n", "    private void renderDetailHtmlUnused() {\n")

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("BrowseView 处理完成")

# ---------- 新增 LaTeX 工具类（渲染与转换共用） ----------
latex = '''package com.ankiassistant;

/**
 * 老卡片里的 LaTeX 清理：
 *   · 去掉 <inline_latex_formula> 这类自定义包壳
 *   · 把被切断的数学环境接回去："\\\\(f = \\\\mu N\\\\),\\\\quad \\\\(f_k = \\\\mu_k N\\\\)"
 *     → "\\\\(f = \\\\mu N,\\\\quad f_k = \\\\mu_k N\\\\)"，这样 \\\\quad 才会真正渲染成空格
 */
public class LaTeX {

    public static String clean(String v) {
        if (v == null) return "";
        String s = v.replaceAll("(?i)</?(inline_latex_formula|anki-mathjax|latex_formula|mathjax_formula)[^>]*>", "");
        return mergeSplit(s);
    }

    /** 把只含 LaTeX 命令的间隙并回同一个数学环境 */
    public static String mergeSplit(String s) {
        if (s == null) return null;
        String cur = s;
        for (int i = 0; i < 8; i++) {
            String merged = cur.replaceAll("\\\\)([^()]*\\\\\\\\[a-zA-Z]+[^()]*)\\\\(", "$1");
            if (merged.equals(cur)) break;
            cur = merged;
        }
        return cur;
    }
}
'''
io.open(APP + r"\src\com\ankiassistant\LaTeX.java", "w", encoding="utf-8", newline="\n").write(latex)
print("LaTeX 工具类已创建")
