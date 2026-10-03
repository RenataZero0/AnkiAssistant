"""手机端 UI 修正：
   1) 「更新内容」真正挪到设置最后（按 addIndex 顺序检查）
   2) 弹窗：去掉阴影；按钮不换行（单行 + 更紧凑）
   3) 顶栏：去掉阴影、手机端压到 48dp、头像与版本徽标缩小
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------- 1) 更新内容挪到最后 ----------
p = APP + r"\src\com\ankiassistant\SettingsView.java"
s = io.open(p, encoding="utf-8").read()
start = s.find("        // ================= 更新内容")
end = s.find("        // ================= 同步", start)
if start > 0 and end > start:
    block = s[start:end]
    s = s[:start] + s[end:]
    idx = s.find("        loadValues();")
    s = s[:idx] + block + "\n" + s[idx:]
    print("更新内容已挪到所有栏目之后")
else:
    print("!! 定位失败 start=%d end=%d" % (start, end))
io.open(p, "w", encoding="utf-8", newline="\n").write(s)

# ---------- 2) 弹窗：去阴影 + 按钮单行 ----------
p2 = APP + r"\src\com\ankiassistant\DialogUi.java"
s2 = io.open(p2, encoding="utf-8").read()
s2 = s2.replace("            root.setElevation(Ui.dp(10));\n", "")
s2 = s2.replace("            root.setPadding(Ui.dp(22), Ui.dp(20), Ui.dp(22), Ui.dp(12));",
                "            root.setPadding(Ui.dp(20), Ui.dp(18), Ui.dp(20), Ui.dp(10));")
s2 = s2.replace('''            b.setTextSize(15);
            b.setTextColor(color);
            b.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
            b.setBackground(Ui.press(0x00000000, 10));
            b.setPadding(Ui.dp(16), Ui.dp(8), Ui.dp(16), Ui.dp(8));
            b.setMinWidth(0);
            b.setMinimumWidth(0);''',
'''            b.setTextSize(14.5f);
            b.setTextColor(color);
            b.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
            b.setBackground(Ui.press(0x00000000, 10));
            b.setPadding(Ui.dp(10), Ui.dp(8), Ui.dp(10), Ui.dp(8));
            b.setMinWidth(0);
            b.setMinimumWidth(0);
            b.setSingleLine(true);          // 中文按钮不能折成"登录并同/步"
            b.setAllCaps(false);''')
s2 = s2.replace("            lp.leftMargin = Ui.dp(4);", "            lp.leftMargin = Ui.dp(2);")
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("弹窗阴影已去、按钮改单行")

# ---------- 3) 顶栏 ----------
p3 = APP + r"\src\com\ankiassistant\MainActivity.java"
s3 = io.open(p3, encoding="utf-8").read()
# 顶栏高度：手机 48dp，平板 56dp
s3 = s3.replace('''        column.addView(topBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(56)));''',
'''        column.addView(topBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(isPhone() ? 48 : 56)));''')
s3 = s3.replace("        topBar.setElevation(Ui.dp(2));", "        // 不要阴影：手机上那层灰边看着很脏")
# 手机顶栏里的头像与版本号再小一点
s3 = s3.replace('''            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(28), Ui.dp(28));''',
                '''            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(26), Ui.dp(26));''')
s3 = s3.replace('''        ver.setPadding(Ui.dp(9), Ui.dp(3), Ui.dp(9), Ui.dp(3));''',
                '''        ver.setPadding(Ui.dp(7), Ui.dp(2), Ui.dp(7), Ui.dp(2));''')
# 顶栏标题字号：手机小一点
s3 = s3.replace('''        Ui.title(title, "Anki 助手");
        title.setTextSize(18);''',
'''        Ui.title(title, "Anki 助手");
        title.setTextSize(isPhone() ? 16.5f : 18);''')
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("顶栏已压缩、阴影已去")

# ---------- 4) 内容区顶部留白：手机上小一点 ----------
p4 = APP + r"\src\com\ankiassistant\BrowseView.java"
s4 = io.open(p4, encoding="utf-8").read()
s4 = s4.replace("        setPadding(Ui.dp(12), Ui.dp(14), Ui.dp(12), Ui.dp(12));",
                "        setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(10));")
io.open(p4, "w", encoding="utf-8", newline="\n").write(s4)
print("浏览页留白已微调")
