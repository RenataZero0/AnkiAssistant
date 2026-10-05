"""1) 旋转/尺寸变化：直接让框架重建界面（原来自己 buildUi 会和旧视图混在一起，
      平板竖屏→横屏后出现"左侧栏 + 手机顶栏"同时存在）
   2) 两个弹窗的头部搬进 DialogUi 自己的标题区（内部色块去掉，风格统一）
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------- 1) 旋转 ----------
p = APP + r"\src\com\ankiassistant\MainActivity.java"
s = io.open(p, encoding="utf-8").read()
old = '''    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        buildUi();
        show(current);
    }'''
new = '''    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        // 自己 buildUi 会把新视图接到旧视图上（平板竖屏→横屏后出现"侧栏 + 手机顶栏"共存）。
        // 交给框架重建一次最稳。
        recreate();
    }'''
if old in s:
    s = s.replace(old, new, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("旋转改为 recreate()")
else:
    print("!! onConfigurationChanged 未匹配")

# ---------- 2) 更新弹窗：去掉内部头部 ----------
p2 = APP + r"\src\com\ankiassistant\SettingsView.java"
s2 = io.open(p2, encoding="utf-8").read()
start = s2.find("        LinearLayout head = new LinearLayout(getContext());\n        head.setOrientation(LinearLayout.VERTICAL);\n        head.setBackground(Ui.roundTop(Ui.ACCENT_SOFT, 20));")
end = s2.find("        root.addView(head);", start)
if start > 0 and end > start:
    s2 = s2[:start] + s2[end + len("        root.addView(head);\n"):]
    print("更新弹窗内部头部已移除")
else:
    print("!! 更新弹窗头部未匹配")

s2 = s2.replace('''        new DialogUi.Builder(act)
                .content(root)
                .wide()
                .negative("稍后", null)''',
'''        new DialogUi.Builder(act)
                .title("发现新版本 " + rel.tag)
                .message("当前版本 " + Version.VERSION_TAG
                        + (rel.apkSize > 0 ? "　·　安装包 " + Math.max(1, rel.apkSize / 1024 / 1024) + " MB" : "")
                        + (rel.name != null && rel.name.length() > 0 ? "　·　" + rel.name : ""))
                .content(root)
                .wide()
                .negative("稍后", null)''')
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)

# ---------- 3) 更新日志：头部与底部关闭按钮交给 DialogUi ----------
p3 = APP + r"\src\com\ankiassistant\ChangelogView.java"
s3 = io.open(p3, encoding="utf-8").read()
start = s3.find("        // ---- 头部：当前版本 ----")
end = s3.find("        root.addView(head);", start)
if start > 0 and end > start:
    s3 = s3[:start] + s3[end + len("        root.addView(head);\n"):]
    print("更新日志内部头部已移除")
else:
    print("!! 更新日志头部未匹配")

# 版本标签条：底色改成卡片色 + 下面一条细线
s3 = s3.replace("            chipsBar.setBackgroundColor(Ui.BG);", "            chipsBar.setBackgroundColor(Ui.CARD);")

# 关闭按钮如果自建 footer，就交给 DialogUi 的按钮；这里只把标题/说明补上
s3 = s3.replace('''        final Dialog[] holder = new Dialog[1];
        close.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (holder[0] != null) holder[0].dismiss();
            }
        });
        Dialog made = new DialogUi.Builder(act).content(root).wide().show();''',
'''        Dialog made = new DialogUi.Builder(act)
                .title("更新内容")
                .message("当前版本 " + Version.VERSION_TAG + "　·　" + Changelog.sourceLabel(act)
                        + (fetchError != null && fetchError.length() > 0 ? "　·　在线更新失败：" + fetchError : ""))
                .content(root)
                .wide()
                .negative("关闭", null)
                .show();''')
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("更新日志头部/关闭按钮已交给 DialogUi")
