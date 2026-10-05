"""设置页新增四个栏目：同步、制卡习惯、皮肤、关于。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

CARDS = '''        // ================= 同步 =================
        LinearLayout sync = card();
        sync.addView(heading("同步"));
        TextView syncTip = new TextView(getContext());
        syncTip.setText("本机收藏库与 AnkiWeb 云端的同步方式；手动同步随时可以在左上角头像里点。");
        syncTip.setTextColor(Ui.TEXT_DIM);
        syncTip.setTextSize(12.5f);
        sync.addView(syncTip);

        autoSyncBox = new CheckBox(getContext());
        autoSyncBox.setTextColor(Ui.TEXT_BODY);
        autoSyncBox.setTextSize(14);
        sync.addView(checkRow(autoSyncBox, "保存卡片后自动同步一次"));

        wifiOnlyBox = new CheckBox(getContext());
        wifiOnlyBox.setTextColor(Ui.TEXT_BODY);
        wifiOnlyBox.setTextSize(14);
        sync.addView(checkRow(wifiOnlyBox, "只在 Wi-Fi 下自动同步"));

        mediaBox = new CheckBox(getContext());
        mediaBox.setTextColor(Ui.TEXT_BODY);
        mediaBox.setTextSize(14);
        sync.addView(checkRow(mediaBox, "同步时包含媒体文件（图片 / 音频）"));

        syncInfo = status();
        sync.addView(syncInfo);
        Button syncNowBtn = new Button(getContext());
        syncNowBtn.setText("立即同步");
        Ui.secondary(syncNowBtn);
        syncNowBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (act instanceof MainActivity) ((MainActivity) act).syncNow();
            }
        });
        LinearLayout.LayoutParams snLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        snLp.topMargin = Ui.dp(8);
        sync.addView(syncNowBtn, snLp);
        LinearLayout.LayoutParams syncLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        syncLp.bottomMargin = Ui.dp(11);
        col.addView(sync, syncLp);
        addIndex("同步", sync);

        // ================= 制卡习惯 =================
        LinearLayout habit = card();
        habit.addView(heading("制卡习惯"));
        TextView habitTip = new TextView(getContext());
        habitTip.setText("连着做很多张时能省几次点击。");
        habitTip.setTextColor(Ui.TEXT_DIM);
        habitTip.setTextSize(12.5f);
        habit.addView(habitTip);

        clearBox = new CheckBox(getContext());
        clearBox.setTextColor(Ui.TEXT_BODY);
        clearBox.setTextSize(14);
        habit.addView(checkRow(clearBox, "保存成功后清空输入，方便接着做下一张"));

        previewBox = new CheckBox(getContext());
        previewBox.setTextColor(Ui.TEXT_BODY);
        previewBox.setTextSize(14);
        habit.addView(checkRow(previewBox, "AI 填充完成后自动切到预览"));

        LinearLayout.LayoutParams habitLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        habitLp.bottomMargin = Ui.dp(11);
        col.addView(habit, habitLp);
        addIndex("制卡习惯", habit);

        // ================= 皮肤 =================
        LinearLayout skin = card();
        skin.addView(heading("皮肤"));
        TextView skinTip = new TextView(getContext());
        skinTip.setText("换一套配色。深色皮肤会把制卡编辑区与浏览详情一起切成暗色。");
        skinTip.setTextColor(Ui.TEXT_DIM);
        skinTip.setTextSize(12.5f);
        skin.addView(skinTip);

        skinRow = new LinearLayout(getContext());
        skinRow.setOrientation(LinearLayout.HORIZONTAL);
        skinRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        skinRow.setPadding(0, Ui.dp(10), 0, 0);
        skin.addView(skinRow);

        LinearLayout.LayoutParams skinLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        skinLp.bottomMargin = Ui.dp(11);
        col.addView(skin, skinLp);
        addIndex("皮肤", skin);

        // ================= 关于 =================
        LinearLayout about = card();
        about.addView(heading("关于"));
        TextView aboutText = new TextView(getContext());
        aboutText.setText("Anki 助手 " + Version.VERSION_TAG + "\\n"
                + "卡片同步使用 Anki 官方后端（rslib / rsdroid，AGPL-3.0）。"
                + "收藏库保存在本机，不经过任何第三方服务器。");
        aboutText.setTextColor(Ui.TEXT_BODY);
        aboutText.setTextSize(13);
        aboutText.setLineSpacing(Ui.dp(3), 1f);
        about.addView(aboutText);

        Button repo = new Button(getContext());
        repo.setText("打开项目主页");
        Ui.secondary(repo);
        repo.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                try {
                    getContext().startActivity(new android.content.Intent(
                            android.content.Intent.ACTION_VIEW,
                            android.net.Uri.parse(Updater.REPO_PAGE)));
                } catch (Exception ignored) { }
            }
        });
        LinearLayout.LayoutParams repoLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        repoLp.topMargin = Ui.dp(10);
        about.addView(repo, repoLp);
        LinearLayout.LayoutParams aboutLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aboutLp.bottomMargin = Ui.dp(11);
        col.addView(about, aboutLp);
        addIndex("关于", about);

        loadValues();'''

s = s.replace("        loadValues();", CARDS, 1)

s = s.replace("    private ScrollView scroll;",
              "    private ScrollView scroll;\n"
              "    private CheckBox autoSyncBox, wifiOnlyBox, mediaBox, clearBox, previewBox;\n"
              "    private TextView syncInfo;\n"
              "    private LinearLayout skinRow;")

# loadValues
s = s.replace("        refreshProviderBtn();\n        refreshConfigCard();\n    }",
              "        if (autoSyncBox != null) autoSyncBox.setChecked(store.autoSyncAfterSave());\n"
              "        if (wifiOnlyBox != null) wifiOnlyBox.setChecked(store.syncWifiOnly());\n"
              "        if (mediaBox != null) mediaBox.setChecked(store.syncMedia());\n"
              "        if (clearBox != null) clearBox.setChecked(store.clearAfterSave());\n"
              "        if (previewBox != null) previewBox.setChecked(store.previewAfterFill());\n"
              "        if (syncInfo != null) syncInfo.setText(AnkiSync.describe(store));\n"
              "        refreshSkinRow();\n"
              "        refreshProviderBtn();\n        refreshConfigCard();\n    }", 1)

# save
s = s.replace("        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());",
              "        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());\n"
              "        if (autoSyncBox != null) store.setAutoSyncAfterSave(autoSyncBox.isChecked());\n"
              "        if (wifiOnlyBox != null) store.setSyncWifiOnly(wifiOnlyBox.isChecked());\n"
              "        if (mediaBox != null) store.setSyncMedia(mediaBox.isChecked());\n"
              "        if (clearBox != null) store.setClearAfterSave(clearBox.isChecked());\n"
              "        if (previewBox != null) store.setPreviewAfterFill(previewBox.isChecked());", 1)

METHODS = '''
    // ------------------------------------------------------------------ 皮肤

    /** 当前皮肤 + 切换按钮（每个皮肤前面一个色点） */
    private void refreshSkinRow() {
        if (skinRow == null) return;
        skinRow.removeAllViews();
        Theme cur = Theme.byId(store.theme());

        View dot = new View(getContext());
        dot.setBackground(Ui.round(cur.accent, 8));
        skinRow.addView(dot, new LinearLayout.LayoutParams(Ui.dp(14), Ui.dp(14)));

        TextView label = new TextView(getContext());
        label.setText(cur.name + (cur.dark ? "（深色）" : ""));
        label.setTextColor(Ui.INK);
        label.setTextSize(14.5f);
        label.setPadding(Ui.dp(9), 0, 0, 0);
        skinRow.addView(label, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        Button pick = new Button(getContext());
        pick.setText("选择皮肤");
        Ui.secondary(pick);
        pick.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { pickSkin(); }
        });
        skinRow.addView(pick);
    }

    private void pickSkin() {
        java.util.List<Theme> all = Theme.all();
        String[] names = new String[all.size()];
        int checked = 0;
        for (int i = 0; i < all.size(); i++) {
            names[i] = all.get(i).name + (all.get(i).dark ? "（深色）" : "");
            if (all.get(i).id.equals(store.theme())) checked = i;
        }
        new DialogUi.Builder(act)
                .title("选择皮肤")
                .message("换皮肤会重建界面；正在编辑的卡片内容不会丢。")
                .choices(names, checked, new DialogUi.Picker() {
                    @Override public void onPick(int which) {
                        store.setTheme(Theme.all().get(which).id);
                        Theme.apply(act, store);
                        if (act instanceof MainActivity) ((MainActivity) act).onThemeChanged();
                    }
                })
                .negative("取消", null)
                .show();
    }
'''
s = s.replace("    private TextView small(String text) {", METHODS + "\n    private TextView small(String text) {", 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("设置页四个栏目已加入")
