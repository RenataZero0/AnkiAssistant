"""去掉输出格式选择器上误加的色点调用（只有皮肤需要）。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\CNUK"  # placeholder, replaced below
APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()
old = '''                .title("选择输出格式")
                .choiceColors(dots)'''
new = '''                .title("选择输出格式")'''
if old in s:
    s = s.replace(old, new, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("已移除")
else:
    print("!! 未匹配")
