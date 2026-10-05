"""浏览页详情也要收到当前皮肤：以前只有制卡页推 setTheme，
   浏览页用的是同一个 editor.html，却一直停在默认（浅色），深色下就显得很怪。
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\BrowseView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

old = '''        detailWeb.setWebViewClient(new WebViewClient() {
            @Override public void onPageFinished(WebView view, String url) {
                detailWebReady = true;
                if (pendingNoteJson != null) pushNoteToWeb();
            }
        });'''
new = '''        detailWeb.setWebViewClient(new WebViewClient() {
            @Override public void onPageFinished(WebView view, String url) {
                detailWebReady = true;
                pushThemeToWeb();          // 先套用当前皮肤，否则深色下这块是浅色的
                if (pendingNoteJson != null) pushNoteToWeb();
            }
        });'''
if old in s:
    s = s.replace(old, new, 1)
    print("onPageFinished 里已加推送主题")
else:
    print("!! onPageFinished 未匹配")

# 新方法 + pushNoteToWeb 里也补一次（换皮肤重建界面后同样会走到）
if "pushThemeToWeb" not in s.replace(old, new):
    s = s.replace('''    /** 页面就绪后把内容推进去（没就绪就等 onPageFinished 回调） */''',
'''    /** 把当前皮肤推给详情页（与制卡页共用同一套 CSS 变量） */
    private void pushThemeToWeb() {
        if (detailWeb == null) return;
        try {
            detailWeb.evaluateJavascript(
                    "setTheme(" + Theme.byId(store.theme()).webVars() + ")", null);
        } catch (Exception ignored) { }
    }

    /** 页面就绪后把内容推进去（没就绪就等 onPageFinished 回调） */''')

s = s.replace('''        detailWeb.evaluateJavascript("showNoteFields(" + pendingNoteJson + ")", null);''',
              '''        pushThemeToWeb();
        detailWeb.evaluateJavascript("showNoteFields(" + pendingNoteJson + ")", null);''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("pushThemeToWeb 已加并在注入前调用")
