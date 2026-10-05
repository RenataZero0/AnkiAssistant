"""弹窗里看不到头像：
   收藏库拉回来的头像按 "custom" 存档，而弹窗按"当前风格"（如 auto）去查缓存 → 查不到 → 空白。
   统一改用 anyCached()：先看 custom，再看当前风格。
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# 1) Avatar：加 anyCached
p = APP + r"\src\com\ankiassistant\Avatar.java"
s = io.open(p, encoding="utf-8").read()
if "anyCached" not in s:
    s = s.replace('''    /**
     * 取头像：先缓存，再联网，最后本地生成。**必须在后台线程调用**。
     */''',
'''    /**
     * 找一个能用的缓存：先看"自己上传"的那份（收藏库拉回来的也存这里），再看当前风格的。
     * 界面要立刻显示头像时用这个，别只查当前风格。
     */
    public static Bitmap anyCached(Context c, String email, String style) {
        Bitmap b = cached(c, email, "custom");
        if (b == null && style != null && !"custom".equals(style)) b = cached(c, email, style);
        if (b == null && !"auto".equals(style)) b = cached(c, email, "auto");
        return b;
    }

    /**
     * 取头像：先缓存，再联网，最后本地生成。**必须在后台线程调用**。
     */''', 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("Avatar.anyCached 已加")

# 2) MainActivity：弹窗与首屏都改用 anyCached
p2 = APP + r"\src\com\ankiassistant\MainActivity.java"
s2 = io.open(p2, encoding="utf-8").read()
s2 = s2.replace('android.graphics.Bitmap av = Avatar.cached(this, mail, store.avatarStyle(mail));',
                'android.graphics.Bitmap av = Avatar.anyCached(this, mail, store.avatarStyle(mail));')
s2 = s2.replace('final android.graphics.Bitmap cached = Avatar.cached(this, mail, style);',
                'final android.graphics.Bitmap cached = Avatar.anyCached(this, mail, style);')
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("弹窗与首屏已改用 anyCached")

# 3) 弹窗头像即使没图也得有底衬（否则透明=空），给它一层浅色底
s2 = io.open(p2, encoding="utf-8").read()
s2 = s2.replace('''            avatar.setBackground(Ui.round(0x00000000, 23));''',
                '''            avatar.setBackground(Ui.round(Ui.PANEL, 23));   // 还没加载出来时也有个可见的底''')
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("弹窗头像底衬已给")
