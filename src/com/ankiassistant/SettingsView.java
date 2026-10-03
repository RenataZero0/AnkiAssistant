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
    private EditText deckInput, tagInput, subjectInput;
    private CheckBox autoSyncBox;
    private Button providerBtn, testAiBtn, saveBtn;
    private TextView aiStatus, updateStatus;
    private ProgressBar aiBusy;
    private CheckBox thinkingCheck;
    private TextView engineStatus;
    private Button engineTestBtn;
    private CheckBox engineCheck;
    private TextView configStatus;
    private Button configPickBtn, configNewBtn, configEditBtn, configDelBtn;
    private TextView syncStatus;
    private EditText syncUserInput, syncPassInput;
    private Button syncBtn;
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

        // ================= 输出格式 config =================
        LinearLayout cfgCard = card();
        cfgCard.addView(heading("输出格式（config）"));
        TextView cfgTip = new TextView(getContext());
        cfgTip.setText("决定 AI 按什么格式产出、卡片有哪些字段。默认那套是 A Level 数学/物理术语卡；"
                + "也可以自己新建一套（自定字段与提示词）。");
        cfgTip.setTextColor(Ui.TEXT_DIM);
        cfgTip.setTextSize(12.5f);
        cfgTip.setLineSpacing(0, 1.15f);
        cfgCard.addView(cfgTip);

        configStatus = status();
        cfgCard.addView(configStatus);

        configPickBtn = new Button(getContext());
        configPickBtn.setText("切换 config");
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

        // ================= 内置引擎（Anki 官方 Rust 后端打包在 APK 里） =================
        LinearLayout eng = card();
        eng.addView(heading("AnkiWeb 同步（内置引擎）"));
        TextView engTip = new TextView(getContext());
        engTip.setText("卡片写进设备自己的收藏库，并由内置引擎直接与 AnkiWeb 同步。"
                + "登录一次即可；密码不会保存在设备上。");
        engTip.setTextColor(Ui.TEXT_DIM);
        engTip.setTextSize(12.5f);
        engTip.setLineSpacing(0, 1.15f);
        eng.addView(engTip);

        engineCheck = new CheckBox(getContext());
        engineCheck.setTextColor(Ui.TEXT_BODY);
        engineCheck.setTextSize(14);
        eng.addView(checkRow(engineCheck, "优先使用内置引擎（写进本机收藏库）"));

        syncUserInput = input("AnkiWeb 邮箱", false);
        syncPassInput = input("AnkiWeb 密码（只用于登录，不会存下来）", true);
        addTo(eng, "AnkiWeb 账号", syncUserInput);
        addTo(eng, "密码", syncPassInput);

        engineStatus = status();
        eng.addView(engineStatus);

        syncBtn = new Button(getContext());
        syncBtn.setText("登录并同步到 AnkiWeb");
        Ui.primary(syncBtn);
        syncBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { doAnkiWebSync(); }
        });
        LinearLayout.LayoutParams syncLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        syncLp.topMargin = Ui.dp(8);
        eng.addView(syncBtn, syncLp);

        syncStatus = status();
        eng.addView(syncStatus);

        engineTestBtn = new Button(getContext());
        engineTestBtn.setText("检测内置引擎");
        Ui.primary(engineTestBtn);
        engineTestBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { testEngine(); }
        });
        LinearLayout.LayoutParams engLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        engLp.topMargin = Ui.dp(8);
        eng.addView(engineTestBtn, engLp);

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

        loadValues();
    }

    // ------------------------------------------------------------------ 读写

    private void loadValues() {
        aiKeyInput.setText(store.aiApiKey());
        modelInput.setText(store.aiModelEffective());
        urlInput.setText(store.aiBaseUrlEffective());
        deckInput.setText(store.defaultDeck());
        tagInput.setText(store.defaultTags());
        subjectInput.setText(store.subject());
        autoSyncBox.setChecked(store.autoSync());
        if (thinkingCheck != null) thinkingCheck.setChecked(store.aiThinking());
        if (engineCheck != null) engineCheck.setChecked(store.useEngine());
        if (syncUserInput != null) syncUserInput.setText(store.ankiWebUser());
        if (syncStatus != null) syncStatus.setText(AnkiSync.describe(store));
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
        store.setDefaultDeck(deckInput.getText().toString());
        store.setDefaultTags(tagInput.getText().toString());
        store.setSubject(subjectInput.getText().toString());
        store.setAutoSync(autoSyncBox.isChecked());
        if (thinkingCheck != null) store.setAiThinking(thinkingCheck.isChecked());
        if (engineCheck != null) store.setUseEngine(engineCheck.isChecked());
        // 连接信息可能改了，侧栏那盏状态灯跟着复测一次（手机端没有灯，内部会自己忽略）
        if (act != null) act.checkAnkiLamp();
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
        new AlertDialog.Builder(act)
                .setTitle("选择 config")
                .setSingleChoiceItems(names, checked, new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int which) {
                        store.setActiveConfigId(all.get(which).id);
                        d.dismiss();
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
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
        nameIn.setHint("例如：雅思词汇");
        box.addView(small("名字"));
        box.addView(nameIn);

        final EditText fieldsIn = new EditText(getContext());
        fieldsIn.setText(fieldsToText(base));
        fieldsIn.setMinLines(4);
        box.addView(small("字段（每行一个：字段名 = AI键 = 提示；第一行是卡片正面）"));
        box.addView(fieldsIn);

        final EditText promptIn = new EditText(getContext());
        promptIn.setText(base.prompt);
        promptIn.setMinLines(6);
        box.addView(small("提示词（可用 {word} 与 {subject}）"));
        box.addView(promptIn);

        ScrollView sv = new ScrollView(getContext());
        sv.addView(box);
        new AlertDialog.Builder(act)
                .setTitle(creating ? "新建 config" : "编辑 config")
                .setView(sv)
                .setPositiveButton("保存", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        CardConfig c = new CardConfig();
                        c.id = creating ? ("cfg" + System.currentTimeMillis()) : base.id;
                        c.name = nameIn.getText().toString().trim();
                        if (c.name.length() == 0) c.name = "未命名 config";
                        c.noteType = c.name;
                        c.fields = textToFields(fieldsIn.getText().toString());
                        c.prompt = promptIn.getText().toString();
                        c.builtin = false;
                        store.saveConfig(c);
                        store.setActiveConfigId(c.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
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
        new AlertDialog.Builder(act)
                .setTitle("删除 config")
                .setMessage("确定删除「" + c.name + "」吗？已经用它做过的卡片不受影响。")
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        store.deleteConfig(c.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private void onConfigChanged() {
        if (act instanceof MainActivity) ((MainActivity) act).onCardConfigChanged();
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
        root.setBackgroundColor(Ui.WHITE);

        LinearLayout head = new LinearLayout(getContext());
        head.setOrientation(LinearLayout.VERTICAL);
        head.setBackground(Ui.round(Ui.ACCENT_SOFT, 0));
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

        final AlertDialog dlg = new AlertDialog.Builder(act)
                .setView(root)
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
                .create();
        dlg.show();
        android.view.Window w = dlg.getWindow();
        if (w != null) {
            // 高度交给内容决定（说明区的最终高度已在显示前按内容量好）
            w.setLayout((int) (getResources().getDisplayMetrics().widthPixels * 0.94),
                    ViewGroup.LayoutParams.WRAP_CONTENT);
            w.setBackgroundDrawable(Ui.round(Ui.WHITE, 14));
        }
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

    /** 检测内置引擎：能不能加载 .so、能不能开收藏库、能不能读到牌组 */
    private void testEngine() {
        engineTestBtn.setEnabled(false);
        engineStatus.setText("正在检测内置引擎…");
        engineStatus.setTextColor(Ui.SUB);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                AnkiEngine e = null;
                final StringBuilder msg = new StringBuilder();
                int color = Ui.GREEN;
                try {
                    e = new AnkiEngine();
                    msg.append("原生库加载成功 ✓ 后端已启动\n");
                    e.openCollection(getContext());
                    msg.append("收藏库：" + e.collectionPath() + "\n");
                    java.util.Map<String, Long> decks = e.deckNames();
                    msg.append("牌组 " + decks.size() + " 个");
                    if (!decks.isEmpty()) {
                        int i = 0;
                        for (String n : decks.keySet()) {
                            if (i++ >= 4) { msg.append(" …"); break; }
                            msg.append("\n　· " + n);
                        }
                    }
                    msg.append("\n\n内置引擎可用：卡片可以完全在本机写入并同步 AnkiWeb");
                } catch (final Exception ex) {
                    color = Ui.RED;
                    msg.append("失败：" + ex.getMessage());
                    android.util.Log.e("AnkiAssistant", "内置引擎检测失败", ex);
                } finally {
                    if (e != null) e.close();
                }
                final String text = msg.toString();
                final int c2 = color;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        engineTestBtn.setEnabled(true);
                        engineStatus.setText(text);
                        engineStatus.setTextColor(c2);
                    }
                });
            }
        });
    }
    /** 登录 AnkiWeb 并同步；需要全量同步时弹对话框让用户在"上传/下载"之间选 */
    private void doAnkiWebSync() {
        save();
        final String user = syncUserInput.getText().toString().trim();
        final String pass = syncPassInput.getText().toString();
        syncBtn.setEnabled(false);
        syncStatus.setText("正在登录并同步…（首次同步可能要一会儿）");
        syncStatus.setTextColor(Ui.SUB);
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(getContext(), store, user, pass, null);
                    syncPassInput.setText("");
                    showSyncResult(out.message, Ui.GREEN);
                } catch (final AnkiSync.FullSyncRequired f) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            syncBtn.setEnabled(true);
                            askFullSyncPass2(user, pass, f.reason);
                        }
                    });
                } catch (final Exception e) {
                    syncPassInput.setText("");
                    showSyncResult("同步失败：" + e.getMessage(), Ui.RED);
                }
            }
        });
    }

    /** 第二轮：用户选完上传/下载后真正执行全量同步 */
    private void askFullSyncPass2(final String user, final String pass, String reason) {
        syncStatus.setText(reason + "　请选择同步方向：");
        syncStatus.setTextColor(Ui.AMBER);
        new AlertDialog.Builder(act)
                .setTitle("需要全量同步")
                .setMessage(reason + "\n\n上传：用本机的卡片覆盖云端\n下载：用云端覆盖本机"
                        + "\n\n（如果本机是刚装的、云端才有你的卡片，选「下载云端」）")
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
                .setNegativeButton("取消", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        syncStatus.setText("已取消全量同步");
                        syncStatus.setTextColor(Ui.AMBER);
                    }
                })
                .show();
    }

    private void runFullSync(final String user, final String pass, final Boolean upload) {
        syncStatus.setText(upload.booleanValue() ? "正在上传本机收藏库…" : "正在下载云端收藏库…");
        syncStatus.setTextColor(Ui.SUB);
        syncBtn.setEnabled(false);
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(getContext(), store, user, pass, upload);
                    showSyncResult(out.message, Ui.GREEN);
                } catch (final Exception e) {
                    showSyncResult("全量同步失败：" + e.getMessage(), Ui.RED);
                }
            }
        });
    }

    private void showSyncResult(final String msg, final int color) {
        Th.ui(new Runnable() {
            @Override public void run() {
                syncBtn.setEnabled(true);
                syncStatus.setText(msg + "\n" + AnkiSync.describe(store));
                syncStatus.setTextColor(color);
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
