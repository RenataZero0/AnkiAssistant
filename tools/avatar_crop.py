"""头像选项精简 + 内置裁剪：
   · 选图后弹出裁剪框（拖动/缩放），确定才写入
   · 头像选项只剩两条：从相册选择 / 换回默认头像（Gravatar）
   · 图片体积上限 10MB，超了自动压缩（最终都压到 256px 的 PNG，通常 <50KB）
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) Avatar：只留两种风格 ----------------
p = APP + r"\src\com\ankiassistant\Avatar.java"
s = io.open(p, encoding="utf-8").read()
s = s.replace('''    public static final String[][] STYLES = {
            {"custom", "自己上传（相册选图）", "custom"},
            {"url", "从网址（可放 GitHub 仓库）", "url"},
            {"auto", "自动（先 Gravatar 后生成）", ""},
            {"initials", "字母", "initials"},
            {"identicon", "几何图形", "identicon"},
            {"shapes", "色块", "shapes"},
            {"bottts", "机器人", "bottts"},
            {"fun-emoji", "表情", "fun-emoji"},
            {"adventurer", "卡通脸", "adventurer"},
            {"letter", "本地字母（不联网）", "local"},
    };''',
'''    /** 界面上只给两种选择：自己传一张，或换回默认（Gravatar）。 */
    public static final String[][] STYLES = {
            {"custom", "从相册选择（可裁剪）", "custom"},
            {"auto", "换回默认头像（Gravatar）", ""},
    };''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("头像选项已精简为两条")

# ---------------- 2) MainActivity：选图后进裁剪 ----------------
p2 = APP + r"\src\com\ankiassistant\MainActivity.java"
s2 = io.open(p2, encoding="utf-8").read()

# 2a) onActivityResult：解码成功后弹裁剪框，而不是直接保存
old_save = '''                android.graphics.Bitmap src = decodeSampled(uri, 1024);
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
                        Avatar.pushToCloud(MainActivity.this, mail, saved);   // 随收藏库同步到别的设备
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this, "头像已更新",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                });'''
new_save = '''                final android.graphics.Bitmap src = decodeSampled(uri, 1600);
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (src == null) {
                            android.widget.Toast.makeText(MainActivity.this, "这张图读不出来，换一张试试",
                                    android.widget.Toast.LENGTH_LONG).show();
                            return;
                        }
                        showCropper(mail, src);
                    }
                });'''
if old_save in s2:
    s2 = s2.replace(old_save, new_save, 1)
    print("选图后改为进裁剪")
else:
    print("!! 选图流程未匹配")

# 2b) 换头像弹窗：两条选项 + 说明
s2 = s2.replace('''                .title("头像")
                .message("头像按邮箱生成，同一账号在不同设备上会得到同一个头像；"
                        + "家里没有外网时也可以用本地字母头像。")''',
'''                .title("头像")
                .message("默认用 Gravatar（按邮箱取，换设备也是同一张）；"
                        + "也可以从相册选一张，裁好后会写进收藏库跟着同步到别的设备。")''')

# 2c) 探针里的配置读取用 mediaName 反推，保持不变；新增裁剪弹窗方法
if "private void showCropper" not in s2:
    CROP = '''
    /** 裁剪弹窗：拖动图片、双指缩放，框内就是头像 */
    private void showCropper(final String mail, final android.graphics.Bitmap src) {
        final CropView crop = new CropView(this, src);
        int h = (int) (getResources().getDisplayMetrics().heightPixels * 0.46f);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        TextView tip = new TextView(this);
        tip.setText("拖动调整位置，双指缩放。方框内的部分会成为头像。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12.5f);
        tip.setPadding(0, 0, 0, Ui.dp(8));
        box.addView(tip);
        box.addView(crop, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, h));

        new DialogUi.Builder(this)
                .title("裁剪头像")
                .content(box)
                .negative("取消", null)
                .positive("就用这张", new Runnable() {
                    @Override public void run() {
                        try {
                            android.graphics.Bitmap cut = crop.cropped(256);
                            android.graphics.Bitmap saved = Avatar.saveCustom(MainActivity.this, mail, cut);
                            store.setAvatarStyle(mail, "custom");
                            if (saved != null) {
                                // 写进收藏库，随同步带到别的设备
                                Avatar.pushToCloud(MainActivity.this, mail, saved);
                            }
                            loadAvatarAsync();
                            android.widget.Toast.makeText(MainActivity.this, "头像已更新",
                                    android.widget.Toast.LENGTH_SHORT).show();
                        } catch (Throwable t) {
                            android.widget.Toast.makeText(MainActivity.this,
                                    "裁剪失败：" + t.getMessage(),
                                    android.widget.Toast.LENGTH_LONG).show();
                        }
                    }
                })
                .show();
    }
'''
    s2 = s2.replace("    /** 让用户填一个图片网址（放到 GitHub 仓库就能多设备共用） */", CROP + "\n    /** 让用户填一个图片网址（放到 GitHub 仓库就能多设备共用） */", 1)
    print("裁剪弹窗已加入")

# 2d) 图片体积上限：解码前先看文件大小，超过 10MB 直接拒绝（并提示会压缩）
s2 = s2.replace('''        final android.net.Uri uri = data.getData();''',
'''        final android.net.Uri uri = data.getData();
        long sizeBytes = -1;
        try {
            android.database.Cursor cur = getContentResolver().query(uri, null, null, null, null);
            if (cur != null) {
                int idx = cur.getColumnIndex(android.provider.OpenableColumns.SIZE);
                if (idx >= 0 && cur.moveToFirst()) sizeBytes = cur.getLong(idx);
                cur.close();
            }
        } catch (Exception ignored) { }
        // 上限 10MB；超了也不直接拒绝——反正会压缩到 256px，这里只是提示一下
        final boolean big = sizeBytes > 10L * 1024 * 1024;''')
s2 = s2.replace('''    /** 按显示需要解码（先量尺寸再采样，避免大图 OOM） */''',
'''    /** 按显示需要解码（先量尺寸再采样，避免大图 OOM；最终头像固定压到 256px，通常不到 50KB） */''')
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("MainActivity 已接入裁剪")
