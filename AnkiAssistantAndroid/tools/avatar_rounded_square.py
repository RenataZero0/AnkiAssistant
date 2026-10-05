"""头像从圆形改成圆角方形（全平台一致）。
   · 遮罩换成圆角矩形（半径 = 边长 30%）
   · 本地生成的字母头像同样改成圆角方形
   · 清一次缓存：Gravatar/生成的头像会自动变成圆角方形；
     相册上传的那张需要重新选一次图（云端存的已经是圆的，改不回来）
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\Avatar.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 遮罩：圆 → 圆角方形
old = '''    /** 把方形头像裁成圆形（透明背景的四角） */
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
new = '''    /** 圆角半径占边长的比例（各平台统一用这一个） */
    private static final float CORNER = 0.30f;

    /** 把方形头像裁成圆角方形（四角透明） */
    public static Bitmap rounded(Bitmap src) {
        if (src == null) return null;
        int size = Math.min(src.getWidth(), src.getHeight());
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
        android.graphics.BitmapShader shader = new android.graphics.BitmapShader(
                src, android.graphics.Shader.TileMode.CLAMP, android.graphics.Shader.TileMode.CLAMP);
        android.graphics.Matrix m = new android.graphics.Matrix();
        float k = (float) size / Math.min(src.getWidth(), src.getHeight());
        m.postScale(k, k);
        shader.setLocalMatrix(m);
        p.setShader(shader);
        float r = size * CORNER;
        cv.drawRoundRect(new android.graphics.RectF(0, 0, size, size), r, r, p);
        if (src != out) src.recycle();
        return out;
    }

    /** 兼容旧调用 */
    public static Bitmap circular(Bitmap src) { return rounded(src); }'''
if old in s:
    s = s.replace(old, new, 1)
    print("遮罩已改为圆角方形")
else:
    print("!! 遮罩未匹配")

s = s.replace("Bitmap round = circular(b);", "Bitmap round = rounded(b);")

# 2) 字母头像：圆 → 圆角方形
old2 = '''        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        p.setColor(Ui.ACCENT);
        cv.drawCircle(s / 2f, s / 2f, s / 2f, p);'''
new2 = '''        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        p.setColor(Ui.ACCENT);
        float r = s * CORNER;
        cv.drawRoundRect(new android.graphics.RectF(0, 0, s, s), r, r, p);'''
if old2 in s:
    s = s.replace(old2, new2, 1)
    print("字母头像已改为圆角方形")

io.open(p, "w", encoding="utf-8", newline="\n").write(s)

# 3) 换新的一次性清缓存标记（旧缓存是圆的）
p2 = APP + r"\src\com\ankiassistant\Store.java"
s2 = io.open(p2, encoding="utf-8").read()
s2 = s2.replace('public boolean avatarRoundMigrated() { return sp.getBoolean("avatarRoundV2", false); }',
                'public boolean avatarRoundMigrated() { return sp.getBoolean("avatarRoundedV3", false); }')
s2 = s2.replace('public void setAvatarRoundMigrated(boolean v) { sp.edit().putBoolean("avatarRoundV2", v).apply(); }',
                'public void setAvatarRoundMigrated(boolean v) { sp.edit().putBoolean("avatarRoundedV3", v).apply(); }')
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("缓存迁移标记已更新")
