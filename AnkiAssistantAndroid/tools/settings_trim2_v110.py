"""设置页第二轮清理：
   · 删掉「内置引擎 / AnkiWeb 同步」卡片整块
   · 删掉随之失去用途的字段与方法（登录/同步那套移到 MainActivity）
   · 卡片标题改「输出格式」并润色文案
   · config 编辑器增加 默认牌组 / 默认标签 / 学科背景
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()

# ---------- 1) 删引擎卡片整块 ----------
start = src.find("        // ================= 内置引擎")
end_marker = "\n\n        // ================= 更新内容"
end = src.find(end_marker, start)
if start >= 0 and end > start:
    src = src[:start] + src[end + 2:]
    print("  已删除内置引擎卡片")
else:
    print("  !! 引擎卡片定位失败 start=%d end=%d" % (start, end))

# ---------- 2) 删除方法块 ----------
def drop_method(text, sig):
    i = text.find(sig)
    if i < 0:
        print("  !! 找不到方法 %s" % sig[:50])
        return text
    # 往前吃掉紧邻的注释
    j = i
    while True:
        prev = text.rfind("\n", 0, j - 1)
        line = text[prev + 1:j].strip()
        if line.startswith("//") or line.startswith("*") or line.startswith("/**"):
            j = prev + 1
        else:
            break
    depth = 0
    k = i
    while k < len(text):
        if text[k] == "{":
            depth += 1
        elif text[k] == "}":
            depth -= 1
            if depth == 0:
                k += 1
                break
        k += 1
    # 吃掉后面的空行
    while k < len(text) and text[k] == "\n":
        k += 1
    print("  删除方法 %s" % sig.strip()[:44])
    return text[:j] + text[k:]

for sig in ("    private void testEngine() {",
            "    private void doAnkiWebSync() {",
            "    private void askFullSyncPass2(",
            "    private void runFullSync(",
            "    private void showSyncResult("):
    src = drop_method(src, sig)

# ---------- 3) 字段声明清理 ----------
src = src.replace("    private EditText deckInput, tagInput, subjectInput;\n", "")
src = src.replace("    private CheckBox autoSyncBox;\n", "")
src = src.replace("    private TextView engineStatus;\n", "")
src = src.replace("    private Button engineTestBtn;\n", "")
src = src.replace("    private CheckBox engineCheck;\n", "")
src = src.replace("    private TextView syncStatus;\n", "")
src = src.replace("    private EditText syncUserInput, syncPassInput;\n", "")
src = src.replace("    private Button syncBtn;\n", "")

# ---------- 4) loadValues / save 里对这些控件的引用 ----------
for pat in (
    r"^\s*if \(engineCheck != null\) engineCheck\.setChecked\(store\.useEngine\(\)\);\n",
    r"^\s*if \(syncUserInput != null\) syncUserInput\.setText\(store\.ankiWebUser\(\)\);\n",
    r"^\s*if \(syncStatus != null\) syncStatus\.setText\(AnkiSync\.describe\(store\)\);\n",
    r"^\s*if \(engineCheck != null\) store\.setUseEngine\(engineCheck\.isChecked\(\)\);\n",
    r"^\s*if \(deckInput != null\) deckInput\.setText\(store\.defaultDeck\(\)\);\n",
    r"^\s*if \(tagInput != null\) tagInput\.setText\(store\.defaultTags\(\)\);\n",
    r"^\s*if \(subjectInput != null\) subjectInput\.setText\(store\.subject\(\)\);\n",
    r"^\s*if \(autoSyncBox != null\) autoSyncBox\.setChecked\(store\.autoSync\(\)\);\n",
    r"^\s*store\.setDefaultDeck\(deckInput\.getText\(\)\.toString\(\)\);\n",
    r"^\s*store\.setDefaultTags\(tagInput\.getText\(\)\.toString\(\)\);\n",
    r"^\s*store\.setSubject\(subjectInput\.getText\(\)\.toString\(\)\);\n",
    r"^\s*if \(autoSyncBox != null\) store\.setAutoSync\(autoSyncBox\.isChecked\(\)\);\n",
):
    src = re.sub(pat, "", src, flags=re.M)

# ---------- 5) 标题与文案 ----------
src = src.replace('cfgCard.addView(heading("输出格式（config）"));', 'cfgCard.addView(heading("输出格式"));')
src = src.replace(
    'cfgTip.setText("决定 AI 按什么格式产出、卡片有哪些字段。默认那套是 A Level 数学/物理术语卡；"\n'
    '                + "也可以自己新建一套（自定字段与提示词）。");',
    'cfgTip.setText("控制 AI 的输出格式与卡片字段，并连带决定该格式使用的默认牌组、标签与学科背景。"\n'
    '                + "内置一套通用英语词汇格式；可按学科或考试另建格式，随时切换。");')

# ---------- 6) 编辑器加默认值三栏 ----------
editor_anchor = "        final EditText promptIn = new EditText(getContext());"
addition = '''        final EditText deckIn = new EditText(getContext());
        deckIn.setText(base.deckOr(store.defaultDeck()));
        box.addView(small("默认牌组"));
        box.addView(deckIn);

        final EditText tagsIn = new EditText(getContext());
        tagsIn.setText(base.tagsOr(store.defaultTags()));
        box.addView(small("默认标签"));
        box.addView(tagsIn);

        final EditText subjectIn = new EditText(getContext());
        subjectIn.setText(base.subjectOr(store.subject()));
        box.addView(small("学科背景（影响 AI 释义风格）"));
        box.addView(subjectIn);

'''
if "默认牌组" not in src.split("private void editConfig")[-1][:4000]:
    src = src.replace(editor_anchor, addition + editor_anchor, 1)
    print("  编辑器已加默认值三栏")

# 保存时写入这些值
src = src.replace("                        c.prompt = promptIn.getText().toString();",
                  "                        c.prompt = promptIn.getText().toString();\n"
                  "                        c.defaultDeck = deckIn.getText().toString().trim();\n"
                  "                        c.defaultTags = tagsIn.getText().toString().trim();\n"
                  "                        c.subject = subjectIn.getText().toString().trim();")

# 标题也用「名字」而不是「config 名字」
src = src.replace('nameIn.setHint("例如：雅思词汇");', 'nameIn.setHint("例如：雅思词汇、A Level 物理");')
src = src.replace('new AlertDialog.Builder(act)\n                .setTitle(creating ? "新建 config" : "编辑 config")',
                  'new AlertDialog.Builder(act)\n                .setTitle(creating ? "新建输出格式" : "编辑输出格式")')

io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("SettingsView 处理完成")
