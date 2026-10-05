"""把皮肤铺满：
   1) Theme 增加"面板色"，Ui 增加 PANEL / ON_ACCENT，WHITE 恢复为纯白
   2) Java 里所有写死的界面颜色改成主题色
   3) editor.html 改用 CSS 变量，由 Java 传入当前配色（深色/浅色、任意主色都能覆盖）
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")


def rd(name):
    return io.open(APP + "\\src\\com\\ankiassistant\\" + name, encoding="utf-8").read()


def wr(name, s):
    io.open(APP + "\\src\\com\\ankiassistant\\" + name, "w", encoding="utf-8", newline="\n").write(s)


# ---------- 1) Theme：加 panel ----------
s = rd("Theme.java")
if "public final int panel" not in s:
    s = s.replace("    public final int bg, card, ink, sub, line;",
                  "    public final int bg, card, panel, ink, sub, line;")
    s = s.replace("""    private Theme(String id, String name, boolean dark,
                  int bg, int card, int ink, int sub, int line,""",
                  """    private Theme(String id, String name, boolean dark,
                  int bg, int card, int panel, int ink, int sub, int line,""")
    s = s.replace("        this.bg = bg; this.card = card; this.ink = ink; this.sub = sub; this.line = line;",
                  "        this.bg = bg; this.card = card; this.panel = panel;\n"
                  "        this.ink = ink; this.sub = sub; this.line = line;")
    # 每个主题补一个 panel 值（紧跟 card 之后）
    s = s.replace('0xFFF3F5F9, 0xFFFFFFFF, 0xFF1B2432', '0xFFF3F5F9, 0xFFFFFFFF, 0xFFF6F9FE, 0xFF1B2432')
    s = s.replace('0xFFF1F6F2, 0xFFFFFFFF, 0xFF17241C', '0xFFF1F6F2, 0xFFFFFFFF, 0xFFF3F8F4, 0xFF17241C')
    s = s.replace('0xFFFAF6F1, 0xFFFFFFFF, 0xFF2A211A', '0xFFFAF6F1, 0xFFFFFFFF, 0xFFFBF6F0, 0xFF2A211A')
    s = s.replace('0xFFF6F4FB, 0xFFFFFFFF, 0xFF211B33', '0xFFF6F4FB, 0xFFFFFFFF, 0xFFF7F5FC, 0xFF211B33')
    s = s.replace('0xFF10131A, 0xFF181C25, 0xFFE9EEF7', '0xFF10131A, 0xFF181C25, 0xFF20252F, 0xFFE9EEF7')
    s = s.replace("        Ui.WHITE = dark ? 0xFF232936 : 0xFFFFFFFF;   // 深色下\"白\"其实是卡片上的浅色浮层\n",
                  "")
    s = s.replace("        Ui.WHITE = dark ? 0xFF232936 : 0xFFFFFFFF;   // 深色下“白”其实是卡片上的浅色浮层\n", "")
    s = s.replace("        Ui.TEXT_DIM = textDim;\n", "        Ui.TEXT_DIM = textDim;\n        Ui.PANEL = panel;\n        Ui.WHITE = 0xFFFFFFFF;\n")
    wr("Theme.java", s)
    print("Theme 已加 panel")

# ---------- 2) Ui：加 PANEL / alpha ----------
s = rd("Ui.java")
if "public static int PANEL" not in s:
    s = s.replace("    public static boolean DARK = false;",
                  "    public static boolean DARK = false;\n"
                  "    /** 次级面板色（索引栏、徽标、输入框底等） */\n"
                  "    public static int PANEL = 0xFFF6F9FE;")
if "public static int alpha(" not in s:
    s = s.replace("    public static int dim(int color, float f) {",
                  "    /** 给颜色加透明度（0..255） */\n"
                  "    public static int alpha(int color, int a) {\n"
                  "        return (color & 0x00FFFFFF) | ((a & 0xFF) << 24);\n"
                  "    }\n\n"
                  "    public static int dim(int color, float f) {")
wr("Ui.java", s)
print("Ui 已加 PANEL / alpha")

# ---------- 3) Java 写死颜色替换 ----------
repl = {
    "BrowseView.java": [
        ("Ui.round(0xFFEDF1F8, 11)", "Ui.round(Ui.PANEL, 11)"),
        ("on ? Ui.round(0xFFFFFFFF, 9)", "on ? Ui.round(Ui.CARD, 9)"),
        ("detailWeb.setBackgroundColor(0xFFFFFFFF);", "detailWeb.setBackgroundColor(Ui.CARD);"),
    ],
    "ChangelogView.java": [
        ("Ui.round(0xFFF4F6FA, 4)", "Ui.round(Ui.PANEL, 4)"),
        ("Ui.round(0xFFF4F6FA, 8)", "Ui.round(Ui.PANEL, 8)"),
    ],
    "CreateView.java": [
        ("Ui.round(0xFFF7F9FC, 10)", "Ui.round(Ui.PANEL, 10)"),
    ],
    "DialogUi.java": [
        ("Ui.roundStroke(0xFFF7F9FC, Ui.LINE, 12)", "Ui.roundStroke(Ui.PANEL, Ui.LINE, 12)"),
        ("root.setBackground(Ui.round(Ui.WHITE, 20));", "root.setBackground(Ui.round(Ui.CARD, 20));"),
    ],
    "MainActivity.java": [
        ("new int[]{0xFFFFFFFF, 0xFFF3F7FD});", "new int[]{Ui.CARD, Ui.PANEL});"),
        ("ver.setBackground(Ui.round(0xFFEEF2F9, 8));", "ver.setBackground(Ui.round(Ui.PANEL, 8));"),
        ("new int[]{0x663568E8, 0x003568E8}));", "new int[]{Ui.alpha(Ui.ACCENT, 0x66), Ui.alpha(Ui.ACCENT, 0x00)}));"),
        ("new int[]{0xFFF8FAFD, 0xFFF1F5FB}));", "new int[]{Ui.CARD, Ui.PANEL}));"),
        ("ankiDot.setBackground(Ui.round(0xFFB9C2D0, 5));", "ankiDot.setBackground(Ui.round(Ui.TEXT_DIM, 5));"),
        ("card.setBackground(Ui.round(0xFFF6F9FE, 16));", "card.setBackground(Ui.round(Ui.PANEL, 16));"),
        ('setSyncLamp(0xFFB9C2D0, "未登录");', 'setSyncLamp(Ui.TEXT_DIM, "未登录");'),
        ('setSyncLamp(0xFFF5A623, "待同步");', 'setSyncLamp(Ui.AMBER, "待同步");'),
        ('setSyncLamp(0xFF22A06B, when);', 'setSyncLamp(Ui.GREEN, when);'),
        ("color == 0xFF22A06B ? color : Ui.TEXT_DIM", "color == Ui.GREEN ? color : Ui.TEXT_DIM"),
    ],
    "SettingsView.java": [
        ("indexBox.setBackgroundColor(0xFFF4F7FC);", "indexBox.setBackgroundColor(Ui.PANEL);"),
        ("on ? Ui.round(0xFFFFFFFF, 9) : null", "on ? Ui.round(Ui.CARD, 9) : null"),
    ],
    "IconDrawable.java": [
        ("p.setColor(0xFFFFFFFF);", "p.setColor(color);"),
    ],
}
for name, pairs in repl.items():
    s = rd(name)
    for a, b in pairs:
        if a in s:
            s = s.replace(a, b)
        else:
            print("  !! %s 未匹配: %s" % (name, a[:50]))
    wr(name, s)
    print("  已处理 " + name)

print("原生配色替换完成")
