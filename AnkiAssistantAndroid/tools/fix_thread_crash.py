"""修「Only the original thread that created a view hierarchy can touch its views」：
   同步成功后的刷新（头像、状态灯）是在后台线程里做的，必须回到主线程。
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) loadAvatarAsync 内部对控件的操作统统回到主线程
old = '''    private void loadAvatarAsync() {
        final String mail = store.ankiWebUser();
        final String style = store.avatarStyle(mail);
        android.graphics.Bitmap cached = Avatar.cached(this, mail, style);
        if (cached != null) {
            if (accountAvatar != null) accountAvatar.setImageBitmap(cached);
            if (topAvatar != null) topAvatar.setImageBitmap(cached);
        }'''
new = '''    private void loadAvatarAsync() {
        final String mail = store.ankiWebUser();
        final String style = store.avatarStyle(mail);
        final android.graphics.Bitmap cached = Avatar.cached(this, mail, style);
        // 这个方法可能从后台线程（同步成功后）调用，碰控件必须先回主线程
        Th.ui(new Runnable() {
            @Override public void run() {
                if (cached != null) {
                    if (accountAvatar != null) accountAvatar.setImageBitmap(cached);
                    if (topAvatar != null) topAvatar.setImageBitmap(cached);
                }
            }
        });'''
if old in s:
    s = s.replace(old, new, 1)
    print("loadAvatarAsync 已回主线程")
else:
    print("!! loadAvatarAsync 未匹配")

# 2) 同步成功后：状态灯与头像刷新都走主线程
s = s.replace('''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    updateSyncLamp();
                    loadAvatarAsync();   // 另一端刚上传的头像，同步完就能显示''',
'''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    Th.ui(new Runnable() {
                        @Override public void run() { updateSyncLamp(); }
                    });
                    loadAvatarAsync();   // 另一端刚上传的头像，同步完就能显示''')

s = s.replace('''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);
                    loadAvatarAsync();''',
'''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);
                    Th.ui(new Runnable() {
                        @Override public void run() { updateSyncLamp(); }
                    });
                    loadAvatarAsync();''')

# 3) 同步失败分支里的 updateSyncLamp 同样处理
s = s.replace('''                } catch (final Exception e) {
                    toastUi("同步失败：" + e.getMessage());
                    updateSyncLamp();
                }''',
'''                } catch (final Exception e) {
                    toastUi("同步失败：" + e.getMessage());
                    Th.ui(new Runnable() {
                        @Override public void run() { updateSyncLamp(); }
                    });
                }''')

# 4) updateSyncLamp 自己也兜一层（无论谁调用都安全）
s = s.replace('''    public void updateSyncLamp() {
        boolean loggedIn = store.ankiWebKey != null ? false : false;''', '''    public void updateSyncLamp() {''')
if "private void updateSyncLampOnUi()" not in s:
    s = s.replace('''    /** 左下角指示灯：反映 AnkiWeb 登录/同步状态（顺带更新头像下面的小字） */
    public void updateSyncLamp() {''',
'''    /** 左下角指示灯：反映 AnkiWeb 登录/同步状态（顺带更新头像下面的小字） */
    public void updateSyncLamp() {
        if (android.os.Looper.myLooper() != android.os.Looper.getMainLooper()) {
            Th.ui(new Runnable() {
                @Override public void run() { updateSyncLamp(); }
            });
            return;
        }''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("同步后的界面刷新已回主线程")
