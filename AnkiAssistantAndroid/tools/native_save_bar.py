"""制卡页：把「保存到 Anki / 存草稿」从网页里挪到原生底部固定栏，
   这样网页不必再撑出一块空白，按钮也始终看得见。
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) editor.html：支持隐藏网页内的保存按钮 ----------------
p = APP + r"\assets\editor.html"
h = io.open(p, encoding="utf-8").read()
if "setNativeActions" not in h:
    h = h.replace("/* ---------------- 工具栏 ---------------- */",
'''/* 保存按钮搬到原生底栏时，把网页里的那一行藏掉（避免出现两套按钮） */
body.native-actions #metaPane .mrow.actions { display: none; }

/* ---------------- 工具栏 ---------------- */''', 1)
    h = h.replace("/* 皮肤：Java 侧在建好页面后调用，传入当前主题的取色 */",
'''/* Java 侧：保存按钮是否放在原生底栏 */
function setNativeActions(on) {
  if (on) document.body.classList.add('native-actions');
  else document.body.classList.remove('native-actions');
}

/* 皮肤：Java 侧在建好页面后调用，传入当前主题的取色 */''', 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(h)
    print("editor.html 已支持隐藏网页内保存按钮")

# ---------------- 2) CreateView：加原生底栏 ----------------
p2 = APP + r"\src\com\ankiassistant\CreateView.java"
s = io.open(p2, encoding="utf-8").read()

# 2a) 页面就绪时告诉网页"按钮在原生栏"
s = s.replace("                editorReady = true;\n                pushTheme();",
              "                editorReady = true;\n                pushTheme();\n                js(\"setNativeActions(true)\");", 1)

# 2b) 在编辑器容器之后加原生底栏
anchor = "        editorWrap.addView(editor, new LinearLayout.LayoutParams(\n                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));"
if anchor in s and "nativeActions" not in s:
    row = anchor + '''

        // 保存按钮固定在底部：网页只需按内容高度显示，就不会在底部留出一块空白
        nativeActions = new LinearLayout(getContext());
        nativeActions.setOrientation(LinearLayout.HORIZONTAL);
        nativeActions.setPadding(Ui.dp(12), Ui.dp(8), Ui.dp(12), Ui.dp(10));
        nativeActions.setBackgroundColor(Ui.BG);

        Button saveBtn = new Button(getContext());
        saveBtn.setText("保存到 Anki");
        Ui.primary(saveBtn);
        saveBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { js("doSave(false)"); }
        });
        nativeActions.addView(saveBtn, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1.35f));

        Button draftBtn = new Button(getContext());
        draftBtn.setText("存草稿");
        Ui.secondary(draftBtn);
        draftBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { js("doSave(true)"); }
        });
        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        dlp.leftMargin = Ui.dp(8);
        nativeActions.addView(draftBtn, dlp);

        addView(nativeActions, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));'''
    s = s.replace(anchor, row, 1)
    s = s.replace("    private boolean editorReady;", "    private boolean editorReady;\n    private LinearLayout nativeActions;")
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s)
    print("CreateView 原生底栏已加")

# 2c) 手机端浏览器页不再需要撑满：仍保持 weight=1（内容短时由网页自身留白），但去掉多余底边距
s = io.open(p2, encoding="utf-8").read()
s = s.replace('js("setSaving(true)")', 'js("setSaving(true)")')   # 占位，保持幂等
io.open(p2, "w", encoding="utf-8", newline="\n").write(s)
