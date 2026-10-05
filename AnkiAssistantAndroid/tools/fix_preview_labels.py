"""预览里的「正面 / 背面」也改成跟随输出格式：
   正面 → 当前格式第一个字段名；背面 → "其余字段"（不再用"正面/背面"的说法）
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# 1) editor.html：暴露设置标签的接口
p = APP + r"\assets\editor.html"
s = io.open(p, encoding="utf-8").read()
if "function setCardLabels" not in s:
    s = s.replace("/* Java 侧：保存按钮是否放在原生底栏 */",
'''/* Java 侧：卡片预览的两个标签（正面字段名 / 其余字段） */
function setCardLabels(front, back) {
  var f = document.querySelector('#cardFront .cardlabel');
  var b = document.querySelector('#cardBack .cardlabel');
  if (f && front) f.textContent = front;
  if (b && back) b.textContent = back;
}

/* Java 侧：保存按钮是否放在原生底栏 */''', 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("editor.html 已加 setCardLabels")

# 2) CreateView：切格式/建好页面时推标签
p2 = APP + r"\src\com\ankiassistant\CreateView.java"
s2 = io.open(p2, encoding="utf-8").read()
if "pushCardLabels" not in s2:
    helper = '''
    /** 预览里的两个标签：正面用当前格式的第一个字段名，背面不再叫"背面" */
    private void pushCardLabels() {
        String front = "词";
        try {
            CardConfig cfg = store.activeConfig();
            if (cfg != null && cfg.fields != null && cfg.fields.size() > 0
                    && cfg.fields.get(0).name != null && cfg.fields.get(0).name.length() > 0) {
                front = cfg.fields.get(0).name;
            }
        } catch (Exception ignored) { }
        js("setCardLabels(" + JSONObject.quote(front) + "," + JSONObject.quote("其余字段") + ")");
    }
'''
    s2 = s2.replace("    private void pushWord(String word) {", helper + "\n    private void pushWord(String word) {", 1)
    # 页面就绪时 + 切换格式时都推
    s2 = s2.replace("                pushMeta();\n                pushWord(wordInput.getText().toString());",
                    "                pushMeta();\n                pushCardLabels();\n                pushWord(wordInput.getText().toString());")
    s2 = s2.replace("    public void refreshConfig() {\n        refreshWordLabel();\n        pushConfig();",
                    "    public void refreshConfig() {\n        refreshWordLabel();\n        pushConfig();\n        pushCardLabels();")
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("CreateView 已推送预览标签")
