package com.ankiassistant;

import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * 设置页：Anki 连接、AI 服务商、默认值、关于。
 */
public class SettingsView extends LinearLayout {

    private final MainActivity act;
    private final Store store;

    private EditText hostInput, portInput, ankiKeyInput;
    private EditText aiKeyInput, modelInput, urlInput;
    private EditText deckInput, tagInput, subjectInput;
    private CheckBox autoSyncBox;
    private Button providerBtn, testAnkiBtn, testAiBtn, saveBtn;
    private TextView ankiStatus, aiStatus, updateStatus;
    private ProgressBar aiBusy;
    private CheckBox thinkingCheck;
    private boolean checking;

    public SettingsView(MainActivity context) {
        super(context);
        act = context;
        store = context.store;
        setOrientation(VERTICAL);
        setPadding(0, 0, 0, 0);
        build();
    }

    // ------------------------------------------------------------------ 小部件

    private LinearLayout card() {
        LinearLayout l = new LinearLayout(getContext());
        l.setOrientation(LinearLayout.VERTICAL);
        Ui.card(l);
        l.setPadding(Ui.dp(14), Ui.dp(13), Ui.dp(14), Ui.dp(13));
        return l;
    }

    private TextView heading(String text) {
        TextView t = new TextView(getContext());
        t.setText(text);
        t.setTextColor(Ui.INK);
        t.setTextSize(16);
        t.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        return t;
    }

    private TextView label(String text) {
        TextView t = new TextView(getContext());
        t.setText(text);
        t.setTextColor(Ui.SUB);
        t.setTextSize(12);
        t.setPadding(0, Ui.dp(9), 0, Ui.dp(2));
        return t;
    }

    private EditText input(String hint, boolean password) {
        EditText e = new EditText(getContext());
        e.setSingleLine(true);
        e.setTextSize(14.5f);
        e.setTextColor(Ui.INK);
        Ui.hint(e, hint);
        if (password) e.setInputType(android.text.InputType.TYPE_CLASS_TEXT
                | android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);
        e.setBackground(Ui.roundStroke(Ui.WHITE, Ui.LINE, 9));
        e.setPadding(Ui.dp(10), Ui.dp(7), Ui.dp(10), Ui.dp(7));
        return e;
    }

    private TextView status() {
        TextView t = new TextView(getContext());
        t.setTextSize(12.5f);
        t.setTextColor(Ui.TEXT_DIM);
        t.setPadding(0, Ui.dp(7), 0, 0);
        return t;
    }

