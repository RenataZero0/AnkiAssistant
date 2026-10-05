"""浏览页这一轮的三处修改：
   1) 顶部留白 + 分段控件改成扁平分段样式（不再用两个大 Button 挤在一起）
   2) MathJax 资源路径修正（资源服务器是 mathjax/tex-mml-chtml.js）
   3) 详情里兼容老卡片：去掉 <inline_latex_formula> 这类自定义包壳，公式才会被 MathJax 渲染
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\BrowseView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# ---------- 1) 分段控件 ----------
old_seg = '''    private void build() {
        LinearLayout seg = new LinearLayout(getContext());
        seg.setOrientation(HORIZONTAL);
        seg.setPadding(0, 0, 0, Ui.dp(12));

        segCards = new Button(getContext());
        segDraft = new Button(getContext());
        segCards.setText("本机卡片");
        segDraft.setText("本地草稿");
        seg.addView(segCards, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        slp.leftMargin = Ui.dp(8);
        segDraft.setLayoutParams(slp);
        seg.addView(segDraft);
        addView(seg);
        segCards.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(0); }
        });
        segDraft.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(1); }
        });
'''
new_seg = '''    private void build() {
        // 页面自身留白：顶栏下面本来太空，加上内边距才不显得贴在一起
        setPadding(Ui.dp(12), Ui.dp(14), Ui.dp(12), Ui.dp(12));

        // 分段控件：一条圆角灰底 + 两个等宽扁平标签（选中的是白底蓝字）
        LinearLayout seg = new LinearLayout(getContext());
        seg.setOrientation(HORIZONTAL);
        seg.setBackground(Ui.round(0xFFEDF1F8, 11));
        seg.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        LinearLayout.LayoutParams segLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        segLp.bottomMargin = Ui.dp(14);
        addView(seg, segLp);

        segCards = new Button(getContext());
        segDraft = new Button(getContext());
        segCards.setText("本机卡片");
        segDraft.setText("本地草稿");
        seg.addView(segCards, new LinearLayout.LayoutParams(0, Ui.dp(40), 1f));
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0, Ui.dp(40), 1f);
        slp.leftMargin = Ui.dp(4);
        segDraft.setLayoutParams(slp);
        seg.addView(segDraft);
        segCards.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(0); }
        });
        segDraft.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(1); }
        });
'''
if old_seg in s:
    s = s.replace(old_seg, new_seg, 1)
    print("分段控件与留白已改")
else:
    print("!! 分段控件未匹配")

# ---------- tab 高亮样式（扁平分段） ----------
old_tab = '''        Ui.primary(segCards);
        Ui.secondary(segDraft);
        if (which == 0) { Ui.primary(segCards); Ui.secondary(segDraft); }
        else { Ui.primary(segDraft); Ui.secondary(segCards); }'''
new_tab = '''        styleSeg(segCards, which == 0);
        styleSeg(segDraft, which == 1);'''
if old_tab in s:
    s = s.replace(old_tab, new_tab, 1)
    print("分段高亮逻辑已改")

# 新增 styleSeg（插在 col() 前）
helper = '''
    /** 扁平分段标签：选中=白底蓝字，未选中=透明灰字 */
    private void styleSeg(Button b, boolean on) {
        b.setTextColor(on ? Ui.ACCENT : Ui.SUB);
        b.setTextSize(14);
        b.setTypeface(on ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        b.setAllCaps(false);
        b.setStateListAnimator(null);
        b.setElevation(0);
        b.setBackground(on ? Ui.round(0xFFFFFFFF, 9) : Ui.round(0x00000000, 9));
        b.setPadding(0, 0, 0, 0);
    }

'''
if "private void styleSeg(" not in s:
    s = s.replace("    private LinearLayout col() {", helper + "    private LinearLayout col() {", 1)
    print("styleSeg 已加")

# ---------- 2) MathJax 路径 ----------
s = s.replace('base + "mj-tex-mml-chtml.js"', 'base + "mathjax/tex-mml-chtml.js"')

# ---------- 3) 兼容老卡片的自定义包壳 + 明文公式字段 ----------
old_val = '''                body.append("<div class=\\"fld\\"><span class=\\"lab\\">【").append(esc(k))
                    .append("】</span><span class=\\"val\\">")
                    .append(v == null ? "" : v)     // 字段本身就是 HTML，按原样渲染
                    .append("</span></div>\\n");'''
new_val = '''                body.append("<div class=\\"fld\\"><span class=\\"lab\\">【").append(esc(k))
                    .append("】</span><span class=\\"val\\">")
                    .append(sanitizeField(v))       // 字段本身是 HTML，按原样渲染（只去掉老版自定义包壳）
                    .append("</span></div>\\n");'''
if old_val in s:
    s = s.replace(old_val, new_val, 1)
    print("字段渲染改为 sanitizeField")

sanitizer = '''
    /**
     * 老版本把整块内容包在 &lt;inline_latex_formula&gt; 里，MathJax 认不出里面的 \\\\(…\\\\)，
     * 于是公式显示成裸文本。这里把这类自定义包壳去掉（保留内部 HTML），公式就能正常渲染。
     */
    private static String sanitizeField(String v) {
        if (v == null) return "";
        String s = v;
        s = s.replaceAll("(?i)</?inline_latex_formula[^>]*>", "");
        s = s.replaceAll("(?i)</?(anki-mathjax|latex_formula|mathjax_formula)[^>]*>", "");
        return s;
    }

'''
if "sanitizeField" not in s.split("buildDetailHtml")[0]:
    s = s.replace("    /** 把这条笔记的所有字段渲染成一页 HTML（字段名 + 值），适配任意笔记类型 */",
                  sanitizer + "    /** 把这条笔记的所有字段渲染成一页 HTML（字段名 + 值），适配任意笔记类型 */", 1)
    print("sanitizeField 已加")

# ---------- 详情标题区也给点留白 ----------
s = s.replace('tip.setPadding(0, Ui.dp(8), 0, Ui.dp(4));', 'tip.setPadding(0, Ui.dp(9), 0, Ui.dp(6));')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("BrowseView 处理完成")
