"""CreateView 行为接线：
   · 保存成功后按设置决定是否清空
   · 保存成功后按设置自动同步（可限定 Wi-Fi）
   · AI 填充完成后按设置自动切预览
   · 把当前皮肤（是否深色）推给编辑区
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\CreateView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 页面就绪时推皮肤
s = s.replace('''                editorReady = true;
                pushConfig();''', '''                editorReady = true;
                pushTheme();
                pushConfig();''', 1)

# 2) 保存成功后：按设置清空 + 按需自动同步
s = s.replace('''                                public void run() {
                                    js("setSaving(false)");
                                    clearEditor();
                                    status(ok, Ui.GREEN);
                                }''', '''                                public void run() {
                                    js("setSaving(false)");
                                    if (store.clearAfterSave()) {
                                        clearEditor();
                                    } else {
                                        js("setSaving(false)");
                                    }
                                    status(ok, Ui.GREEN);
                                    maybeAutoSync();
                                }''', 1)

# 3) AI 填充完成后按设置切预览
s = s.replace('''        pushFields(CardFormat.noteFieldsFor(word, parsedF, store.activeConfig()));''',
              '''        pushFields(CardFormat.noteFieldsFor(word, parsedF, store.activeConfig()));
        if (store.previewAfterFill()) js("setMode('preview')");''', 1)

# 4) 新方法
METHODS = '''
    /** 把皮肤（是否深色）推给编辑区，让卡片预览与编辑区跟着换色 */
    private void pushTheme() {
        js("setTheme(" + (Theme.byId(store.theme()).dark ? "true" : "false") + ")");
    }

    /** 保存后是否自动同步一次（可在设置里关掉；可选只在 Wi-Fi 下） */
    private void maybeAutoSync() {
        if (!store.autoSyncAfterSave()) return;
        if (store.ankiWebHkey().length() == 0) return;
        if (store.syncWifiOnly() && !onWifi()) return;
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(getContext(), store, null, null, null);
                    android.util.Log.i("AnkiAssistant", "自动同步：" + out.message);
                } catch (AnkiSync.FullSyncRequired f) {
                    android.util.Log.i("AnkiAssistant", "自动同步需要全量同步，跳过：" + f.reason);
                } catch (Exception e) {
                    android.util.Log.w("AnkiAssistant", "自动同步失败：" + e.getMessage());
                }
            }
        });
    }

    /** 当前是否在 Wi-Fi 上 */
    private boolean onWifi() {
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager)
                    getContext().getSystemService(android.content.Context.CONNECTIVITY_SERVICE);
            android.net.NetworkInfo ni = cm.getActiveNetworkInfo();
            return ni != null && ni.isConnected()
                    && ni.getType() == android.net.ConnectivityManager.TYPE_WIFI;
        } catch (Exception e) {
            return true;   // 查不到就不拦着
        }
    }
'''
s = s.replace("    private void pushWord(String word) {", METHODS + "\n    private void pushWord(String word) {", 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("CreateView 行为已接线")

# ---------------- editor.html：深色皮肤支持 ----------------
p2 = APP + r"\assets\editor.html"
h = io.open(p2, encoding="utf-8").read()
if "function setTheme(" not in h:
    css = '''/* 深色皮肤：只改网页部分的底色与文字，卡片预览跟着一起变 */
body.dark { background: #10131A; color: #E9EEF7; }
body.dark .field { background: #181C25; border-color: #2A3040; }
body.dark .fhead { color: #9AA6BC; }
body.dark .fedit { color: #E9EEF7; }
body.dark #topSticky { background: #141821; border-color: #2A3040; }
body.dark .tab { color: #9AA6BC; }
body.dark .tab.on { color: #6E9BFF; border-color: #6E9BFF; }
body.dark #toolbar { background: #181C25; border-color: #2A3040; }
body.dark .tb { color: #C6CEDC; }
body.dark .sheet { background: #181C25; border-color: #2A3040; }
body.dark .card { background: #10131A; }
body.dark .mlabel { color: #7C8798; }
body.dark .mtxt { background: #181C25; color: #E9EEF7; border-color: #2A3040; }
body.dark .notemeta { color: #7C8798; border-color: #2A3040; }
'''
    h = h.replace(".notemeta {", css + ".notemeta {", 1)
    h = h.replace("/* ---------------- 工具栏 ---------------- */",
                  '''/* 皮肤：Java 侧在建好页面后调用（true = 深色） */
function setTheme(isDark) {
  if (isDark) document.body.classList.add('dark');
  else document.body.classList.remove('dark');
}

/* ---------------- 工具栏 ---------------- */''', 1)
    io.open(p2, "w", encoding="utf-8", newline="\n").write(h)
    print("editor.html 深色皮肤已加")
