"""1) 裁剪矩阵修正：输出 = ((原图*scale + d) - 裁剪框左上角) * k
   2) 手机顶栏的「A」也换成头像（之前只有侧栏与弹窗换成了图片）
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) 裁剪矩阵 ----------------
p = APP + r"\src\com\ankiassistant\CropView.java"
s = io.open(p, encoding="utf-8").read()
old = '''        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Matrix m = new Matrix();
        float k = (float) size / side;
        m.postScale(k, k);
        m.postTranslate(-left * k, -top * k);
        m.postScale(scale, scale);
        m.postTranslate(dx, dy);'''
new = '''        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Matrix m = new Matrix();
        float k = (float) size / side;
        // 顺序很重要（post* 是按调用先后依次作用到点上）：
        //   原图 → 缩放 → 平移到视图坐标 → 挪到裁剪框原点 → 放大到输出尺寸
        m.postScale(scale, scale);
        m.postTranslate(dx, dy);
        m.postTranslate(-left, -top);
        m.postScale(k, k);'''
if old in s:
    s = s.replace(old, new, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("裁剪矩阵已修正")
else:
    print("!! 裁剪矩阵未匹配")

# ---------------- 2) 手机顶栏头像 ----------------
p2 = APP + r"\src\com\ankiassistant\MainActivity.java"
s2 = io.open(p2, encoding="utf-8").read()
old2 = '''        if (isPhone()) {
            TextView avatar = new TextView(this);
            avatar.setText("A");
            avatar.setTextColor(Ui.WHITE);
            avatar.setTextSize(13);
            avatar.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
            avatar.setGravity(android.view.Gravity.CENTER);
            avatar.setBackground(Ui.round(Ui.ACCENT, 9));
            avatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAccountDialog(); }
            });'''
new2 = '''        if (isPhone()) {
            // 顶栏也用真实头像（和侧栏、账号弹窗一致），没有头像时才显示字母底
            topAvatar = new android.widget.ImageView(this);
            topAvatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
            topAvatar.setBackground(Ui.round(Ui.ACCENT, 9));
            topAvatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAccountDialog(); }
            });'''
if old2 in s2:
    s2 = s2.replace(old2, new2, 1)
    s2 = s2.replace('''            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(26), Ui.dp(26));
            avlp.rightMargin = Ui.dp(10);
            avlp.gravity = android.view.Gravity.CENTER_VERTICAL;
            topBar.addView(avatar, avlp);''',
'''            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(26), Ui.dp(26));
            avlp.rightMargin = Ui.dp(10);
            avlp.gravity = android.view.Gravity.CENTER_VERTICAL;
            topBar.addView(topAvatar, avlp);''')
    s2 = s2.replace("    private android.widget.ImageView accountAvatar;",
                    "    private android.widget.ImageView accountAvatar;\n    private android.widget.ImageView topAvatar;")
    # 加载头像时两个都刷新
    s2 = s2.replace('''                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                    }''',
'''                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                        if (topAvatar != null) topAvatar.setImageBitmap(b);
                    }''')
    s2 = s2.replace('''        Bitmap cached = Avatar.cached(this, mail, style);''',
                    '''        android.graphics.Bitmap cached = Avatar.cached(this, mail, style);''')
    s2 = s2.replace('''        if (cached != null && accountAvatar != null) accountAvatar.setImageBitmap(cached);''',
'''        if (cached != null) {
            if (accountAvatar != null) accountAvatar.setImageBitmap(cached);
            if (topAvatar != null) topAvatar.setImageBitmap(cached);
        }''')
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("手机顶栏头像已换成图片")
else:
    print("!! 顶栏头像未匹配")
