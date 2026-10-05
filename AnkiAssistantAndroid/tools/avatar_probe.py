"""加一个头像跨设备同步的自检探针：写一张头像进收藏库 → 再读回来。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

if "runAvatarProbe" not in s:
    PROBE = '''
    /** 调试用：`--ez avatarProbe true` 验证头像写入收藏库（媒体+配置）再读回 */
    private void runAvatarProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    String mail = store.ankiWebUser();
                    if (mail.length() == 0) mail = "probe@example.com";
                    android.graphics.Bitmap bmp = Avatar.letterBitmap(mail, 256);
                    boolean ok = Avatar.pushToCloud(MainActivity.this, mail, bmp);
                    android.util.Log.i("AnkiAssistant", "AVATAR push=" + ok
                            + " mediaName=" + Avatar.mediaName(mail));
                    java.io.File f = new java.io.File(Avatar.mediaDir(MainActivity.this),
                            Avatar.mediaName(mail));
                    android.util.Log.i("AnkiAssistant", "AVATAR mediaFile exists=" + f.exists()
                            + " size=" + (f.exists() ? f.length() : 0));
                    AnkiEngine e = EngineHolder.get(MainActivity.this);
                    String json = e.getConfigJson("ankiassistant.avatar."
                            + Avatar.mediaName(mail).replace("ankiassistant-avatar-", "").replace(".png", ""));
                    android.util.Log.i("AnkiAssistant", "AVATAR config=" + json);
                    Avatar.clear(MainActivity.this, mail);
                    android.graphics.Bitmap back = Avatar.pullFromCloud(MainActivity.this, mail, "custom");
                    android.util.Log.i("AnkiAssistant", "AVATAR pull=" + (back != null));
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "AVATAR PROBE FAIL: " + t, t);
                }
            }
        }).start();
    }

'''
    s = s.replace("    /** 调试用：`--ez restoreBackup true`", PROBE + "    /** 调试用：`--ez restoreBackup true`", 1)
    s = s.replace('        if (getIntent() != null && getIntent().getBooleanExtra("restoreBackup", false)) runRestoreBackup();',
                  '        if (getIntent() != null && getIntent().getBooleanExtra("avatarProbe", false)) runAvatarProbe();' + chr(13) + chr(10) +
                  '        if (getIntent() != null && getIntent().getBooleanExtra("restoreBackup", false)) runRestoreBackup();')
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("头像探针已加")
