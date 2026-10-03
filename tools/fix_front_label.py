"""制卡页的"正面"文案改成跟随当前输出格式的第一个字段名（不再写死"正面"）。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\CreateView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

s = s.replace('        TextView wordLabel = smallLabel("正面 · 单词（你输入的词就是卡片正面）");',
              '        wordLabel = smallLabel("单词");   // 文案跟着当前输出格式的第一个字段走')

s = s.replace('        status("先输入单词（卡片正面）", Ui.RED);',
              '        status("请先输入要制卡的词", Ui.RED);')

# 字段声明
s = s.replace("    private boolean editorReady;", "    private boolean editorReady;\n    private TextView wordLabel;")

# 刷新时更新文案
if "refreshWordLabel" not in s:
    helper = '''
    /** 顶部那个标签写当前格式的第一个字段名（以前写死"正面 · 单词"，但"正面"其实由格式决定） */
    private void refreshWordLabel() {
        if (wordLabel == null) return;
        String name = "单词";
        try {
            CardConfig cfg = store.activeConfig();
            if (cfg != null && cfg.fields != null && cfg.fields.size() > 0
                    && cfg.fields.get(0).name != null && cfg.fields.get(0).name.length() > 0) {
                name = cfg.fields.get(0).name;
            }
        } catch (Exception ignored) { }
        wordLabel.setText(name + " · 你输入的词就是卡片开头的那一栏");
    }
'''
    s = s.replace("    public void refreshConfig() {", helper + "\n    public void refreshConfig() {", 1)
    # refreshConfig 里调用
    s = s.replace('''    public void refreshConfig() {
        pushConfig();''', '''    public void refreshConfig() {
        refreshWordLabel();
        pushConfig();''')
    # 首次构建后也调一次
    s = s.replace("        editorReady = true;\n                pushTheme();", "        editorReady = true;\n                pushTheme();")
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("文案已改为跟随格式")

# 类顶部注释也顺手改准确
s = io.open(p, encoding="utf-8").read()
s = s.replace(" *   单词（正面） → AI 按 PRMOPT 第二节第 3 条的格式自动填充背面 → 富文本微调 → 保存到 Anki。",
              " *   输入一个词 → AI 按当前输出格式填充各字段 → 富文本微调 → 保存到 Anki。")
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("类注释已更新")

# 在 build 之后调用一次
s = io.open(p, encoding="utf-8").read()
if "refreshWordLabel();" in s and s.count("refreshWordLabel();") == 1:
    s = s.replace("        loadDraftsCountIfAny();", "        refreshWordLabel();\n        loadDraftsCountIfAny();") \
        if "loadDraftsCountIfAny();" in s else s
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("调用点：", s.count("refreshWordLabel();"))
