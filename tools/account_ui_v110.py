"""MainActivity：
   · 左上角「A」头像 = Anki 账号按钮（登录/注册/同步/退出）
   · 手机布局（没有侧栏）时，顶栏也放一个同样的头像按钮
   · 左下角指示灯改成「同步状态」：未登录=灰、已登录已同步=绿；点一下=同步（未登录先弹登录）
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()

# ---------------- 1) 侧栏头像改成账号按钮 ----------------
old_logo = '''        // ---- 顶部：应用标识（填掉上方那块空白） ----
        TextView logo = new TextView(this);
        logo.setText("A");
        logo.setTextColor(Ui.WHITE);
        logo.setTextSize(17);
        logo.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        logo.setGravity(android.view.Gravity.CENTER);
        logo.setBackground(Ui.round(Ui.ACCENT, 11));
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36));
        llp.gravity = android.view.Gravity.CENTER_HORIZONTAL;
        llp.bottomMargin = Ui.dp(4);
        r.addView(logo, llp);
'''
new_logo = '''        // ---- 顶部：Anki 账号按钮（图标是应用标识；点开登录/注册/同步） ----
        LinearLayout accountBtn = new LinearLayout(this);
        accountBtn.setOrientation(LinearLayout.VERTICAL);
        accountBtn.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
        accountBtn.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        accountBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { showAccountDialog(); }
        });
        TextView logo = new TextView(this);
        logo.setText("A");
        logo.setTextColor(Ui.WHITE);
        logo.setTextSize(17);
        logo.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        logo.setGravity(android.view.Gravity.CENTER);
        logo.setBackground(Ui.round(Ui.ACCENT, 11));
        accountBtn.addView(logo, new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36)));
        accountLabel = new TextView(this);
        accountLabel.setText("登录");
        accountLabel.setTextColor(Ui.SUB);
        accountLabel.setTextSize(10.5f);
        accountLabel.setGravity(android.view.Gravity.CENTER);
        LinearLayout.LayoutParams alp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        alp.topMargin = Ui.dp(4);
        accountBtn.addView(accountLabel, alp);
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        llp.gravity = android.view.Gravity.CENTER_HORIZONTAL;
        llp.bottomMargin = Ui.dp(4);
        r.addView(accountBtn, llp);
'''
if old_logo in src:
    src = src.replace(old_logo, new_logo)
    print("  侧栏头像已改成账号按钮")
else:
    print("  !! 侧栏头像块未匹配")

# ---------------- 2) 指示灯：改成同步状态，点击=同步 ----------------
old_lamp_click = '''        lamp.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { checkAnkiLamp(); }
        });'''
new_lamp_click = '''        lamp.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { onLampClick(); }
        });'''
if old_lamp_click in src:
    src = src.replace(old_lamp_click, new_lamp_click)
    print("  指示灯点击行为已改")

# ---------------- 3) 顶栏给手机布局加账号按钮 ----------------
old_ver = '''        TextView ver = new TextView(this);
        ver.setText(Version.VERSION_TAG);'''
new_ver = '''        // 手机布局没有侧栏，顶栏也给一个账号入口
        if (isPhone()) {
            TextView avatar = new TextView(this);
            avatar.setText("A");
            avatar.setTextColor(Ui.WHITE);
            avatar.setTextSize(13);
            avatar.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
            avatar.setGravity(android.view.Gravity.CENTER);
            avatar.setBackground(Ui.round(Ui.ACCENT, 9));
            avatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAccountDialog(); }
            });
            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(28), Ui.dp(28));
            avlp.rightMargin = Ui.dp(10);
            avlp.gravity = android.view.Gravity.CENTER_VERTICAL;
            topBar.addView(avatar, avlp);
        }

        TextView ver = new TextView(this);
        ver.setText(Version.VERSION_TAG);'''
if old_ver in src and "if (isPhone()) {" not in src:
    src = src.replace(old_ver, new_ver, 1)
    print("  顶栏已加账号按钮（手机布局）")

# ---------------- 4) 字段 ----------------
if "private TextView accountLabel;" not in src:
    src = src.replace("    private View ankiDot;",
                      "    private TextView accountLabel;\n    private View ankiDot;")

# ---------------- 5) 新方法 ----------------
METHODS = '''
    // ------------------------------------------------------------------ Anki 账号（登录 / 注册 / 同步）

    private boolean isPhone() {
        return getResources().getConfiguration().smallestScreenWidthDp < 600;
    }

    /** 左上角头像：Anki 账号弹窗（登录 / 注册 / 同步 / 退出） */
    public void showAccountDialog() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        int pad = Ui.dp(18);
        box.setPadding(pad, Ui.dp(6), pad, Ui.dp(2));

        TextView st = new TextView(this);
        st.setText(AnkiSync.describe(store));
        st.setTextColor(Ui.SUB);
        st.setTextSize(12.5f);
        st.setLineSpacing(0, 1.15f);
        box.addView(st);

        TextView tip = new TextView(this);
        tip.setText("登录后卡片会同步到 AnkiWeb。密码只用于登录，不会保存在设备上。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12);
        tip.setPadding(0, Ui.dp(6), 0, Ui.dp(2));
        box.addView(tip);

        final EditText user = new EditText(this);
        user.setHint("AnkiWeb 邮箱");
        user.setText(store.ankiWebUser());
        box.addView(user);

        final EditText pass = new EditText(this);
        pass.setHint("密码");
        pass.setInputType(android.text.InputType.TYPE_CLASS_TEXT
                | android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);
        box.addView(pass);

        final AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle("Anki 账号")
                .setView(box)
                .setPositiveButton("登录并同步", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        syncWithAccount(user.getText().toString().trim(),
                                pass.getText().toString());
                    }
                })
                .setNeutralButton("注册", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        openAnkiWebRegister();
                    }
                })
                .setNegativeButton("关闭", null)
                .create();
        dlg.show();
    }

    /** 用 AnkiWeb 官网的注册页（后端 API 不提供注册） */
    private void openAnkiWebRegister() {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW,
                    android.net.Uri.parse("https://ankiweb.net/account/register")));
        } catch (Exception e) {
            android.widget.Toast.makeText(this, "打不开浏览器：" + e.getMessage(),
                    android.widget.Toast.LENGTH_LONG).show();
        }
    }

    /** 已有 hkey 时点灯同步；没有就弹登录 */
    private void onLampClick() {
        if (store.ankiWebHkey().length() == 0) {
            showAccountDialog();
        } else {
            syncWithAccount(null, null);
        }
    }

    private void syncWithAccount(final String user, final String pass) {
        android.widget.Toast.makeText(this, "正在同步…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    updateSyncLamp();
                } catch (final AnkiSync.FullSyncRequired f) {
                    Th.ui(new Runnable() {
                        @Override public void run() { askFullSync(user, pass, f.reason); }
                    });
                } catch (final Exception e) {
                    toastUi("同步失败：" + e.getMessage());
                    updateSyncLamp();
                }
            }
        });
    }

    /** 需要全量同步时让用户选方向 */
    private void askFullSync(final String user, final String pass, String reason) {
        new AlertDialog.Builder(this)
                .setTitle("需要全量同步")
                .setMessage(reason + "\\n\\n上传：用本机的卡片覆盖云端\\n下载：用云端覆盖本机"
                        + "\\n\\n（本机是刚装的、云端才有你的卡片时，选「下载云端」）")
                .setPositiveButton("上传本机", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        runFullSync(user, pass, Boolean.TRUE);
                    }
                })
                .setNeutralButton("下载云端", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        runFullSync(user, pass, Boolean.FALSE);
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private void runFullSync(final String user, final String pass, final Boolean upload) {
        android.widget.Toast.makeText(this, upload.booleanValue() ? "正在上传本机收藏库…"
                : "正在下载云端收藏库…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);
                } catch (final Exception e) {
                    toastUi("全量同步失败：" + e.getMessage());
                }
                Th.ui(new Runnable() {
                    @Override public void run() { updateSyncLamp(); }
                });
            }
        });
    }

    private void toastUi(final String msg) {
        Th.ui(new Runnable() {
            @Override public void run() {
                android.widget.Toast.makeText(MainActivity.this, msg,
                        android.widget.Toast.LENGTH_LONG).show();
            }
        });
    }

    /** 左下角指示灯：反映 AnkiWeb 登录/同步状态（顺带更新头像下面的小字） */
    public void updateSyncLamp() {
        boolean loggedIn = store.ankiWebHkey().length() > 0;
        long last = store.lastSyncAt();
        if (accountLabel != null) {
            accountLabel.setText(loggedIn ? "已登录" : "登录");
        }
        if (ankiDot == null) return;
        if (!loggedIn) {
            setSyncLamp(0xFFB9C2D0, "未登录");
        } else if (last <= 0) {
            setSyncLamp(0xFFF5A623, "待同步");
        } else {
            long min = (System.currentTimeMillis() - last) / 60000L;
            String when = min < 1 ? "刚刚" : (min < 60 ? (min + " 分钟前")
                    : (min / 60 + " 小时前"));
            setSyncLamp(0xFF22A06B, when);
        }
    }

    private void setSyncLamp(int color, String label) {
        if (ankiDot != null) ankiDot.setBackground(Ui.round(color, 5));
        if (ankiLampText != null) {
            ankiLampText.setText(label);
            ankiLampText.setTextColor(color == 0xFF22A06B ? color : Ui.TEXT_DIM);
        }
    }

'''
anchor = "    private void setAnkiLamp(int color, String label) {"
if "showAccountDialog()" in src and "private void syncWithAccount" not in src:
    src = src.replace(anchor, METHODS + anchor, 1)
    print("  账号/同步方法已插入")

# 启动时刷新指示灯
src = src.replace("        if (rail != null) checkAnkiLamp();",
                  "        if (rail != null) updateSyncLamp();")

io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("MainActivity 处理完成")
