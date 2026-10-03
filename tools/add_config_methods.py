"""插入 config 相关方法到 SettingsView（卡片 UI 已插入，方法还缺）。"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

METHODS = '''    // ------------------------------------------------------------------ 输出格式 config

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
            if (i > 0) sb.append("\\n");
            sb.append(f.name).append(" = ").append(f.key);
            if (f.hint != null && f.hint.length() > 0) sb.append(" = ").append(f.hint);
        }
        return sb.toString();
    }

    /** 文本 -> 字段对象；AI 键留空时自动生成 */
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

path = APP + r"\src\com\ankiassistant\SettingsView.java"
src = io.open(path, encoding="utf-8").read()
if "private void pickConfig()" in src:
    print("已存在，跳过")
else:
    anchor = "    private void refreshProviderBtn() {"
    if anchor not in src:
        print("!! 找不到插入锚点")
    else:
        src = src.replace(anchor, METHODS + anchor, 1)
        io.open(path, "w", encoding="utf-8", newline="\n").write(src)
        print("方法已插入")

# CreateView: 公开 refreshConfig
cv = APP + r"\src\com\ankiassistant\CreateView.java"
cvs = io.open(cv, encoding="utf-8").read()
if "public void refreshConfig()" not in cvs:
    anchor2 = "    private void pushWord(String word) {"
    add = ('    /** 设置页换了 config 之后调用：重建字段框并刷新模板 */\n'
           '    public void refreshConfig() {\n'
           '        pushConfig();\n'
           '        pushTemplates();\n'
           '    }\n\n')
    cvs = cvs.replace(anchor2, add + anchor2, 1)
    io.open(cv, "w", encoding="utf-8", newline="\n").write(cvs)
    print("CreateView.refreshConfig 已加")

# MainActivity: onCardConfigChanged
ma = APP + r"\src\com\ankiassistant\MainActivity.java"
mas = io.open(ma, encoding="utf-8").read()
if "onCardConfigChanged" not in mas:
    anchor3 = "    private void maybeIntro() {"
    add3 = ('    /** 设置页切换/编辑 config 之后，让制卡页按新字段重建输入框 */\n'
            '    public void onCardConfigChanged() {\n'
            '        if (createView != null) createView.refreshConfig();\n'
            '    }\n\n')
    mas = mas.replace(anchor3, add3 + anchor3, 1)
    io.open(ma, "w", encoding="utf-8", newline="\n").write(mas)
    print("MainActivity.onCardConfigChanged 已加")
