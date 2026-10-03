"""头像支持自定义上传：
   · Avatar 增加 custom（本地相册选图）/ url（从网址取，便于用 GitHub 仓库当"头像数据库"）两种风格
   · 加方形裁剪与缩放
   · MainActivity 加选图（ACTION_OPEN_DOCUMENT + onActivityResult）与网址输入
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) Avatar：新风格 + 裁剪 ----------------
p = APP + r"\src\com\ankiassistant\Avatar.java"
s = io.open(p, encoding="utf-8").read()

s = s.replace('''    public static final String[][] STYLES = {
            {"auto", "自动（先 Gravatar 后生成）", ""},''',
'''    public static final String[][] STYLES = {
            {"custom", "自己上传（相册选图）", "custom"},
            {"url", "从网址（可放 GitHub 仓库）", "url"},
            {"auto", "自动（先 Gravatar 后生成）", ""},''')

# fetch 里先处理 custom / url
s = s.replace('''        // 1) 本地字母：不联网
        if ("local".equals(dicebear)) return null;''',
'''        // 0) 自定义：本地文件（上面缓存没命中就说明还没设置过）
        if ("custom".equals(dicebear) || "url".equals(dicebear)) return null;

        // 1) 本地字母：不联网
        if ("local".equals(dicebear)) return null;''')

# url 风格：由 MainActivity 传入 URL，这里提供下载+缓存的入口
s = s.replace('''    /** 本地生成的字母头像：主色圆 + 首字母（完全离线） */''',
'''    /** 从网址下载并保存为该邮箱的头像（用于"从网址"风格；GitHub raw 之类都能用） */
    public static Bitmap downloadInto(Context c, String email, String style, String url) {
        if (url == null || url.trim().length() == 0) return null;
        Bitmap b = http(url.trim());
        if (b == null) return null;
        b = square(b, 256);
        save(c, email, style, b);
        return b;
    }

    /** 直接保存一张用户选的图片（会先裁成正方形并缩到 256） */
    public static Bitmap saveCustom(Context c, String email, Bitmap src) {
        if (src == null) return null;
        Bitmap b = square(src, 256);
        save(c, email, "custom", b);
        return b;
    }

    /** 居中裁成正方形并缩放到指定边长 */
    public static Bitmap square(Bitmap src, int size) {
        if (src == null) return null;
        int w = src.getWidth(), h = src.getHeight();
        int side = Math.min(w, h);
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Paint p = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
        Rect srcR = new Rect((w - side) / 2, (h - side) / 2, (w - side) / 2 + side, (h - side) / 2 + side);
        Rect dstR = new Rect(0, 0, size, size);
        cv.drawBitmap(src, srcR, dstR, p);
        src.recycle();
        return out;
    }

    /** 本地生成的字母头像：主色圆 + 首字母（完全离线） */''')

# load 里对 custom / url 风格不要尝试联网生成
s = s.replace('''        if (email == null || email.trim().length() == 0) {''',
'''        if ("custom".equals(style) || "url".equals(style)) {
            // 这两种风格只能靠用户提供；没有缓存就直接用字母头像顶上
            b = letterBitmap(email, 0);
            return b;
        }
        if (email == null || email.trim().length() == 0) {''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("Avatar 已支持自定义上传与网址")

# ---------------- 2) Store：记住网址 ----------------
p2 = APP + r"\src\com\ankiassistant\Store.java"
s2 = io.open(p2, encoding="utf-8").read()
if "avatarUrl(" not in s2:
    s2 = s2.replace('''    // ------------------------------------------------------------ 外观 / 同步 / 制卡习惯''',
'''    /** 某个邮箱头像图片的网址（"从网址"风格用） */
    public String avatarUrl(String email) {
        return sp.getString("avatarUrl:" + (email == null ? "" : email.trim().toLowerCase()), "");
    }

    public void setAvatarUrl(String email, String url) {
        put("avatarUrl:" + (email == null ? "" : email.trim().toLowerCase()), url == null ? "" : url.trim());
    }

    // ------------------------------------------------------------ 外观 / 同步 / 制卡习惯''')
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("Store 已记住头像网址")

# ---------------- 3) MainActivity：选图 + 网址 ----------------
p3 = APP + r"\src\com\ankiassistant\MainActivity.java"
s3 = io.open(p3, encoding="utf-8").read()

# 3a) 选择器里 custom / url 走单独流程
s3 = s3.replace('''                    @Override public void onPick(int which) {
                        String style = Avatar.STYLES[which][0];
                        store.setAvatarStyle(mail, style);
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this,
                                "头像已切换为「" + Avatar.STYLES[which][1] + "」",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }''',
'''                    @Override public void onPick(int which) {
                        String style = Avatar.STYLES[which][0];
                        if ("custom".equals(style)) { pickAvatarImage(); return; }
                        if ("url".equals(style)) { askAvatarUrl(); return; }
                        store.setAvatarStyle(mail, style);
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this,
                                "头像已切换为「" + Avatar.STYLES[which][1] + "」",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }''')

# 3b) 新方法：选图 / 网址 / 处理返回
METHODS = '''
    private static final int REQ_AVATAR = 0x9A71;

    /** 从相册/文件里选一张图当头像 */
    private void pickAvatarImage() {
        try {
            android.content.Intent i = new android.content.Intent(
                    android.content.Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(android.content.Intent.CATEGORY_OPENABLE);
            i.setType("image/*");
            startActivityForResult(i, REQ_AVATAR);
        } catch (Exception e) {
            android.widget.Toast.makeText(this, "打不开图片选择器：" + e.getMessage(),
                    android.widget.Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onActivityResult(int req, int result, android.content.Intent data) {
        super.onActivityResult(req, result, data);
        if (req != REQ_AVATAR || result != RESULT_OK || data == null || data.getData() == null) return;
        final android.net.Uri uri = data.getData();
        final String mail = store.ankiWebUser();
        android.widget.Toast.makeText(this, "正在处理图片…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                android.graphics.Bitmap src = decodeSampled(uri, 1024);
                final android.graphics.Bitmap saved =
                        src == null ? null : Avatar.saveCustom(MainActivity.this, mail, src);
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (saved == null) {
                            android.widget.Toast.makeText(MainActivity.this, "这张图读不出来，换一张试试",
                                    android.widget.Toast.LENGTH_LONG).show();
                            return;
                        }
                        store.setAvatarStyle(mail, "custom");
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this, "头像已更新",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                });
            }
        });
    }

    /** 按显示需要解码（先量尺寸再采样，避免大图 OOM） */
    private android.graphics.Bitmap decodeSampled(android.net.Uri uri, int max) {
        try {
            android.graphics.BitmapFactory.Options o = new android.graphics.BitmapFactory.Options();
            o.inJustDecodeBounds = true;
            java.io.InputStream in1 = getContentResolver().openInputStream(uri);
            android.graphics.BitmapFactory.decodeStream(in1, null, o);
            if (in1 != null) in1.close();
            int sample = 1;
            while (o.outWidth / sample > max || o.outHeight / sample > max) sample *= 2;
            android.graphics.BitmapFactory.Options o2 = new android.graphics.BitmapFactory.Options();
            o2.inSampleSize = sample;
            java.io.InputStream in2 = getContentResolver().openInputStream(uri);
            android.graphics.Bitmap b = android.graphics.BitmapFactory.decodeStream(in2, null, o2);
            if (in2 != null) in2.close();
            return b;
        } catch (Throwable t) {
            return null;
        }
    }

    /** 让用户填一个图片网址（放到 GitHub 仓库就能多设备共用） */
    private void askAvatarUrl() {
        final String mail = store.ankiWebUser();
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        TextView tip = new TextView(this);
        tip.setText("填一个能直接打开图片的网址（GitHub raw 链接也行）。"
                + "下载后会缓存在本机，之后离线也能显示。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12.5f);
        tip.setLineSpacing(Ui.dp(3), 1f);
        box.addView(tip);
        final android.widget.EditText input = new android.widget.EditText(this);
        input.setSingleLine(true);
        input.setHint("https://raw.githubusercontent.com/…/avatar.png");
        input.setText(store.avatarUrl(mail));
        Ui.field(input);
        box.addView(DialogUi.label(this, "图片网址"));
        box.addView(DialogUi.inputWrap(this, input));

        new DialogUi.Builder(this)
                .title("从网址设置头像")
                .content(box)
                .negative("取消", null)
                .positive("下载", new Runnable() {
                    @Override public void run() {
                        final String url = input.getText().toString().trim();
                        if (url.length() == 0) return;
                        store.setAvatarUrl(mail, url);
                        store.setAvatarStyle(mail, "url");
                        Avatar.clear(MainActivity.this, mail);
                        android.widget.Toast.makeText(MainActivity.this, "正在下载…",
                                android.widget.Toast.LENGTH_SHORT).show();
                        Th.bg(new Runnable() {
                            @Override public void run() {
                                final android.graphics.Bitmap b =
                                        Avatar.downloadInto(MainActivity.this, mail, "url", url);
                                Th.ui(new Runnable() {
                                    @Override public void run() {
                                        if (b == null) {
                                            Toast.makeText(MainActivity.this,
                                                    "下载失败，检查网址或网络", Toast.LENGTH_LONG).show();
                                        } else {
                                            loadAvatarAsync();
                                            Toast.makeText(MainActivity.this, "头像已更新",
                                                    Toast.LENGTH_SHORT).show();
                                        }
                                    }
                                });
                            }
                        });
                    }
                })
                .show();
    }
'''
s3 = s3.replace("    /** 从同步端点里取出主机名，给账号卡做副标题用 */", METHODS + "\n    /** 从同步端点里取出主机名，给账号卡做副标题用 */", 1)

# toastUi 里用的是 android.widget.Toast，这里补上短名导入式的用法
s3 = s3.replace("                                            Toast.makeText(MainActivity.this,", "                                            android.widget.Toast.makeText(MainActivity.this,")
s3 = s3.replace("                                                    \"下载失败，检查网址或网络\", Toast.LENGTH_LONG).show();", "                                                    \"下载失败，检查网址或网络\", android.widget.Toast.LENGTH_LONG).show();")
s3 = s3.replace("                                            Toast.makeText(MainActivity.this, \"头像已更新\",\n                                                    Toast.LENGTH_SHORT).show();", "                                            android.widget.Toast.makeText(MainActivity.this, \"头像已更新\",\n                                                    android.widget.Toast.LENGTH_SHORT).show();")
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("MainActivity 已加上传/网址设置头像")
