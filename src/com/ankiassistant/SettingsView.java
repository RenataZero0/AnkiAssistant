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

    private EditText aiKeyInput, modelInput, urlInput;
    private Button providerBtn, testAiBtn, saveBtn;
    private TextView aiStatus, updateStatus;
    private ProgressBar aiBusy;
    private CheckBox thinkingCheck;
    private TextView configStatus;
    private Button configPickBtn, configNewBtn, configEditBtn, configDelBtn;
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

    private ScrollView scroll;
    private CheckBox autoSyncBox, wifiOnlyBox, mediaBox, clearBox, previewBox;
    private TextView syncInfo;
    private LinearLayout skinRow;
    private LinearLayout indexBox;
    private android.widget.HorizontalScrollView indexScroller;

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
        e.setBackground(Ui.roundStroke(Ui.PANEL, Ui.LINE, 9));
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
        boolean wide = getResources().getConfiguration().smallestScreenWidthDp >= 600;

        // 外层：宽屏时左右分栏（左索引 / 右内容），窄屏时上下（上标签 / 下内容）
        LinearLayout outer = new LinearLayout(getContext());
        outer.setOrientation(wide ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        addView(outer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        indexBox = new LinearLayout(getContext());
        indexBox.setOrientation(wide ? LinearLayout.VERTICAL : LinearLayout.HORIZONTAL);
        indexBox.setBackgroundColor(Ui.CARD);
        if (wide) {
            indexBox.setPadding(Ui.dp(6), Ui.dp(14), Ui.dp(6), Ui.dp(10));
            outer.addView(indexBox, new LinearLayout.LayoutParams(Ui.dp(126),
                    ViewGroup.LayoutParams.MATCH_PARENT));
        } else {
            indexBox.setPadding(Ui.dp(6), Ui.dp(6), Ui.dp(6), Ui.dp(6));
            android.widget.HorizontalScrollView ix = new android.widget.HorizontalScrollView(getContext());
            ix.setHorizontalScrollBarEnabled(false);
            indexScroller = ix;
            ix.addView(indexBox, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            outer.addView(ix, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        }

        scroll = new ScrollView(getContext());
        scroll.setVerticalScrollBarEnabled(false);
        outer.addView(scroll, new LinearLayout.LayoutParams(
                wide ? 0 : ViewGroup.LayoutParams.MATCH_PARENT,
                wide ? ViewGroup.LayoutParams.MATCH_PARENT : 0,
                wide ? 1f : 1f));

        LinearLayout col = new LinearLayout(getContext());
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(14));
        scroll.addView(col, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

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
        providerBtn.setSingleLine(true);
        providerBtn.setEllipsize(android.text.TextUtils.TruncateAt.END);
        Ui.secondary(providerBtn);
        providerBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { pickProvider(); }
        });
        ai.addView(label("服务商"));
        ai.addView(providerBtn);

        aiKeyInput = input("留空则用内置 Key", true);
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
        Ui.check(thinkingCheck);
        thinkingCheck.setTextColor(Ui.TEXT_BODY);
        thinkingCheck.setTextSize(14);
        ai.addView(checkRow(thinkingCheck, "让思考型模型先思考"));

        TextView thinkingTip = new TextView(getContext());
        thinkingTip.setText("默认关闭。开启后更慢，且长推理容易把 JSON 输出挤断（需要重试）。");
        thinkingTip.setTextColor(Ui.TEXT_DIM);
        thinkingTip.setTextSize(12);
        thinkingTip.setLineSpacing(0, 1.15f);
        ai.addView(thinkingTip);

        LinearLayout.LayoutParams aiLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aiLp.bottomMargin = Ui.dp(11);
        col.addView(ai, aiLp);
        addIndex("AI 自动填充", ai);


        // ================= 输出格式 config =================
        LinearLayout cfgCard = card();
        cfgCard.addView(heading("输出格式"));
        TextView cfgTip = new TextView(getContext());
        cfgTip.setText("控制 AI 的输出格式与卡片字段，并连带决定该格式使用的默认牌组、标签与学科背景。"
                + "内置一套通用英语词汇格式；可按学科或考试另建格式，随时切换。");
        cfgTip.setTextColor(Ui.TEXT_DIM);
        cfgTip.setTextSize(12.5f);
        cfgTip.setLineSpacing(0, 1.15f);
        cfgCard.addView(cfgTip);

        configStatus = status();
        cfgCard.addView(configStatus);

        configPickBtn = new Button(getContext());
        configPickBtn.setText("切换格式");
        Ui.primary(configPickBtn);
        configPickBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { pickConfig(); }
        });
        LinearLayout.LayoutParams cfgLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cfgLp.topMargin = Ui.dp(8);
        cfgCard.addView(configPickBtn, cfgLp);

        LinearLayout cfgRow = new LinearLayout(getContext());
        cfgRow.setOrientation(LinearLayout.HORIZONTAL);
        cfgRow.setPadding(0, Ui.dp(8), 0, 0);
        configNewBtn = new Button(getContext());
        configNewBtn.setText("新建");
        Ui.secondary(configNewBtn);
        configNewBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { editConfig(null); }
        });
        configEditBtn = new Button(getContext());
        configEditBtn.setText("编辑");
        Ui.secondary(configEditBtn);
        configEditBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { editConfig(store.activeConfig()); }
        });
        configDelBtn = new Button(getContext());
        configDelBtn.setText("删除");
        Ui.secondary(configDelBtn);
        configDelBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { deleteActiveConfig(); }
        });
        LinearLayout.LayoutParams third = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        third.rightMargin = Ui.dp(6);
        cfgRow.addView(configNewBtn, third);
        LinearLayout.LayoutParams third2 = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        third2.rightMargin = Ui.dp(6);
        cfgRow.addView(configEditBtn, third2);
        cfgRow.addView(configDelBtn, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        cfgCard.addView(cfgRow);

        LinearLayout.LayoutParams cfgCardLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cfgCardLp.bottomMargin = Ui.dp(11);
        col.addView(cfgCard, cfgCardLp);
        addIndex("输出格式", cfgCard);

        // ================= 同步 =================
        LinearLayout sync = card();
        sync.addView(heading("同步"));
        TextView syncTip = new TextView(getContext());
        syncTip.setText("本机收藏库与 AnkiWeb 云端的同步方式；手动同步随时可以在左上角头像里点。");
        syncTip.setTextColor(Ui.TEXT_DIM);
        syncTip.setTextSize(12.5f);
        sync.addView(syncTip);

        autoSyncBox = new CheckBox(getContext());
        Ui.check(autoSyncBox);
        autoSyncBox.setTextColor(Ui.TEXT_BODY);
        autoSyncBox.setTextSize(14);
        sync.addView(checkRow(autoSyncBox, "保存卡片后自动同步一次"));

        wifiOnlyBox = new CheckBox(getContext());
        Ui.check(wifiOnlyBox);
        wifiOnlyBox.setTextColor(Ui.TEXT_BODY);
        wifiOnlyBox.setTextSize(14);
        sync.addView(checkRow(wifiOnlyBox, "只在 Wi-Fi 下自动同步"));

        mediaBox = new CheckBox(getContext());
        Ui.check(mediaBox);
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
        Ui.check(clearBox);
        clearBox.setTextColor(Ui.TEXT_BODY);
        clearBox.setTextSize(14);
        habit.addView(checkRow(clearBox, "保存成功后清空输入，方便接着做下一张"));

        previewBox = new CheckBox(getContext());
        Ui.check(previewBox);
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
        aboutText.setText("Anki 助手 " + Version.VERSION_TAG + "\n"
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

        // ================= 更新内容（照 StudyCompanion 的做法，日志打包在 APK 内） =================
        LinearLayout upd = card();
        upd.addView(heading("更新内容"));
        TextView ver = new TextView(getContext());
        ver.setText("当前版本 " + Version.VERSION_TAG);
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
        addIndex("更新内容", upd);



        loadValues();
        attachScrollSpy();
    }

    // ------------------------------------------------------------------ 读写


    // ------------------------------------------------------------------ 左侧索引

    private final java.util.List<View> indexItems = new java.util.ArrayList<View>();
    private final java.util.List<View> indexTargets = new java.util.ArrayList<View>();

    private void addIndex(String title, final View target) {
        if (indexBox == null) return;
        final TextView item = new TextView(getContext());
        item.setText(title);
        item.setTextSize(13.5f);
        item.setGravity(android.view.Gravity.CENTER_VERTICAL);
        item.setPadding(Ui.dp(13), Ui.dp(9), Ui.dp(10), Ui.dp(9));
        item.setLineSpacing(Ui.dp(2), 1f);
        item.setSingleLine(true);       // 栏目名不折行（手机上尤其明显）
        item.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (scroll != null) scroll.smoothScrollTo(0, Math.max(0, target.getTop() - Ui.dp(6)));
                int idx = indexItems.indexOf(item);
                styleIndex(idx);
                // 横排时把选中的那一项滚到看得见的位置
                if (indexBox != null && indexBox.getOrientation() == LinearLayout.HORIZONTAL) {
                    if (indexScroller != null) {
                        int center = item.getLeft() - (indexScroller.getWidth() - item.getWidth()) / 2;
                        indexScroller.smoothScrollTo(Math.max(0, center), 0);
                    }
                }
            }
        });
        indexItems.add(item);
        indexTargets.add(target);
        indexBox.addView(item, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        if (indexItems.size() == 1) styleIndex(0);
    }

    /** 只让选中的那一条是高亮蓝底 */
    private void styleIndex(int active) {
        for (int i = 0; i < indexItems.size(); i++) {
            TextView t = (TextView) indexItems.get(i);
            boolean on = (i == active);
            t.setTextColor(on ? Ui.ACCENT : Ui.SUB);
            t.setTextSize(on ? 14 : 13.5f);
            t.setTypeface(on ? android.graphics.Typeface.DEFAULT_BOLD
                    : android.graphics.Typeface.DEFAULT);
            // 选中：主色淡底 + 左侧一道主色条；未选中：透明（深色皮肤下也不会变成"一块黑洞"）
            android.graphics.drawable.GradientDrawable bg = Ui.round(
                    on ? Ui.ACCENT_SOFT : 0x00000000, 10);
            if (on) {
                bg.setStroke(Ui.dp(1), Ui.alpha(Ui.ACCENT, 0x44));
            }
            t.setBackground(bg);
        }
    }

    /** 滚动时跟着高亮当前卡片 */
    private void attachScrollSpy() {
        if (scroll == null || indexTargets.isEmpty()) return;
        scroll.getViewTreeObserver().addOnScrollChangedListener(
                new android.view.ViewTreeObserver.OnScrollChangedListener() {
            @Override public void onScrollChanged() {
                int y = scroll.getScrollY() + Ui.dp(40);
                int active = 0;
                for (int i = 0; i < indexTargets.size(); i++) {
                    if (indexTargets.get(i).getTop() <= y) active = i;
                }
                styleIndex(active);
            }
        });
    }

    private void loadValues() {
        aiKeyInput.setText(store.aiApiKey());
        modelInput.setText(store.aiModelEffective());
        urlInput.setText(store.aiBaseUrlEffective());
        if (thinkingCheck != null) thinkingCheck.setChecked(store.aiThinking());
        if (autoSyncBox != null) autoSyncBox.setChecked(store.autoSyncAfterSave());
        if (wifiOnlyBox != null) wifiOnlyBox.setChecked(store.syncWifiOnly());
        if (mediaBox != null) mediaBox.setChecked(store.syncMedia());
        if (clearBox != null) clearBox.setChecked(store.clearAfterSave());
        if (previewBox != null) previewBox.setChecked(store.previewAfterFill());
        if (syncInfo != null) syncInfo.setText(AnkiSync.describe(store));
        refreshSkinRow();
        refreshProviderBtn();
        refreshConfigCard();
    }

    public void onShown() {
        // 从别的页面回来时，输入框里可能已被改过，重新读一次（用户没保存就不覆盖已有输入）
        refreshProviderBtn();
    }

    private void save() {
        store.setAiApiKey(aiKeyInput.getText().toString());
        store.setAiModel(modelInput.getText().toString());
        store.setAiBaseUrl(urlInput.getText().toString());
        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());
        if (autoSyncBox != null) store.setAutoSyncAfterSave(autoSyncBox.isChecked());
        if (wifiOnlyBox != null) store.setSyncWifiOnly(wifiOnlyBox.isChecked());
        if (mediaBox != null) store.setSyncMedia(mediaBox.isChecked());
        if (clearBox != null) store.setClearAfterSave(clearBox.isChecked());
        if (previewBox != null) store.setPreviewAfterFill(previewBox.isChecked());
        if (act != null) act.updateSyncLamp();
    }

    // ------------------------------------------------------------------ 输出格式 config

    private void refreshConfigCard() {
        if (configStatus == null) return;
        CardConfig c = store.activeConfig();
        StringBuilder sb = new StringBuilder();
        sb.append("当前：").append(c.name);
        sb.append(c.builtin ? "（内置）" : "（自定义）");
        sb.append("\n字段 ").append(c.fields.size()).append(" 个：");
        for (int i = 0; i < c.fields.size(); i++) {
            if (i > 0) sb.append(" / ");
            sb.append(c.fields.get(i).name);
        }
        sb.append("\n笔记类型：").append(c.noteType);
        configStatus.setText(sb.toString());
        configStatus.setTextColor(Ui.TEXT_BODY);
        boolean custom = !c.builtin;
        configEditBtn.setVisibility(custom ? View.VISIBLE : View.GONE);
        configDelBtn.setVisibility(custom ? View.VISIBLE : View.GONE);
    }

    private void pickConfig() {
        final java.util.List<CardConfig> all = store.configs();
        final String[] names = new String[all.size()];
        int checked = 0;
        String activeId = store.activeConfig().id;
        for (int i = 0; i < all.size(); i++) {
            CardConfig c = all.get(i);
            names[i] = c.name + (c.builtin ? "（内置）" : "");
            if (c.id.equals(activeId)) checked = i;
        }
        new DialogUi.Builder(act)
                .title("选择输出格式")
                .choices(names, checked, new DialogUi.Picker() {
                    @Override public void onPick(int which) {
                        store.setActiveConfigId(all.get(which).id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .negative("取消", null)
                .show();
    }

    /** 新建（cfg==null 或内置）或编辑一个 config */
    private void editConfig(final CardConfig cfg) {
        final boolean creating = (cfg == null || cfg.builtin);
        final CardConfig base = creating ? duplicateOfActive() : cfg;

        LinearLayout box = new LinearLayout(getContext());
        box.setOrientation(LinearLayout.VERTICAL);
        int pad = Ui.dp(16);
        box.setPadding(pad, Ui.dp(8), pad, Ui.dp(4));

        final EditText nameIn = new EditText(getContext());
        nameIn.setText(creating ? "" : base.name);
        nameIn.setHint("例如：雅思词汇、A Level 物理");
        box.addView(small("名字"));
        box.addView(nameIn);

        final EditText fieldsIn = new EditText(getContext());
        fieldsIn.setText(fieldsToText(base));
        fieldsIn.setMinLines(4);
        box.addView(small("字段（每行一个：字段名 = AI键 = 提示；第一行是卡片正面）"));
        box.addView(fieldsIn);

        final EditText deckIn = new EditText(getContext());
        deckIn.setText(base.deckOr(store.defaultDeck()));
        box.addView(small("默认牌组"));
        box.addView(deckIn);

        final EditText tagsIn = new EditText(getContext());
        tagsIn.setText(base.tagsOr(store.defaultTags()));
        box.addView(small("默认标签"));
        box.addView(tagsIn);

        final EditText subjectIn = new EditText(getContext());
        subjectIn.setText(base.subjectOr(store.subject()));
        box.addView(small("学科背景（影响 AI 释义风格）"));
        box.addView(subjectIn);

        final EditText promptIn = new EditText(getContext());
        promptIn.setText(base.prompt);
        promptIn.setMinLines(6);
        box.addView(small("提示词（可用 {word} 与 {subject}）"));
        box.addView(promptIn);

        ScrollView sv = new ScrollView(getContext());
        sv.addView(box);
        new DialogUi.Builder(act)
                .title(creating ? "新建输出格式" : "编辑输出格式")
                .content(sv)
                .positive("保存", new Runnable() {
                    @Override public void run() {
                        CardConfig c = new CardConfig();
                        c.id = creating ? ("cfg" + System.currentTimeMillis()) : base.id;
                        c.name = nameIn.getText().toString().trim();
                        if (c.name.length() == 0) c.name = "未命名 config";
                        c.noteType = c.name;
                        c.fields = textToFields(fieldsIn.getText().toString());
                        c.prompt = promptIn.getText().toString();
                        c.defaultDeck = deckIn.getText().toString().trim();
                        c.defaultTags = tagsIn.getText().toString().trim();
                        c.subject = subjectIn.getText().toString().trim();
                        c.builtin = false;
                        store.saveConfig(c);
                        store.setActiveConfigId(c.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .negative("取消", null)
                .show();
    }

    /** 以内置/当前 config 为模板复制一份，供新建时改 */
    private CardConfig duplicateOfActive() {
        CardConfig src = store.activeConfig();
        CardConfig c = new CardConfig();
        c.name = "";
        c.noteType = "";
        c.prompt = src.prompt;
        c.fields = new java.util.ArrayList<CardConfig.Field>();
        for (CardConfig.Field f : src.fields) {
            c.fields.add(new CardConfig.Field(f.name, f.key, f.hint, f.latex));
        }
        return c;
    }

    private void deleteActiveConfig() {
        final CardConfig c = store.activeConfig();
        if (c.builtin) return;
        final CardConfig target = c;
        new DialogUi.Builder(act)
                .title("删除输出格式")
                .message("确定删除「" + c.name + "」吗？用它做过的卡片不受影响。")
                .negative("取消", null)
                .positive("删除", new Runnable() {
                    @Override public void run() {
                        store.deleteConfig(target.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .show();
    }

    private void onConfigChanged() {
        if (act instanceof MainActivity) ((MainActivity) act).onCardConfigChanged();
    }




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
        int[] dots = new int[all.size()];
        int checked = 0;
        for (int i = 0; i < all.size(); i++) {
            names[i] = all.get(i).name + (all.get(i).dark ? "（深色）" : "");
            dots[i] = all.get(i).accent;
            if (all.get(i).id.equals(store.theme())) checked = i;
        }
        new DialogUi.Builder(act)
                .title("选择皮肤")
                .message("换皮肤会重建界面；正在编辑的卡片内容不会丢。")
                .choiceColors(dots)
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

    private TextView small(String text) {
        TextView t = new TextView(getContext());
        t.setText(text);
        t.setTextColor(Ui.SUB);
        t.setTextSize(12.5f);
        t.setPadding(0, Ui.dp(8), 0, 0);
        return t;
    }

    /** 字段对象 -> 可编辑文本（每行：字段名 = AI键 = 提示） */
    private String fieldsToText(CardConfig c) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < c.fields.size(); i++) {
            CardConfig.Field f = c.fields.get(i);
            if (i > 0) sb.append("\n");
            sb.append(f.name).append(" = ").append(f.key);
            if (f.hint != null && f.hint.length() > 0) sb.append(" = ").append(f.hint);
        }
        return sb.toString();
    }

    /** 文本 -> 字段对象；AI 键留空时自动生成 */
    private java.util.List<CardConfig.Field> textToFields(String text) {
        java.util.List<CardConfig.Field> out = new java.util.ArrayList<CardConfig.Field>();
        String[] lines = text == null ? new String[0] : text.split("\n");
        int idx = 0;
        for (String line : lines) {
            String s = line.trim();
            if (s.length() == 0) continue;
            String[] parts = s.split("=");
            String name = parts[0].trim();
            if (name.length() == 0) continue;
            String key = parts.length > 1 ? parts[1].trim() : "";
            String hint = parts.length > 2 ? parts[2].trim() : "";
            if (key.length() == 0 && idx > 0) key = "f" + idx;
            out.add(new CardConfig.Field(name, key, hint, false));
            idx++;
        }
        if (out.isEmpty()) out = CardConfig.defaultConfig().fields;
        return out;
    }

    private void refreshProviderBtn() {
        providerBtn.setText("当前：" + AiClient.presetLabel(store.aiProvider()) + "  ▾");
        providerBtn.setSingleLine(true);
        providerBtn.setEllipsize(android.text.TextUtils.TruncateAt.END);
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

        // 弹窗里不再直接丢 Markdown 原文（以前和 SC 老版本一样，## / - / ** 全都露出来），
        // 改用和「更新内容」阅读器同一套自绘 Markdown 渲染
        LinearLayout root = new LinearLayout(getContext());
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Ui.CARD);

        LinearLayout head = new LinearLayout(getContext());
        head.setOrientation(LinearLayout.VERTICAL);
        head.setBackground(Ui.roundTop(Ui.ACCENT_SOFT, 20));
        head.setPadding(Ui.dp(18), Ui.dp(14), Ui.dp(18), Ui.dp(12));

        TextView title = new TextView(getContext());
        title.setText("发现新版本 " + rel.tag);
        title.setTextSize(18);
        title.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        title.setTextColor(Ui.INK);
        head.addView(title);

        TextView sub = new TextView(getContext());
        sub.setText("当前版本 " + Version.VERSION_TAG
                + (rel.apkSize > 0 ? "　·　安装包 " + Math.max(1, rel.apkSize / 1024 / 1024) + " MB" : "")
                + (rel.name != null && rel.name.length() > 0 ? "　·　" + rel.name : ""));
        sub.setTextSize(12);
        sub.setTextColor(Ui.SUB);
        sub.setPadding(0, Ui.dp(4), 0, 0);
        head.addView(sub);
        root.addView(head);

        final View notesView;
        final android.util.DisplayMetrics dm = getResources().getDisplayMetrics();
        if (notes.length() == 0) {
            TextView empty = new TextView(getContext());
            empty.setText("这个版本没有写更新说明。");
            empty.setTextSize(13.5f);
            empty.setTextColor(Ui.TEXT_BODY);
            empty.setPadding(Ui.dp(18), Ui.dp(14), Ui.dp(18), Ui.dp(14));
            root.addView(empty, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            notesView = empty;
        } else {
            notesView = ChangelogView.markdownScroll(getContext(), notes, 18);
            // 先按内容量出需要多高：短说明就贴着按钮（不留空白），太长则封顶在屏幕 62%，内部滚动。
            // 注意必须**显示前**量好并写死高度 —— 显示后再调，长内容会把窗口先撑到屏幕外。
            int avail = (int) (dm.widthPixels * 0.94) - Ui.dp(36);
            notesView.measure(
                    View.MeasureSpec.makeMeasureSpec(Math.max(avail, Ui.dp(200)),
                            View.MeasureSpec.AT_MOST),
                    View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED));
            int contentH = notesView.getMeasuredHeight();
            int maxH = (int) (dm.heightPixels * 0.62);
            int finalH = Math.max(Ui.dp(60), Math.min(contentH > 0 ? contentH : maxH, maxH));
            root.addView(notesView, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, finalH));
        }

        new DialogUi.Builder(act)
                .content(root)
                .wide()
                .negative("稍后", null)
                .neutral("打开仓库", new Runnable() {
                    @Override public void run() {
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
                .positive("下载并安装", new Runnable() {
                    @Override public void run() { downloadAndInstall(rel); }
                })
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
        new DialogUi.Builder(act)
                .title("选择 AI 服务商")
                .choices(labels, -1, new DialogUi.Picker() {
                    @Override
                    public void onPick(int which) {
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
                .negative("取消", null)
                .show();
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
