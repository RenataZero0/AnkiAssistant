"""头像做成圆形：
   · 在写入缓存这个唯一入口做圆形遮罩 —— 相册、网址、Gravatar、收藏库拉回来的都会是圆的
   · 一次性清掉旧缓存，让已经设置过的头像立刻变圆
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

p = APP + r"\src\com\ankiassistant\Avatar.java"
s = io.open(p, encoding="utf-8").read()

# 1) save 改成返回"圆角后的那张"
old_save = '''    private static void save(Context c, String email, String style, Bitmap b) {
        if (b == null) return;
        try {
            File f = cacheFile(c, email, style);
            FileOutputStream out = new FileOutputStream(f);
            b.compress(Bitmap.CompressFormat.PNG, 100, out);
            out.close();
        } catch (Exception ignored) { }
    }'''
new_save = '''    /** 写入缓存；统一在这里做圆形遮罩，返回"圆的那张"给界面直接使用 */
    private static Bitmap save(Context c, String email, String style, Bitmap b) {
        if (b == null) return null;
        Bitmap round = circular(b);
        try {
            File f = cacheFile(c, email, style);
            FileOutputStream out = new FileOutputStream(f);
            round.compress(Bitmap.CompressFormat.PNG, 100, out);
            out.close();
        } catch (Exception ignored) { }
        return round;
    }

    /** 把方形头像裁成圆形（透明背景的四角） */
    public static Bitmap circular(Bitmap src) {
        if (src == null) return null;
        int size = Math.min(src.getWidth(), src.getHeight());
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
        // 用着色器把原图画进圆里，边缘自带抗锯齿
        android.graphics.BitmapShader shader = new android.graphics.BitmapShader(
                src, android.graphics.Shader.TileMode.CLAMP, android.graphics.Shader.TileMode.CLAMP);
        android.graphics.Matrix m = new android.graphics.Matrix();
        float k = (float) size / Math.min(src.getWidth(), src.getHeight());
        m.postScale(k, k);
        shader.setLocalMatrix(m);
        p.setShader(shader);
        cv.drawCircle(size / 2f, size / 2f, size / 2f, p);
        if (src != out) src.recycle();
        return out;
    }'''
if old_save in s:
    s = s.replace(old_save, new_save, 1)
    print("save 已改为圆形遮罩")
else:
    print("!! save 未匹配")

# 2) 各处返回 save 的结果
s = s.replace('''        if (email == null || email.trim().length() == 0) {
            b = letterBitmap(email, 0);
            save(c, email, style, b);
            return b;
        }
        b = fetch(email, style);
        if (b == null) b = letterBitmap(email, 0);
        save(c, email, style, b);
        return b;''',
'''        if (email == null || email.trim().length() == 0) {
            return save(c, email, style, letterBitmap(email, 0));
        }
        b = fetch(email, style);
        if (b == null) b = letterBitmap(email, 0);
        return save(c, email, style, b);''')
s = s.replace('''        Bitmap b = square(src, 256);
        save(c, email, "custom", b);
        return b;''',
'''        return save(c, email, "custom", square(src, 256));''')
s = s.replace('''        b = square(b, 256);
        save(c, email, style, b);
        return b;''',
'''        return save(c, email, style, square(b, 256));''')
s = s.replace('''            Bitmap b = android.graphics.BitmapFactory.decodeFile(f.getAbsolutePath());
            if (b == null) return null;
            save(c, email, style, square(b, 256));
            return cached(c, email, style);''',
'''            Bitmap b = android.graphics.BitmapFactory.decodeFile(f.getAbsolutePath());
            if (b == null) return null;
            return save(c, email, style, square(b, 256));''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("各条路径已返回圆形头像")

# 3) 一次性清缓存，让已设置的头像立刻变圆
p2 = APP + r"\src\com\ankiassistant\MainActivity.java"
s2 = io.open(p2, encoding="utf-8").read()
if "avatarRoundV2" not in s2:
    s2 = s2.replace("        loadAvatarAsync();   // 布局建好后统一加载一次（手机没有侧栏，必须放在这里）",
'''        // 一次性清掉旧头像缓存：以前存的是方形，改成圆形后要重新生成
        if (!store.avatarRoundMigrated()) {
            store.setAvatarRoundMigrated(true);
            try {
                java.io.File dir = new java.io.File(getFilesDir(), "avatars");
                java.io.File[] fs = dir.listFiles();
                if (fs != null) for (java.io.File file : fs) file.delete();
            } catch (Exception ignored) { }
        }
        loadAvatarAsync();   // 布局建好后统一加载一次（手机没有侧栏，必须放在这里）''')
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("一次性清缓存已加")

# 4) Store：迁移标记
p3 = APP + r"\src\com\ankiassistant\Store.java"
s3 = io.open(p3, encoding="utf-8").read()
if "avatarRoundMigrated" not in s3:
    s3 = s3.replace("    /** 某个邮箱用的头像风格（见 Avatar.STYLES） */",
'''    /** 头像改成圆形的一次性迁移标记 */
    public boolean avatarRoundMigrated() { return sp.getBoolean("avatarRoundV2", false); }
    public void setAvatarRoundMigrated(boolean v) { sp.edit().putBoolean("avatarRoundV2", v).apply(); }

    /** 某个邮箱用的头像风格（见 Avatar.STYLES） */''')
    io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
    print("Store 迁移标记已加")
