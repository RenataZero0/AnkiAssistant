"""把「输出格式（config）」卡片插进 SettingsView：
   · 卡片 UI + 切换/新建/编辑/删除
   · 字段声明、loadValues 刷新
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()

# 1) 字段声明
anchor_fields = "    private CheckBox engineCheck;"
if "configStatus" not in src:
    src = src.replace(
        anchor_fields,
        anchor_fields + "\n"
        "    private TextView configStatus;\n"
        "    private Button configPickBtn, configNewBtn, configEditBtn, configDelBtn;")

# 2) 卡片 UI：插在「AnkiWeb 同步」卡片之前
card_code = '''        // ================= 输出格式 config =================
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

'''
marker = "        // ================= 内置引擎（Anki 官方 Rust 后端打包在 APK 里） ================="
if "输出格式（config）" not in src and "LinearLayout cfgCard" not in src:
    if marker in src:
        src = src.replace(marker, card_code + marker, 1)
        print("  卡片已插入（AnkiWeb 卡片之前）")
    else:
        print("  !! 找不到 AnkiWeb 卡片标记，未插入")

# 3) loadValues 里刷新
if "refreshConfigCard();" not in src:
    src = src.replace("        refreshProviderBtn();",
                      "        refreshProviderBtn();\n        refreshConfigCard();", 1)

# 4) 方法实现：放在 refreshProviderBtn 之前
methods = '''    // ------------------------------------------------------------------ 输出格式 config

    private void refreshConfigCard() {
        if (configStatus == null) return;
        CardConfig c = store.activeConfig();
        StringBuilder sb = new StringBuilder();
        sb.append("当前：").append(c.name);
        sb.append(c.builtin ? "（内置）" : "（自定义）");
        sb.append("\\n字段 ").append(c.fields.size()).append(" 个：");
        for (int i = 0; i < c.fields.size(); i++) {
            if (i > 0) sb.append(" / ");
            sb.append(c.fields.get(i).name);
        }
        sb.append("\\n笔记类型：").append(c.noteType);
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

    /** 新建（cfg==null）或编辑一个 config */
    private void editConfig(final CardConfig cfg) {
        final boolean creating = (cfg == null || cfg.builtin);
        final CardConfig base = creating ? duplicateOfActive() : cfg;

        LinearLayout box = new LinearLayout(getContext());
        box.setOrientation(LinearLayout.VERTICAL);
        int pad = Ui.dp(16);
        box.setPadding(pad, Ui.dp(8), pad, Ui.dp(4));

        final EditText nameIn = new EditText(getContext());
        nameIn.setText(creating ? "" : base.name);
        nameIn.setHint("config 名字，例如：雅思词汇");
        box.addView(small("名字"));
        box.addView(nameIn);

        final EditText fieldsIn = new EditText(getContext());
        fieldsIn.setText(fieldsToText(base));
        fieldsIn.setMinLines(4);
        fieldsIn.setHint("每行一个字段，格式：字段名 = AI键 = 提示\\n第一行是卡片正面（用户输入的词）");
        box.addView(small("字段（每行一个：字段名 = AI键 = 提示）"));
        box.addView(fieldsIn);

        final EditText promptIn = new EditText(getContext());
        promptIn.setText(base.prompt);
        promptIn.setMinLines(6);
        promptIn.setHint("给 AI 的格式说明，可含 {word} 与 {subject}");
        box.addView(small("提示词（可用 {word} 和 {subject}）"));
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

    /** 以当前 config 为模板复制一份（内置那条不允许改，就用它做模板） */
    private CardConfig duplicateOfActive() {
        CardConfig src = store.activeConfig();
        CardConfig c = new CardConfig();
        c.name = src.builtin ? "" : (src.name + " 副本");
        c.noteType = c.name;
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

    /** config 变了：让制卡页按新字段重建输入框 */
    private void onConfigChanged() {
        if (mainActivity() != null) mainActivity().onCardConfigChanged();
    }

    private MainActivity mainActivity() {
        return (act instanceof MainActivity) ? (MainActivity) act : null;
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
            if (i > 0) sb.append("\\n");
            sb.append(f.name).append(" = ").append(f.key);
            if (f.hint != null && f.hint.length() > 0) sb.append(" = ").append(f.hint);
        }
        return sb.toString();
    }

    /** 文本 -> 字段对象；键留空时自动按位置生成（正面那条不需要键） */
    private java.util.List<CardConfig.Field> textToFields(String text) {
        java.util.List<CardConfig.Field> out = new java.util.ArrayList<CardConfig.Field>();
        String[] lines = text == null ? new String[0] : text.split("\\n");
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

'''
anchor_method = "    private void refreshProviderBtn() {"
if "refreshConfigCard()" not in src.split("private void pickConfig")[0]:
    src = src.replace(anchor_method, methods + anchor_method, 1)
    print("  方法已插入")

io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("SettingsView.java 处理完成")
