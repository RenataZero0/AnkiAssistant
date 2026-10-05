"""把头像接进界面：
   · Store 记住每个邮箱用的头像风格
   · 侧栏账号按钮显示头像（有图用图，没有就用字母底）
   · 账号弹窗顶部显示头像，并加「换头像」入口
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")


def patch(name, pairs):
    p = APP + "\\src\\com\\ankiassistant\\" + name
    s = io.open(p, encoding="utf-8").read()
    for old, new in pairs:
        if old in s:
            s = s.replace(old, new, 1)
        else:
            print("  !! %s 未匹配: %s" % (name, old.strip().split("\n")[0][:60]))
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("  已处理 " + name)


# ---------------- Store：头像风格 ----------------
patch("Store.java", [(
    "    // ------------------------------------------------------------ 外观 / 同步 / 制卡习惯",
    '''    // ------------------------------------------------------------ 头像

    /** 某个邮箱用的头像风格（见 Avatar.STYLES） */
    public String avatarStyle(String email) {
        return sp.getString("avatarStyle:" + (email == null ? "" : email.trim().toLowerCase()), "auto");
    }

    public void setAvatarStyle(String email, String style) {
        put("avatarStyle:" + (email == null ? "" : email.trim().toLowerCase()),
                style == null ? "auto" : style);
    }

    // ------------------------------------------------------------ 外观 / 同步 / 制卡习惯''')])

# ---------------- MainActivity：侧栏按钮显示头像 + 弹窗头像 + 换头像 ----------------
patch("MainActivity.java", [
    # 1) 侧栏账号按钮：把 "A" 换成 ImageView（默认还是字母底）
    ('''        TextView logo = new TextView(this);
        logo.setText("A");
        logo.setTextColor(Ui.WHITE);
        logo.setTextSize(17);
        logo.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        logo.setGravity(android.view.Gravity.CENTER);
        logo.setBackground(Ui.round(Ui.ACCENT, 11));
        accountBtn.addView(logo, new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36)));''',
     '''        accountAvatar = new android.widget.ImageView(this);
        accountAvatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
        accountAvatar.setBackground(Ui.round(Ui.ACCENT, 11));
        accountAvatar.setClipToOutline(true);
        android.graphics.drawable.GradientDrawable clip = Ui.round(Ui.ACCENT, 11);
        accountAvatar.setBackground(clip);
        accountBtn.addView(accountAvatar, new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36)));
        loadAvatarAsync();'''),
    # 2) 字段
    ("    private TextView accountLabel;",
     "    private TextView accountLabel;\n    private android.widget.ImageView accountAvatar;"),
    # 3) 账号弹窗：头像用真实图片 + 换头像入口
    ('''            TextView avatar = new TextView(this);
            avatar.setText("A");
            avatar.setTextColor(Ui.WHITE);
            avatar.setTextSize(20);
            avatar.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
            avatar.setGravity(android.view.Gravity.CENTER);
            avatar.setBackground(Ui.round(Ui.ACCENT, 12));
            card.addView(avatar, new LinearLayout.LayoutParams(Ui.dp(46), Ui.dp(46)));''',
     '''            final android.widget.ImageView avatar = new android.widget.ImageView(this);
            avatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
            avatar.setBackground(Ui.round(Ui.ACCENT, 12));
            avatar.setClipToOutline(true);
            Bitmap av = Avatar.cached(this, mail == null ? "" : mail, store.avatarStyle(mail));
            if (av != null) avatar.setImageBitmap(av);
            avatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAvatarPicker(); }
            });
            card.addView(avatar, new LinearLayout.LayoutParams(Ui.dp(46), Ui.dp(46)));'''),
    # 4) 弹窗按钮区加「换头像」
    ('''            new DialogUi.Builder(this)
                    .title("Anki 账号")
                    .content(box)
                    .negative("登出", new Runnable() {
                        @Override public void run() { confirmLogout(); }
                    })''',
     '''            new DialogUi.Builder(this)
                    .title("Anki 账号")
                    .content(box)
                    .neutral("换头像", new Runnable() {
                        @Override public void run() { showAvatarPicker(); }
                    })
                    .negative("登出", new Runnable() {
                        @Override public void run() { confirmLogout(); }
                    })'''),
])

# ---------------- 新方法：加载头像 + 选择风格 ----------------
s = io.open(APP + r"\src\com\ankiassistant\MainActivity.java", encoding="utf-8").read()
if "loadAvatarAsync" not in s.split("private void loadAvatarAsync")[0] or "private void loadAvatarAsync" not in s:
    METHODS = '''
    // ------------------------------------------------------------------ 头像

    /** 侧栏按钮上的头像：先显示缓存，再后台联网取一次 */
    private void loadAvatarAsync() {
        final String mail = store.ankiWebUser();
        final String style = store.avatarStyle(mail);
        Bitmap cached = Avatar.cached(this, mail, style);
        if (cached != null && accountAvatar != null) accountAvatar.setImageBitmap(cached);
        Th.bg(new Runnable() {
            @Override public void run() {
                final Bitmap b = Avatar.load(MainActivity.this, mail, style);
                if (b == null) return;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                    }
                });
            }
        });
    }

    /** 换头像：选一个风格（邮箱相同则各设备一致） */
    private void showAvatarPicker() {
        final String mail = store.ankiWebUser();
        if (mail.length() == 0) {
            android.widget.Toast.makeText(this, "登录后才能设置头像", android.widget.Toast.LENGTH_SHORT).show();
            return;
        }
        String[] names = new String[Avatar.STYLES.length];
        int[] colors = new int[Avatar.STYLES.length];
        int checked = 0;
        String cur = store.avatarStyle(mail);
        for (int i = 0; i < Avatar.STYLES.length; i++) {
            names[i] = Avatar.STYLES[i][1];
            colors[i] = Ui.ACCENT;
            if (Avatar.STYLES[i][0].equals(cur)) checked = i;
        }
        new DialogUi.Builder(this)
                .title("头像")
                .message("头像按邮箱生成，同一账号在不同设备上会得到同一个头像；"
                        + "家里没有外网时也可以用本地字母头像。")
                .choiceColors(colors)
                .choices(names, checked, new DialogUi.Picker() {
                    @Override public void onPick(int which) {
                        String style = Avatar.STYLES[which][0];
                        store.setAvatarStyle(mail, style);
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this,
                                "头像已切换为「" + Avatar.STYLES[which][1] + "」",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                })
                .neutral("重新获取", new Runnable() {
                    @Override public void run() {
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                    }
                })
                .negative("关闭", null)
                .show();
    }
'''
    s = s.replace("    /** 从同步端点里取出主机名，给账号卡做副标题用 */", METHODS + "\n    /** 从同步端点里取出主机名，给账号卡做副标题用 */", 1)
    io.open(APP + r"\src\com\ankiassistant\MainActivity.java", "w", encoding="utf-8", newline="\n").write(s)
    print("  头像方法已加入 MainActivity")
