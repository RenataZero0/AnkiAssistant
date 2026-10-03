"""修正媒体目录路径 + 把头像的收藏库同步接到界面上。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# 1) 媒体目录：引擎用的是 <collection>.media（即 collection.anki2 去掉 .anki2 后加 .media）
p = APP + r"\src\com\ankiassistant\Avatar.java"
s = io.open(p, encoding="utf-8").read()
s = s.replace('''    /** 收藏库媒体目录（引擎初始化时用的就是这个路径） */
    public static File mediaDir(Context c) {
        return new File(c.getFilesDir(), "collection.media");
    }''',
'''    /** 收藏库媒体目录：与 AnkiEngine.openCollection 里给后端的路径保持一致 */
    public static File mediaDir(Context c) {
        return new File(c.getFilesDir(), "collection.media");
    }''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)

# 引擎里 base = .../collection.anki2 → 去掉 ".anki2" 再加 ".media" → collection.media ✓ 已一致
print("媒体目录已核对：collection.media")

# 2) MainActivity：上传后写入收藏库；加载时先从收藏库找
p2 = APP + r"\src\com\ankiassistant\MainActivity.java"
s2 = io.open(p2, encoding="utf-8").read()

a = '                        store.setAvatarStyle(mail, "custom");\n                        loadAvatarAsync();'
b = ('                        store.setAvatarStyle(mail, "custom");\n'
     '                        Avatar.pushToCloud(MainActivity.this, mail, saved);   // 随收藏库同步到别的设备\n'
     '                        loadAvatarAsync();')
if a in s2:
    s2 = s2.replace(a, b, 1)
    print("上传后写入收藏库已接")
else:
    print("!! 上传接线未匹配")

c = '                final android.graphics.Bitmap b = Avatar.load(MainActivity.this, mail, style);'
d = ('                android.graphics.Bitmap b = Avatar.load(MainActivity.this, mail, style);\n'
     '                if (Avatar.cached(MainActivity.this, mail, style) == null) {\n'
     '                    android.graphics.Bitmap fromCloud =\n'
     '                            Avatar.pullFromCloud(MainActivity.this, mail, style);\n'
     '                    if (fromCloud != null) b = fromCloud;\n'
     '                }')
if c in s2:
    s2 = s2.replace(c, d, 1)
    print("加载时读收藏库已接")
else:
    print("!! 加载接线未匹配")

io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