    private void addTo(LinearLayout card, String label, EditText input) {
        card.addView(label(label));
        card.addView(input, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
    }

    // ------------------------------------------------------------------ 构建

    private void build() {
        ScrollView scroll = new ScrollView(getContext());
        scroll.setVerticalScrollBarEnabled(false);
        addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout col = new LinearLayout(getContext());
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(14));
        scroll.addView(col, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // ================= Anki 连接 =================
        LinearLayout anki = card();
        anki.addView(heading("Anki 连接"));
        TextView ankiTip = new TextView(getContext());
        ankiTip.setText("填电脑的局域网 IP（电脑和本机连同一个 Wi-Fi）。Anki 要开着。");
        ankiTip.setTextColor(Ui.TEXT_DIM);
        ankiTip.setTextSize(12.5f);
        ankiTip.setPadding(0, Ui.dp(3), 0, 0);
        anki.addView(ankiTip);

        hostInput = input("例如 192.168.1.7", false);
        portInput = input("8765", false);
        ankiKeyInput = input("AnkiConnect 的 apiKey（没设就留空）", true);
        addTo(anki, "电脑的 IP 或主机名", hostInput);
        addTo(anki, "端口", portInput);
        addTo(anki, "API Key", ankiKeyInput);

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, Ui.dp(10), 0, 0);
        testAnkiBtn = new Button(getContext());
        testAnkiBtn.setText("测试连接");
        Ui.primary(testAnkiBtn);
        testAnkiBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { testAnki(); }
        });
        row.addView(testAnkiBtn, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        anki.addView(row);

        ankiStatus = status();
        anki.addView(ankiStatus);

        anki.addView(heading("怎么配置（电脑上只做一次）"));
        TextView guide = new TextView(getContext());
        guide.setText("1. 电脑打开 Anki → 工具 → 插件 → 获取插件，输入 2055492159（AnkiConnect）→ 重启 Anki\n\n"
                + "2. 插件列表里选中 AnkiConnect → 插件设置，把 webBindAddress 改成 \"0.0.0.0\"（允许局域网访问），"
                + "保存后再重启 Anki；想加密码就在配置里加 \"apiKey\": \"一串字符\"，并把同样的值填到上面\n\n"
                + "3. 重启后 Windows 会弹「安全中心：是否允许访问此应用」→ 必须点「允许」，"
                + "否则只有电脑自己能连，手机/平板连过去会超时\n\n"
                + "4. 电脑上 Win+R 输入 ipconfig 看 IPv4 地址，填到上面的「电脑的 IP 或主机名」\n\n"
                + "5. 点「测试连接」：第一次会在电脑上的 Anki 弹出允许提示，点允许即可\n\n"
                + "6. 保存卡片时会自动调用 sync()，推送到你的 AnkiWeb 云端账号\n\n"
                + "（没有 Wi-Fi 只有数据线时：adb reverse tcp:8765 tcp:8765，IP 填 127.0.0.1 也能用）");
        guide.setTextColor(Ui.TEXT_BODY);
        guide.setTextSize(13);
        guide.setLineSpacing(0, 1.15f);
        guide.setPadding(0, Ui.dp(6), 0, 0);
        anki.addView(guide);

        LinearLayout.LayoutParams ankiLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        ankiLp.bottomMargin = Ui.dp(11);
        col.addView(anki, ankiLp);

        // ================= AI =================
        LinearLayout ai = card();
        ai.addView(heading("AI 自动填充"));
        TextView aiTip = new TextView(getContext());
        aiTip.setText("选一个服务商，填好 Key（免密钥的不用填），点「测试」。");
        aiTip.setTextColor(Ui.TEXT_DIM);
        aiTip.setTextSize(12.5f);
        aiTip.setPadding(0, Ui.dp(3), 0, 0);
        ai.addView(aiTip);

        providerBtn = new Button(getContext());
        providerBtn.setText("选择服务商");
        Ui.secondary(providerBtn);
        providerBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { pickProvider(); }
        });
        ai.addView(label("服务商"));
        ai.addView(providerBtn);

        aiKeyInput = input("API Key（Bearer）", true);
        modelInput = input("模型名", false);
        urlInput = input("接口地址", false);
        addTo(ai, "API Key", aiKeyInput);
        addTo(ai, "模型", modelInput);
        addTo(ai, "接口地址（一般不用改）", urlInput);

        LinearLayout aiRow = new LinearLayout(getContext());
        aiRow.setOrientation(LinearLayout.HORIZONTAL);
        aiRow.setPadding(0, Ui.dp(10), 0, 0);
        testAiBtn = new Button(getContext());
        testAiBtn.setText("测试 AI");
        Ui.primary(testAiBtn);
        testAiBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { testAi(); }
        });
        aiRow.addView(testAiBtn, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        ai.addView(aiRow);

        // AI 状态行：带转圈，长耗时能看出程序在工作
        LinearLayout aiStatusRow = new LinearLayout(getContext());
        aiStatusRow.setOrientation(LinearLayout.HORIZONTAL);
        aiStatusRow.setGravity(Gravity.CENTER_VERTICAL);
        aiBusy = new ProgressBar(getContext());
        aiBusy.setIndeterminate(true);
        aiBusy.setVisibility(android.view.View.GONE);
        if (aiBusy.getIndeterminateDrawable() != null) {
            aiBusy.getIndeterminateDrawable().setColorFilter(
                    Ui.ACCENT, android.graphics.PorterDuff.Mode.SRC_IN);
        }
        LinearLayout.LayoutParams busyLp = new LinearLayout.LayoutParams(Ui.dp(18), Ui.dp(18));
        busyLp.topMargin = Ui.dp(7);
        aiStatusRow.addView(aiBusy, busyLp);

        aiStatus = status();
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        stLp.leftMargin = Ui.dp(8);
        aiStatusRow.addView(aiStatus, stLp);
        ai.addView(aiStatusRow);

        thinkingCheck = new CheckBox(getContext());
        thinkingCheck.setTextColor(Ui.TEXT_BODY);
        thinkingCheck.setTextSize(14);
        ai.addView(checkRow(thinkingCheck, "让思考型模型先思考（更慢；思考过程显示在制卡页）"));

        TextView thinkingTip = new TextView(getContext());
        thinkingTip.setText("默认关闭。实测 glm-4.5-flash：开思考要 23 秒、思考内容 1279 字，"
                + "还会把 JSON 输出挤到截断（需要重试）；关掉后 4.5 秒且一次成型。");
        thinkingTip.setTextColor(Ui.TEXT_DIM);
        thinkingTip.setTextSize(12);
        thinkingTip.setLineSpacing(0, 1.15f);
        ai.addView(thinkingTip);

        LinearLayout.LayoutParams aiLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aiLp.bottomMargin = Ui.dp(11);
        col.addView(ai, aiLp);

        // ================= 默认值 =================
        LinearLayout def = card();
        def.addView(heading("默认值"));
        deckInput = input("A Level Pure Mathematics", false);
        tagInput = input("ALevel::Maths", false);
        subjectInput = input("给 AI 的学科背景", false);
        addTo(def, "默认牌组", deckInput);
        addTo(def, "默认标签", tagInput);
        addTo(def, "学科背景（影响 AI 释义风格）", subjectInput);

        autoSyncBox = new CheckBox(getContext());
        autoSyncBox.setTextColor(Ui.TEXT_BODY);
        autoSyncBox.setTextSize(14);
        def.addView(checkRow(autoSyncBox, "保存后自动同步到 AnkiWeb 云端"));

        saveBtn = new Button(getContext());
        saveBtn.setText("保存设置");
        Ui.primary(saveBtn);
        saveBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { save(); }
        });
        LinearLayout.LayoutParams svLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        svLp.topMargin = Ui.dp(10);
        def.addView(saveBtn, svLp);

        LinearLayout.LayoutParams defLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        defLp.bottomMargin = Ui.dp(11);
        col.addView(def, defLp);

        // ================= 更新内容（照 StudyCompanion 的做法，日志打包在 APK 内） =================
        LinearLayout upd = card();
        upd.addView(heading("更新内容"));
        TextView ver = new TextView(getContext());
        ver.setText("当前版本 " + Version.VERSION_TAG
                + "　·　更新日志打包在应用内，离线可看");
        ver.setTextColor(Ui.TEXT_BODY);
        ver.setTextSize(13);
        ver.setLineSpacing(0, 1.2f);
        upd.addView(ver);

        if (Changelog.bundledNewer(getContext())) {
            TextView warn = new TextView(getContext());
            warn.setText("注意：打包进来的日志版本（v" + Changelog.latestVersion(
                    Changelog.text(getContext())) + "）和当前运行版本不一致，构建时忘了改 Version.java？");
            warn.setTextColor(Ui.AMBER);
            warn.setTextSize(12);
            warn.setLineSpacing(0, 1.15f);
            warn.setPadding(0, Ui.dp(6), 0, 0);
            upd.addView(warn);
        }

        Button logBtn = new Button(getContext());
        logBtn.setText("查看更新内容");
        Ui.primary(logBtn);
        logBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { ChangelogView.show(act); }
        });
        LinearLayout.LayoutParams logLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        logLp.topMargin = Ui.dp(9);
        logLp.rightMargin = Ui.dp(8);

        Button checkBtn = new Button(getContext());
        checkBtn.setText("检查更新");
        Ui.secondary(checkBtn);
        checkBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { checkUpdate(); }
        });
        LinearLayout.LayoutParams chkLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        chkLp.topMargin = Ui.dp(9);

        LinearLayout updRow = new LinearLayout(getContext());
        updRow.setOrientation(LinearLayout.HORIZONTAL);
        updRow.addView(logBtn, logLp);
        updRow.addView(checkBtn, chkLp);
        upd.addView(updRow);

        updateStatus = new TextView(getContext());
        updateStatus.setTextColor(Ui.TEXT_DIM);
        updateStatus.setTextSize(12.5f);
        updateStatus.setLineSpacing(0, 1.15f);
        updateStatus.setPadding(0, Ui.dp(7), 0, 0);
        updateStatus.setVisibility(View.GONE);
        upd.addView(updateStatus);

        LinearLayout.LayoutParams updLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        updLp.bottomMargin = Ui.dp(11);
        col.addView(upd, updLp);

        loadValues();
    }

    // ------------------------------------------------------------------ 读写

    private void loadValues() {
        hostInput.setText(store.ankiHost());
        portInput.setText(String.valueOf(store.ankiPort()));
        ankiKeyInput.setText(store.ankiApiKey());
        aiKeyInput.setText(store.aiApiKey());
        modelInput.setText(store.aiModel());
        urlInput.setText(store.aiBaseUrl());
        deckInput.setText(store.defaultDeck());
        tagInput.setText(store.defaultTags());
        subjectInput.setText(store.subject());
        autoSyncBox.setChecked(store.autoSync());
        if (thinkingCheck != null) thinkingCheck.setChecked(store.aiThinking());
        refreshProviderBtn();
    }

    public void onShown() {
        // 从别的页面回来时，输入框里可能已被改过，重新读一次（用户没保存就不覆盖已有输入）
        if (hostInput.getText().length() == 0) loadValues();
        refreshProviderBtn();
    }

    private void save() {
        store.setAnkiHost(hostInput.getText().toString());
        store.setAnkiPort(portInput.getText().toString().trim().length() == 0
                ? "8765" : portInput.getText().toString().trim());
        store.setAnkiApiKey(ankiKeyInput.getText().toString());
        store.setAiApiKey(aiKeyInput.getText().toString());
        store.setAiModel(modelInput.getText().toString());
        store.setAiBaseUrl(urlInput.getText().toString());
        store.setDefaultDeck(deckInput.getText().toString());
        store.setDefaultTags(tagInput.getText().toString());
        store.setSubject(subjectInput.getText().toString());
        store.setAutoSync(autoSyncBox.isChecked());
        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());
        ankiStatus.setText("设置已保存 ✓");
        ankiStatus.setTextColor(Ui.GREEN);
        // 连接信息可能改了，侧栏那盏状态灯跟着复测一次（手机端没有灯，内部会自己忽略）
        if (act != null) act.checkAnkiLamp();
    }

    private void refreshProviderBtn() {
        providerBtn.setText("当前：" + AiClient.presetLabel(store.aiProvider()) + "  ▾");
    }

    // ------------------------------------------------------------------ 动作

    /**
     * 勾选框 + 文字做成一行：方块与文字分开摆，方块顶部对齐第一行文字。
     * （直接用 CheckBox 的 setText 时，文字一旦折行，方块会被垂直居中到两行中间，
     * 看起来就像"勾选框和文字不在同一行"。）
     */
    private LinearLayout checkRow(final CheckBox box, String text) {
        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        // 整行居中对齐：方块和文字都在行内垂直居中，两者中心必然重合
        row.setGravity(android.view.Gravity.CENTER_VERTICAL);
        box.setText("");
        row.addView(box, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView label = new TextView(getContext());
        label.setText(text);
        label.setTextSize(14);
        label.setTextColor(Ui.TEXT_BODY);
        label.setGravity(android.view.Gravity.CENTER_VERTICAL);
        label.setPadding(Ui.dp(4), 0, 0, 0);
        label.setLineSpacing(0, 1.15f);
        row.addView(label, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        LinearLayout.LayoutParams rlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rlp.topMargin = Ui.dp(8);
        rlp.bottomMargin = Ui.dp(4);
        row.setLayoutParams(rlp);

        row.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { box.toggle(); }
        });
        return row;
    }

    // ------------------------------------------------------------------ 自动更新

    private void updateMsg(String text, int color) {
        if (updateStatus == null) return;
        updateStatus.setVisibility(View.VISIBLE);
        updateStatus.setText(text);
        updateStatus.setTextColor(color);
    }

    /** 查 GitHub 最新 Release，比对版本；有新版本就问用户要不要下载 */
    private void checkUpdate() {
        if (checking) return;
        checking = true;
        updateMsg("正在检查更新…", Ui.SUB);
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    final Updater.Release rel = Updater.latest();
                    final boolean newer = rel.isNewer();
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            checking = false;
                            if (!newer) {
                                updateMsg("已是最新版本 " + Version.VERSION_TAG
                                        + "（远端 " + rel.tag + "）✓", Ui.GREEN);
                                return;
                            }
                            updateMsg("发现新版本 " + rel.tag, Ui.ACCENT);
                            showUpdateDialog(rel);
                        }
                    });
                } catch (final Updater.UpException e) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            checking = false;
                            updateMsg("检查更新失败：" + e.getMessage(), Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void showUpdateDialog(final Updater.Release rel) {
        String notes = rel.notes == null ? "" : rel.notes.trim();
        if (notes.length() > 1200) notes = notes.substring(0, 1200) + "…";
        String msg = "当前版本：" + Version.VERSION_TAG + "\n最新版本：" + rel.tag
                + (rel.apkSize > 0 ? "（" + (rel.apkSize / 1024 / 1024) + " MB）" : "")
                + "\n\n" + notes;
        new AlertDialog.Builder(act)
                .setTitle("发现新版本")
                .setMessage(msg)
                .setPositiveButton("下载并安装", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) { downloadAndInstall(rel); }
                })
                .setNeutralButton("打开仓库", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        try {
                            Intent web = new Intent(Intent.ACTION_VIEW,
                                    android.net.Uri.parse(rel.pageUrl.length() > 0
                                            ? rel.pageUrl : Updater.REPO_PAGE));
                            act.startActivity(web);
                        } catch (Exception e) {
                            updateMsg("打不开浏览器：" + e.getMessage(), Ui.RED);
                        }
                    }
                })
                .setNegativeButton("稍后", null)
                .show();
    }

    private void downloadAndInstall(final Updater.Release rel) {
        updateMsg("正在下载 " + rel.tag + "…", Ui.SUB);
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    java.io.File apk = Updater.download(getContext(), rel, new Updater.Progress() {
                        @Override public void onProgress(final int percent) {
                            Th.ui(new Runnable() {
                                @Override public void run() { updateMsg("正在下载… " + percent + "%", Ui.SUB); }
                            });
                        }
                    });
                    final java.io.File out = apk;
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            updateMsg("下载完成，正在打开安装界面…", Ui.GREEN);
                            installApk(out);
                        }
                    });
                } catch (final Updater.UpException e) {
                    Th.ui(new Runnable() {
                        @Override public void run() { updateMsg("下载失败：" + e.getMessage(), Ui.RED); }
                    });
                }
            }
        });
    }

    /** Android 8 起安装 APK 需要单独授权，没授权就先把用户送到那个设置页 */
    private void installApk(java.io.File apk) {
        if (android.os.Build.VERSION.SDK_INT >= 26
                && !act.getPackageManager().canRequestPackageInstalls()) {
            updateMsg("请先允许本应用「安装未知应用」，然后重新点检查更新。", Ui.AMBER);
            try {
                act.startActivity(new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                        android.net.Uri.parse("package:" + act.getPackageName())));
            } catch (Exception ignored) { }
            return;
        }
        try {
            Intent i = new Intent(Intent.ACTION_VIEW);
            i.setDataAndType(ApkProvider.uriFor(apk.getName()),
                    "application/vnd.android.package-archive");
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            act.startActivity(i);
        } catch (Exception e) {
            updateMsg("安装失败，文件已下载到：" + apk.getAbsolutePath(), Ui.RED);
        }
    }

    private void pickProvider() {        final String[] ids = {AiClient.P_DEEPSEEK, AiClient.P_DOUBAO, AiClient.P_ZHIPU,
                AiClient.P_SILICON, AiClient.P_CUSTOM};
        String[] labels = new String[ids.length];
        for (int i = 0; i < ids.length; i++) {
            labels[i] = AiClient.presetLabel(ids[i]);
        }
        final String oldProvider = store.aiProvider();
        new AlertDialog.Builder(act)
                .setTitle("选择 AI 服务商")
                .setItems(labels, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        // 先把当前这把 Key 存回旧服务商名下，再切过去并带出新服务商自己的 Key
                        store.setAiApiKeyFor(oldProvider, aiKeyInput.getText().toString());
                        String id = ids[which];
                        store.setAiProvider(id);
                        if (!AiClient.P_CUSTOM.equals(id)) {
                            // 用预设地址/模型覆盖（用户仍可手动改，改完记得保存）
                            urlInput.setText(AiClient.presetBaseUrl(id));
                            modelInput.setText(AiClient.presetModel(id));
                            store.setAiBaseUrl(urlInput.getText().toString());
                            store.setAiModel(modelInput.getText().toString());
                        }
                        String kept = store.aiApiKeyFor(id);
                        aiKeyInput.setText(kept);
                        refreshProviderBtn();
                        aiStatus.setText(kept.length() > 0
                                ? "已切换到 " + AiClient.presetLabel(id) + "，Key 已自动带出，点「测试 AI」验证"
                                : "已切换到 " + AiClient.presetLabel(id) + "，还没有这个服务商的 Key，填好后点「测试 AI」");
                        aiStatus.setTextColor(Ui.AMBER);
                    }
                })
                .setNegativeButton("取消", null)
                .create().show();
    }

    private void testAnki() {
        save();
        ankiStatus.setText("正在连接（第一次可能要在电脑上点允许）…");
        ankiStatus.setTextColor(Ui.SUB);
        testAnkiBtn.setEnabled(false);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    AnkiClient anki = new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey());
                    anki.requestPermission();
                    final String ver = anki.versionString();
                    final JSONArray decks = anki.deckNames();
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            testAnkiBtn.setEnabled(true);
                            ankiStatus.setText("连接成功 ✓ AnkiConnect v" + ver
                                    + "，共 " + decks.length() + " 个牌组");
                            ankiStatus.setTextColor(Ui.GREEN);
                        }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            testAnkiBtn.setEnabled(true);
                            ankiStatus.setText("连接失败：" + e.getMessage());
                            ankiStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void testAi() {
        save();
        aiStatus.setText("正在调用 AI…（免费档约 30～60 秒）");
        aiStatus.setTextColor(Ui.SUB);
        testAiBtn.setEnabled(false);
        if (aiBusy != null) aiBusy.setVisibility(android.view.View.VISIBLE);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    AiClient ai = new AiClient();
                    String r = ai.chat(store.aiBaseUrlEffective(), store.aiApiKey(),
                            store.aiModelEffective(), CardFormat.SYSTEM, "只回复两个字：可用");
                    final String out = r.trim();
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            testAiBtn.setEnabled(true);
                            if (aiBusy != null) aiBusy.setVisibility(android.view.View.GONE);
                            aiStatus.setText("AI 调用成功 ✓ 模型回复：" + out);
                            aiStatus.setTextColor(Ui.GREEN);
                        }
                    });
                } catch (final AiClient.AiException e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            testAiBtn.setEnabled(true);
                            if (aiBusy != null) aiBusy.setVisibility(android.view.View.GONE);
                            aiStatus.setText("AI 调用失败：" + e.getMessage());
                            aiStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

}
