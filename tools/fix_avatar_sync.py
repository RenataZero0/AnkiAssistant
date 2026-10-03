"""头像跨设备：
   1) 每次都先看收藏库里有没有（有就用它，而不是"本地没缓存才去看"）——
      否则平板上早就缓存了 Gravatar 头像，永远轮不到你上传的那张
   2) 同步成功后刷新头像，这样另一端的头像刚同步完就能看到
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 加载顺序：收藏库 → 本地缓存/Gravatar
old = '''        Th.bg(new Runnable() {
            @Override public void run() {
                android.graphics.Bitmap loaded = Avatar.load(MainActivity.this, mail, style);
                if (Avatar.cached(MainActivity.this, mail, style) == null) {
                    android.graphics.Bitmap fromCloud =
                            Avatar.pullFromCloud(MainActivity.this, mail, style);
                    if (fromCloud != null) loaded = fromCloud;
                }'''
new = '''        Th.bg(new Runnable() {
            @Override public void run() {
                // 先看收藏库：这是"另一台设备上传的那张"，优先级高于本地缓存与 Gravatar
                android.graphics.Bitmap fromCloud =
                        Avatar.pullFromCloud(MainActivity.this, mail, "custom");
                android.graphics.Bitmap loaded = (fromCloud != null)
                        ? fromCloud
                        : Avatar.load(MainActivity.this, mail, style);'''
if old in s:
    s = s.replace(old, new, 1)
    print("头像加载顺序已改为收藏库优先")
else:
    print("!! 加载逻辑未匹配")

# 2) 同步成功后刷新头像
s = s.replace('''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    updateSyncLamp();''',
'''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    updateSyncLamp();
                    loadAvatarAsync();   // 另一端刚上传的头像，同步完就能显示''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("同步后刷新头像已加")

# 3) 全量同步后也刷新
s = io.open(p, encoding="utf-8").read()
s = s.replace('''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);''',
'''                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);
                    loadAvatarAsync();''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("全量同步后刷新头像已加")
