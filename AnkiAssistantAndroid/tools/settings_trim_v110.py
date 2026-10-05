"""设置页调整：
   · 删除「默认值」卡片（默认值下沉到 config）
   · 删除「AnkiWeb 同步」卡片（登录/同步移到左上角头像）
   · 「输出格式（config）」→「输出格式」，文案写专业
   · config 编辑器加上 默认牌组 / 默认标签 / 学科背景
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()
lines = src.split("\n")


def find_line(pred, start=0):
    for i in range(start, len(lines)):
        if pred(lines[i]):
            return i
    return -1


def cut_block(start_marker, end_marker):
    """删除从 start_marker 行到 end_marker 行（含）之间的内容"""
    global lines
    a = find_line(lambda l: start_marker in l)
    if a < 0:
        return False
    b = find_line(lambda l: end_marker in l, a)
    if b < 0:
        return False
    removed = b - a + 1
    lines = lines[:a] + lines[b + 1:]
    print("  删除 %s..%s（%d 行）" % (start_marker.strip()[:28], end_marker.strip()[:28], removed))
    return True


# 1) 删「默认值」卡片（从注释行到 col.addView(def, defLp);）
cut_block("// ================= 默认值", "col.addView(def, defLp);")
# 2) 删「内置引擎 / AnkiWeb」卡片
cut_block("// ================= 内置引擎", "col.addView(eng, engLp);")

src = "\n".join(lines)
io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("设置页卡片删除完成")
