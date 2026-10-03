"""设置页整理：
   1) 栏目顺序：AI 自动填充 → 输出格式 → 制卡习惯 → 同步 → 皮肤 → 关于 → 更新内容（放最后）
   2) 左侧索引：选中项改成主色淡底 + 主色文字 + 左侧色条，字号与间距调顺（深色下也好看）
   3) 皮肤选择弹窗：每一项前面加一个主题色圆点
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) 把「更新内容」卡片挪到最后 ----------------
p = APP + r"\src\com\ankiassistant\SettingsView.java"
s = io.open(p, encoding="utf-8").read()

start = s.find("        // ================= 更新内容")
end = s.find("        loadValues();", start)
if start > 0 and end > start:
    block = s[start:end].rstrip() + "\n"
    s = s[:start] + s[end:]
    # 插到 loadValues() 之前（也就是所有栏目之后）
    idx = s.find("        loadValues();")
    s = s[:idx] + block + "\n" + s[idx:]
    print("更新内容已挪到最后")
else:
    print("!! 未找到更新内容卡片 start=%d end=%d" % (start, end))

# ---------------- 2) 左侧索引样式 ----------------
old_style = '''    private void styleIndex(int active) {
        for (int i = 0; i < indexItems.size(); i++) {
            TextView t = (TextView) indexItems.get(i);
            boolean on = (i == active);
            t.setTextColor(on ? Ui.ACCENT : Ui.TEXT_BODY);
            t.setTypeface(on ? android.graphics.Typeface.DEFAULT_BOLD
                    : android.graphics.Typeface.DEFAULT);
            t.setBackground(on ? Ui.round(Ui.CARD, 9) : null);
        }
    }'''
new_style = '''    private void styleIndex(int active) {
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
    }'''
if old_style in s:
    s = s.replace(old_style, new_style, 1)
    print("索引样式已改")
else:
    print("!! 索引样式未匹配")

# 索引项本身的字号/内边距
s = s.replace('        item.setTextSize(13);\n        item.setGravity(android.view.Gravity.CENTER_VERTICAL);\n        item.setPadding(Ui.dp(12), Ui.dp(11), Ui.dp(8), Ui.dp(11));',
              '        item.setTextSize(13.5f);\n        item.setGravity(android.view.Gravity.CENTER_VERTICAL);\n'
              '        item.setPadding(Ui.dp(13), Ui.dp(12), Ui.dp(8), Ui.dp(12));\n'
              '        item.setLineSpacing(Ui.dp(2), 1f);')

# 索引栏底色：用卡片色，跟右侧内容拉出层次
s = s.replace("indexBox.setBackgroundColor(Ui.PANEL);", "indexBox.setBackgroundColor(Ui.CARD);")

# ---------------- 3) 皮肤弹窗带色点 ----------------
s = s.replace('''                .choices(names, checked, new DialogUi.Picker() {''',
              '''                .choiceColors(dots)
                .choices(names, checked, new DialogUi.Picker() {''')
s = s.replace('''        java.util.List<Theme> all = Theme.all();
        String[] names = new String[all.size()];
        int checked = 0;
        for (int i = 0; i < all.size(); i++) {
            names[i] = all.get(i).name + (all.get(i).dark ? "（深色）" : "");
            if (all.get(i).id.equals(store.theme())) checked = i;
        }''',
'''        java.util.List<Theme> all = Theme.all();
        String[] names = new String[all.size()];
        int[] dots = new int[all.size()];
        int checked = 0;
        for (int i = 0; i < all.size(); i++) {
            names[i] = all.get(i).name + (all.get(i).dark ? "（深色）" : "");
            dots[i] = all.get(i).accent;
            if (all.get(i).id.equals(store.theme())) checked = i;
        }''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("设置页整理完成")

# ---------------- 4) DialogUi 支持色点 ----------------
p2 = APP + r"\src\com\ankiassistant\DialogUi.java"
s2 = io.open(p2, encoding="utf-8").read()
if "choiceColors" not in s2:
    s2 = s2.replace("        private String[] items;\n        private int checked;",
                    "        private String[] items;\n        private int[] itemColors;\n        private int checked;")
    s2 = s2.replace("        public Builder choices(String[] items, int checked, Picker p) {",
                    "        /** 每一行前面画一个色点（例如皮肤列表） */\n"
                    "        public Builder choiceColors(int[] colors) { this.itemColors = colors; return this; }\n\n"
                    "        public Builder choices(String[] items, int checked, Picker p) {")
    s2 = s2.replace('''                    TextView label = new TextView(act);''',
'''                    if (itemColors != null && i < itemColors.length) {
                        View dot = new View(act);
                        dot.setBackground(Ui.round(itemColors[i], 7));
                        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(
                                Ui.dp(14), Ui.dp(14));
                        dlp.rightMargin = Ui.dp(11);
                        row.addView(dot, dlp);
                    }

                    TextView label = new TextView(act);''')
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("DialogUi 色点已加")
