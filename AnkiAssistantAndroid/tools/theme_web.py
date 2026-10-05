"""网页侧（editor.html）也接入皮肤：
   · CSS 全部改用变量（--bg/--card/--accent/...），深色不再靠一堆 body.dark 覆盖
   · Java 侧 setTheme(json) 把当前主题的取色灌进变量，任何主色都能生效
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) Theme：给网页用的变量 ----------------
p = APP + r"\src\com\ankiassistant\Theme.java"
s = io.open(p, encoding="utf-8").read()
if "webVars()" not in s:
    add = '''
    /** 给网页（editor.html）用的 CSS 变量，JSON 字符串；由 CreateView 推进去 */
    public String webVars() {
        int r = (ink >> 16) & 0xFF, g = (ink >> 8) & 0xFF, b = ink & 0xFF;
        StringBuilder sb = new StringBuilder();
        sb.append('{');
        sb.append("\\"bg\\":\\"").append(hex(bg)).append("\\",");
        sb.append("\\"card\\":\\"").append(hex(card)).append("\\",");
        sb.append("\\"panel\\":\\"").append(hex(panel)).append("\\",");
        sb.append("\\"ink\\":\\"").append(hex(ink)).append("\\",");
        sb.append("\\"body\\":\\"").append(hex(textBody)).append("\\",");
        sb.append("\\"sub\\":\\"").append(hex(sub)).append("\\",");
        sb.append("\\"dim\\":\\"").append(hex(textDim)).append("\\",");
        sb.append("\\"line\\":\\"").append(hex(line)).append("\\",");
        sb.append("\\"accent\\":\\"").append(hex(accent)).append("\\",");
        sb.append("\\"accentSoft\\":\\"").append(hex(accentSoft)).append("\\",");
        sb.append("\\"pressed\\":\\"").append(hex(accentSoft)).append("\\",");
        sb.append("\\"amber\\":\\"").append(hex(amber)).append("\\",");
        sb.append("\\"shadow\\":\\"rgba(").append(r).append(',').append(g).append(',')
          .append(b).append(",.22)\\"");
        sb.append('}');
        return sb.toString();
    }

    private static String hex(int c) {
        return String.format("#%06X", c & 0xFFFFFF);
    }
'''
    s = s.replace("    public static Theme apply(Activity act, Store store) {", add + "\n    public static Theme apply(Activity act, Store store) {")
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("Theme.webVars 已加")

# ---------------- 2) editor.html：变量化 ----------------
p2 = APP + r"\assets\editor.html"
h = io.open(p2, encoding="utf-8").read()

# 2a) 插入 :root 变量
root_css = """  /* ---------- 皮肤变量：Java 侧 setTheme() 会覆盖这些值 ---------- */
  :root {
    --bg: #f3f5f9; --card: #ffffff; --panel: #f7f9fc; --ink: #1b2432;
    --body: #3c4a60; --sub: #71809a; --dim: #9aa6b8;
    --line: #e5e9f0; --line2: #dbe3ee;
    --accent: #3568e8; --accent-soft: #e8effe; --pressed: #dde7fb;
    --amber: #de9420; --shadow: rgba(27,36,50,.18);
  }
"""
if ":root {" not in h:
    h = h.replace("  * { box-sizing: border-box;", root_css + "  * { box-sizing: border-box;", 1)

# 2b) 颜色 → 变量（只替换 background 上的白，避免把"蓝底白字"也换掉）
subs = [
    ("#f3f5f9", "var(--bg)"),
    ("#1b2432", "var(--ink)"),
    ("#71809a", "var(--sub)"),
    ("#3c4a60", "var(--body)"),
    ("#9aa6b8", "var(--dim)"),
    ("#e5e9f0", "var(--line)"),
    ("#dbe3ee", "var(--line2)"),
    ("#3568e8", "var(--accent)"),
    ("#e8effe", "var(--accent-soft)"),
    ("#c9d8f8", "var(--accent-soft)"),
    ("#dde7fb", "var(--pressed)"),
    ("#f7f9fc", "var(--panel)"),
    ("#de9420", "var(--amber)"),
    ("rgba(27,36,50,.18)", "var(--shadow)"),
]
for a, b in subs:
    h = h.replace(a, b)
h = h.replace("background: #fff;", "background: var(--card);")
h = h.replace("background:#fff;", "background: var(--card);")

# 2c) 删掉 body.dark 那一堆覆盖（变量已经管了）
h = re.sub(r"(?s)/\* 深色皮肤：只改网页部分的底色与文字，卡片预览跟着一起变 \*/\n(?:body\.dark[^\n]*\n)+", "", h)

# 2d) setTheme 改成设置变量
h = re.sub(r"(?s)/\* 皮肤：Java 侧在建好页面后调用（true = 深色） \*/\nfunction setTheme\(isDark\) \{.*?\n\}\n",
'''/* 皮肤：Java 侧在建好页面后调用，传入当前主题的取色 */
function setTheme(t) {
  if (!t) return;
  var r = document.documentElement.style;
  var map = {
    '--bg': t.bg, '--card': t.card, '--panel': t.panel, '--ink': t.ink,
    '--body': t.body, '--sub': t.sub, '--dim': t.dim,
    '--line': t.line, '--line2': t.line, '--accent': t.accent,
    '--accent-soft': t.accentSoft, '--pressed': t.accentSoft,
    '--amber': t.amber, '--shadow': t.shadow
  };
  for (var k in map) {
    if (map[k]) r.setProperty(k, map[k]);
  }
}
''', h, count=1)

io.open(p2, "w", encoding="utf-8", newline="\n").write(h)
print("editor.html 变量化完成；剩余写死颜色：")
left = re.findall(r"#[0-9a-fA-F]{6}", h)
from collections import Counter
for c, n in Counter(left).most_common(8):
    print("   %s × %d" % (c, n))

# ---------------- 3) CreateView：把变量 JSON 推过去 ----------------
p3 = APP + r"\src\com\ankiassistant\CreateView.java"
s3 = io.open(p3, encoding="utf-8").read()
s3 = s3.replace('js("setTheme(" + (Theme.byId(store.theme()).dark ? "true" : "false") + ")");',
                'js("setTheme(" + Theme.byId(store.theme()).webVars() + ")");')
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("CreateView 已改为推送主题变量")
