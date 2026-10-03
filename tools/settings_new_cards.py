"""设置页新增四个栏目：同步、制卡习惯、数据与维护、关于。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

CARDS = '''
        // ================= 同步 =================
        LinearLayout sync = card();
        sync.addView(heading("同步"));
        TextView syncTip = new TextView(getContext());
        syncTip.setText("本机收藏库与 AnkiWeb 云端的同步方式。手动同步随时可以在左上角头像里点。");
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
        sync.addView(checkRow(wifiOnlyBox, "只在 Wi-Fi 下自动同步（省流量）"));

        mediaBox = new CheckBox(getContext());
        mediaBox.setTextColor(Ui.TEXT_BODY);
        mediaBox.setTextSize(14);
        sync.addView(checkRow(mediaBox, "同步时包含媒体文件（图片 / 音频）"));

        syncInfo = status();
        sync.addView(syncInfo);
        Button syncNow = new Button(getContext());
        syncNow.setText("立即同步");
        Ui.secondary(syncNow);
        syncNow.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (act instanceof MainActivity) ((MainActivity) act).syncNow();
            }
        });
        LinearLayout.LayoutParams syncLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        syncLp.topMargin = Ui.dp(8);
        sync.addView(syncNow, syncLp);
        LinearLayout.LayoutParams syncCardLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        syncCardLp.bottomMargin = Ui.dp(11);
        col.addView(sync, syncCardLp);
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

        // ================= 数据与维护 =================
        LinearLayout data = card();
        data.addView(heading("数据与维护"));
        dataTip = status();
        data.addView(dataTip);

        Button refreshData = new Button(getContext());
        refreshData.setText("刷新概况");
        Ui.secondary(refreshData);
        refreshData.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { refreshDataCard(); }
        });
        data.addView(refreshData);

        Button exportBtn = new Button(getContext());
        exportBtn.setText("导出收藏库备份");
        Ui.secondary(exportBtn);
        exportBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { exportCollection(); }
        });
        LinearLayout.LayoutParams expLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        expLp.topMargin = Ui.dp(8);
        data.addView(exportBtn, expLp);

        Button restoreBtn = new Button(getContext());
        restoreBtn.setText("从内置备份恢复");
        Ui.secondary(restoreBtn);
        restoreBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { confirmRestore(); }
        });
        LinearLayout.LayoutParams resLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        resLp.topMargin = Ui.dp(8);
        data.addView(restoreBtn, resLp);

        LinearLayout.LayoutParams dataLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        dataLp.bottomMargin = Ui.dp(11);
        col.addView(data, dataLp);
        addIndex("数据与维护", data);

        // ================= 关于 =================
        LinearLayout about = card();
        about.addView(heading("关于"));
        TextView aboutText = new TextView(getContext());
        aboutText.setText("Anki 助手 " + Version.VERSION_TAG + "\\n"
                + "卡片同步使用 Anki 官方后端（rslib / rsdroid，AGPL-3.0），"
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

# 字段声明
s = s.replace("    private ScrollView scroll;",
              "    private ScrollView scroll;\n"
              "    private CheckBox autoSyncBox, wifiOnlyBox, mediaBox, clearBox, previewBox;\n"
              "    private TextView syncInfo, dataTip;")

# 读写
s = s.replace("        refreshProviderBtn();\n        refreshConfigCard();\n    }",
              "        if (autoSyncBox != null) autoSyncBox.setChecked(store.autoSyncAfterSave());\n"
              "        if (wifiOnlyBox != null) wifiOnlyBox.setChecked(store.syncWifiOnly());\n"
              "        if (mediaBox != null) mediaBox.setChecked(store.syncMedia());\n"
              "        if (clearBox != null) clearBox.setChecked(store.clearAfterSave());\n"
              "        if (previewBox != null) previewBox.setChecked(store.previewAfterFill());\n"
              "        refreshProviderBtn();\n        refreshConfigCard();\n"
              "        if (syncInfo != null) syncInfo.setText(AnkiSync.describe(store));\n"
              "        refreshDataCard();\n    }", 1)

s = s.replace("        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());",
              "        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());\n"
              "        if (autoSyncBox != null) store.setAutoSyncAfterSave(autoSyncBox.isChecked());\n"
              "        if (wifiOnlyBox != null) store.setSyncWifiOnly(wifiOnlyBox.isChecked());\n"
              "        if (mediaBox != null) store.setSyncMedia(mediaBox.isChecked());\n"
              "        if (clearBox != null) store.setClearAfterSave(clearBox.isChecked());\n"
              "        if (previewBox != null) store.setPreviewAfterFill(previewBox.isChecked());", 1)

# 新方法
METHODS = '''
    // ------------------------------------------------------------------ 数据与维护

    /** 刷新收藏库概况（卡片数 / 牌组数 / 占用） */
    public void refreshDataCard() {
        if (dataTip == null) return;
        dataTip.setText("正在统计…");
        dataTip.setTextColor(Ui.TEXT_DIM);
        final android.content.Context ctx = getContext().getApplicationContext();
        Th.bg(new Runnable() {
            @Override public void run() {
                String txt;
                int color = Ui.TEXT_DIM;
                try {
                    int cards = AnkiBackend.searchTotal(ctx, store, "deck:*");
                    int decks = AnkiBackend.deckNames(ctx, store).length;
                    long mb = CollectionBackup.size(ctx) / 1024;
                    txt = "本机收藏库：" + cards + " 张卡片　·　" + decks + " 个牌组　·　"
                            + mb + " KB\\n内置备份："
                            + (CollectionBackup.hasBackup(ctx)
                                    ? (CollectionBackup.backupSize(ctx) / 1024 + " KB，可用于恢复") : "暂无");
                    color = Ui.TEXT_BODY;
                } catch (Exception e) {
                    txt = "读取失败：" + e.getMessage();
                    color = Ui.RED;
                }
                final String t = txt;
                final int c = color;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (dataTip != null) {
                            dataTip.setText(t);
                            dataTip.setTextColor(c);
                        }
                    }
                });
            }
        });
    }

    /** 导出收藏库到「下载」目录 */
    private void exportCollection() {
        dataTip.setText("正在导出…");
        dataTip.setTextColor(Ui.SUB);
        final android.content.Context ctx = getContext().getApplicationContext();
        Th.bg(new Runnable() {
            @Override public void run() {
                String msg;
                int color;
                try {
                    String where = CollectionBackup.exportToDownloads(ctx);
                    msg = "已导出到 " + where;
                    color = Ui.GREEN;
                } catch (Exception e) {
                    msg = "导出失败：" + e.getMessage();
                    color = Ui.RED;
                }
                final String t = msg;
                final int c = color;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (dataTip != null) {
                            dataTip.setText(t);
                            dataTip.setTextColor(c);
                        }
                    }
                });
            }
        });
    }

    /** 用内置备份恢复（会覆盖当前收藏库） */
    private void confirmRestore() {
        if (!CollectionBackup.hasBackup(getContext())) {
            dataTip.setText("没有内置备份可用（做转换或备份操作后才会生成）");
            dataTip.setTextColor(Ui.AMBER);
            return;
        }
        new DialogUi.Builder(act)
                .title("从内置备份恢复")
                .message("会用内置备份覆盖当前本机收藏库，恢复后本次新加的卡片会丢失。"
                        + "云端（AnkiWeb）不受影响，之后同步即可拉回。")
                .negative("取消", null)
                .positive("恢复", new Runnable() {
                    @Override public void run() {
                        dataTip.setText("正在恢复…");
                        dataTip.setTextColor(Ui.SUB);
                        final android.content.Context ctx = getContext().getApplicationContext();
                        Th.bg(new Runnable() {
                            @Override public void run() {
                                String msg;
                                int color;
                                try {
                                    EngineHolder.close();
                                    Thread.sleep(700);
                                    CollectionBackup.restoreInternal(ctx);
                                    msg = "已恢复，重新打开浏览即可看到备份内容";
                                    color = Ui.GREEN;
                                } catch (Exception e) {
                                    msg = "恢复失败：" + e.getMessage();
                                    color = Ui.RED;
                                }
                                final String t = msg;
                                final int c = color;
                                Th.ui(new Runnable() {
                                    @Override public void run() {
                                        if (dataTip != null) {
                                            dataTip.setText(t);
                                            dataTip.setTextColor(c);
                                        }
                                        refreshDataCard();
                                    }
                                });
                            }
                        });
                    }
                })
                .show();
    }
'''
s = s.replace("    // ------------------------------------------------------------------ 索引辅助", METHODS + "\n    // ------------------------------------------------------------------ 索引辅助", 1) \
    if "索引辅助" in s else s.replace("    private TextView small(String text) {", METHODS + "\n    private TextView small(String text) {", 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("设置页新栏目已加入")
