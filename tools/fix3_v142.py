"""在 v1.14.1 基础上修三处：
   1) 「更新内容」弹窗：去掉彩色头部色块，改成干净的标题 + 说明 + 内容
   2) 设置页顶部栏目条：文字不换行、点选后自动把该栏目滚到看得见的位置
   3) AI 卡片里的「服务商」按钮：不换行、过长省略
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------- 1) 更新内容弹窗头部 ----------
p = APP + r"\src\com\ankiassistant\ChangelogView.java"
s = io.open(p, encoding="utf-8").read()
s = s.replace('        head.setBackground(Ui.roundTop(Ui.ACCENT_SOFT, 20));',
              '        head.setBackgroundColor(Ui.CARD);')
s = s.replace('        head.setPadding(Ui.dp(18), Ui.dp(14), Ui.dp(18), Ui.dp(12));',
              '        head.setPadding(Ui.dp(20), Ui.dp(18), Ui.dp(20), Ui.dp(6));')
s = s.replace('        title.setTextSize(19);', '        title.setTextSize(18);')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("更新内容弹窗头部已改干净")

# ---------- 2) 栏目条：不换行 + 点选后滚到可见 ----------
p2 = APP + r"\src\com\ankiassistant\SettingsView.java"
s2 = io.open(p2, encoding="utf-8").read()
s2 = s2.replace('''        item.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (scroll != null) scroll.smoothScrollTo(0, Math.max(0, target.getTop() - Ui.dp(6)));
                styleIndex(indexItems.indexOf(item));
            }
        });''',
'''        item.setSingleLine(true);       // 栏目名不折行（手机上尤其明显）
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
        });''')
s2 = s2.replace("    private LinearLayout indexBox;", "    private LinearLayout indexBox;\n    private android.widget.HorizontalScrollView indexScroller;")
s2 = s2.replace("            ScrollView ix = new ScrollView(getContext());\n            ix.setHorizontalScrollBarEnabled(false);\n            ix.addView(indexBox, new ViewGroup.LayoutParams(\n                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));\n            outer.addView(ix, new LinearLayout.LayoutParams(",
                "            android.widget.HorizontalScrollView ix = new android.widget.HorizontalScrollView(getContext());\n            ix.setHorizontalScrollBarEnabled(false);\n            indexScroller = ix;\n            ix.addView(indexBox, new ViewGroup.LayoutParams(\n                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));\n            outer.addView(ix, new LinearLayout.LayoutParams(")
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("栏目条已修：不换行 + 选中自动滚入视野")

# ---------- 3) AI 服务商按钮不换行 ----------
s2 = io.open(p2, encoding="utf-8").read()
n = 0
def fix_btn(m):
    global n
    n += 1
    return m.group(0)
# refreshProviderBtn / providerBtn 相关：统一加单行与省略
s2 = re.sub(r"(\w*[Pp]rovider\w*)\.setText\(([^;]+)\);",
            lambda m: m.group(1) + ".setText(" + m.group(2) + ");\n        " + m.group(1) + ".setSingleLine(true);\n        " + m.group(1) + ".setEllipsize(android.text.TextUtils.TruncateAt.END);", s2)
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("服务商按钮已加单行省略（%d 处）" % len(re.findall(r"\.setEllipsize\(", s2)))
