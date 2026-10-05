"""同步调试入口：
   --ez syncReal true --es user <邮箱> --es pass <密码>   → 逐 RPC 打印同步过程（只下载，不上传）
   --ez wipeCollection true                              → 删掉本机收藏库（测试用）
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()

PROBE = '''    /**
     * 调试用：`--ez syncReal true --es user xxx --es pass yyy`
     * 逐 RPC 打印 AnkiWeb 同步过程，便于定位同步失败。**永不选择"上传"**，只会下载。
     * 凭据从 intent 读入，不写进代码。
     */
    private void runSyncRealProbe(final String user, final String pass) {
        new Thread(new Runnable() {
            @Override public void run() {
                AnkiEngine e;
                try {
                    e = EngineHolder.get(MainActivity.this);
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "SYNCREAL open engine fail: " + t, t);
                    return;
                }
                try {
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 login user=" + user);
                    String hkey = e.syncLogin(user, pass, null);
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 ok hkey.len=" + hkey.length());

                    int need = e.syncRequired(hkey);
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step2 syncRequired=" + need
                            + (need == 0 ? " (NO_CHANGES)" : need == 1 ? " (NORMAL)" : " (FULL)"));

                    if (need == 2) {
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 需要全量同步 → 只下载，绝不上传");
                        e.fullUploadOrDownload(hkey, false);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 full download ok");
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step4 media sync…");
                        e.syncMedia(hkey);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step4 media ok");
                    } else if (need == 1) {
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 普通同步（media=false 先试）");
                        int after = e.syncCollection(hkey, false);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 syncCollection -> " + after);
                    }
                    android.util.Log.i("AnkiAssistant", "SYNCREAL PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "SYNCREAL FAIL: " + t, t);
                } finally {
                    store.setLastSyncAt(System.currentTimeMillis());
                    Th.ui(new Runnable() {
                        @Override public void run() { updateSyncLamp(); }
                    });
                }
            }
        }).start();
    }

    /** 调试用：删掉本机收藏库（连带 media 目录），便于测试"首次同步" */
    private void runWipeCollection() {
        try {
            java.io.File dir = getFilesDir();
            String[] names = dir.list();
            int n = 0;
            if (names != null) {
                for (String name : names) {
                    if (name.startsWith("collection.anki2") || name.startsWith("collection.media")) {
                        if (new java.io.File(dir, name).delete()) n++;
                    }
                }
            }
            EngineHolder.close();
            android.util.Log.i("AnkiAssistant", "WIPE removed " + n + " collection files");
        } catch (Throwable t) {
            android.util.Log.e("AnkiAssistant", "WIPE FAIL: " + t, t);
        }
    }

'''
anchor = "    /** 调试用：`--ez configProbe true` 自建 config → 设为当前 → 用它写一张卡 */"
if "runSyncRealProbe" in src:
    print("已存在，跳过")
else:
    src = src.replace(anchor, PROBE + anchor, 1)
    src = src.replace(
        'if (getIntent() != null && getIntent().getBooleanExtra("configProbe", false)) runConfigProbe();',
        'if (getIntent() != null && getIntent().getBooleanExtra("configProbe", false)) runConfigProbe();\n'
        '        if (getIntent() != null && getIntent().getBooleanExtra("syncReal", false)) {\n'
        '            runSyncRealProbe(getIntent().getStringExtra("user"), getIntent().getStringExtra("pass"));\n'
        '        }\n'
        '        if (getIntent() != null && getIntent().getBooleanExtra("wipeCollection", false)) runWipeCollection();')
    io.open(path, "w", encoding="utf-8", newline="\n").write(src)
    print("同步调试入口已加入")
