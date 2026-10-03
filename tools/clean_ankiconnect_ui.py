"""一次性清理：把 SettingsView / MainActivity 里 AnkiConnect 与 AnkiDroid 的 UI 残留删掉。

按「整行匹配」删除，避免行号漂移；跑完会打印每处删了什么。
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

DEL_LINE_PATTERNS = [
    r"^\s*private EditText hostInput, portInput, ankiKeyInput;\s*$",
    r"^\s*private CheckBox deviceCheck;\s*$",
    r"^\s*private TextView deviceTip;\s*$",
    r"^\s*private Button deviceAuthBtn;\s*$",
    r"^\s*hostInput\.setText\(store\.ankiHost\(\)\);\s*$",
    r"^\s*portInput\.setText\(String\.valueOf\(store\.ankiPort\(\)\)\);\s*$",
    r"^\s*ankiKeyInput\.setText\(store\.ankiApiKey\(\)\);\s*$",
    r"^\s*refreshAnkiDroid\(\);\s*$",
    r"^\s*if \(deviceCheck != null\) refreshAnkiDroid\(\);\s*$",
    r"^\s*if \(hostInput\.getText\(\)\.length\(\) == 0\) loadValues\(\);\s*$",
    r"^\s*store\.setAnkiHost\(hostInput\.getText\(\)\.toString\(\)\);\s*$",
    r"^\s*\? \"8765\" : portInput\.getText\(\)\.toString\(\)\.trim\(\)\);\s*$",
    r"^\s*store\.setAnkiApiKey\(ankiKeyInput\.getText\(\)\.toString\(\)\);\s*$",
    r"^\s*if \(deviceCheck != null\) store\.setUseAnkiDroid\(deviceCheck\.isChecked\(\)\);\s*$",
    r"^\s*ankiStatus\.setText\(\"设置已保存 ✓\"\);\s*$",
    r"^\s*ankiStatus\.setTextColor\(Ui\.GREEN\);\s*$",
]

# 整块删除：从匹配 start 的行开始，到匹配 end 的行（含）为止
DEL_BLOCKS = [
    (r"^\s*public void refreshAnkiDroid\(\) \{", r"^\s*\}\s*$", "refreshAnkiDroid()"),
    (r"^\s*private void requestAnkiDroidPermission\(\) \{", r"^\s*\}\s*$", "requestAnkiDroidPermission()"),
    (r"^\s*private void testAnki\(\) \{", r"^\s*\}\s*$", "testAnki()"),
]

FIXUPS = [
    # 字段声明去掉不再存在的控件
    ("private Button providerBtn, testAnkiBtn, testAiBtn, saveBtn;",
     "private Button providerBtn, testAiBtn, saveBtn;"),
    ("private TextView ankiStatus, aiStatus, updateStatus;",
     "private TextView aiStatus, updateStatus;"),
    # 卡片标题与说明
    ('eng.addView(heading("内置引擎（不需要 AnkiDroid，也不需要电脑）"));',
     'eng.addView(heading("AnkiWeb 同步（内置引擎）"));'),
    ('engTip.setText("APK 里带着 Anki 官方的 Rust 后端（rslib/rsdroid，AGPL-3.0）。"\n'
     '                + "开启后卡片写进设备自己的收藏库，并由它直接和 AnkiWeb 同步 —— "\n'
     '                + "既不用装 AnkiDroid，也不用电脑开着 Anki。");',
     'engTip.setText("卡片写进设备自己的收藏库，并由内置引擎直接与 AnkiWeb 同步。"\n'
     '                + "登录一次即可；密码不会保存在设备上。");'),
    # 删掉冗长说明
    ('ver.setText("当前版本 " + Version.VERSION_TAG\n'
     '                + "　·　更新日志打包在应用内，离线可看");',
     'ver.setText("当前版本 " + Version.VERSION_TAG);'),
    ('thinkingCheck = new CheckBox(getContext());', 'thinkingCheck = new CheckBox(getContext());'),
    ('"让思考型模型先思考（更慢；思考过程显示在制卡页）"', '"让思考型模型先思考"'),
    # AnkiDroid 权限回调里对已删方法的调用
    ("            if (settingsView != null) settingsView.refreshAnkiDroid();",
     ""),
]

for name, patterns, blocks in (("SettingsView.java", DEL_LINE_PATTERNS, DEL_BLOCKS),
                               ("MainActivity.java", [], [])):
    path = APP + r"\src\com\ankiassistant\\" + name
    lines = io.open(path, encoding="utf-8").read().split("\n")
    out = []
    i = 0
    removed = 0
    while i < len(lines):
        line = lines[i]
        matched_block = None
        for start_re, end_re, label in blocks:
            if re.match(start_re, line):
                matched_block = (label, start_re)
                break
        if matched_block:
            label = matched_block[0]
            # 往回吃掉紧跟其前的注释/空行
            while out and (out[-1].strip().startswith("/**") or out[-1].strip().startswith("*")
                           or out[-1].strip().startswith("//") or out[-1].strip() == ""):
                if out[-1].strip() == "" and not out[-2:-1]:
                    break
                out.pop()
            depth = 0
            while i < len(lines):
                depth += lines[i].count("{") - lines[i].count("}")
                removed += 1
                if depth <= 0 and lines[i].strip() == "}":
                    i += 1
                    break
                i += 1
            print("  删除块 %s（%d 行）" % (label, removed))
            continue
        if any(re.match(p, line) for p in patterns):
            removed += 1
            i += 1
            continue
        out.append(line)
        i += 1
    text = "\n".join(out)
    for old, new in FIXUPS:
        if old in text:
            text = text.replace(old, new)
            print("  替换：%s" % old.strip().split("\n")[0][:60])
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print("%s：删除/替换完成" % name)
