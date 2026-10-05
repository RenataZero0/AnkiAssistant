"""设置页左侧索引：
   · 宽屏（>=600dp，平板）：左边一列索引，右边原来的滚动内容
   · 窄屏（手机）：内容上方一排横向可滑动的索引标签
   · 点索引跳到对应卡片；滚动时高亮当前所在卡片
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
path = APP + r"\src\com\ankiassistant\SettingsView.java"
sys.stdout.reconfigure(encoding="utf-8")
src = io.open(path, encoding="utf-8").read()

# ---------- 1) build() 开头：改成 左索引 + 右滚动 ----------
old_head = '''    private void build() {
        ScrollView scroll = new ScrollView(getContext());
        scroll.setVerticalScrollBarEnabled(false);
        addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
'''
new_head = '''    private void build() {
        boolean wide = getResources().getConfiguration().smallestScreenWidthDp >= 600;

        // 外层：宽屏时左右分栏（左索引 / 右内容），窄屏时上下（上标签 / 下内容）
        LinearLayout outer = new LinearLayout(getContext());
        outer.setOrientation(wide ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        addView(outer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        indexBox = new LinearLayout(getContext());
        indexBox.setOrientation(wide ? LinearLayout.VERTICAL : LinearLayout.HORIZONTAL);
        indexBox.setBackgroundColor(0xFFF4F7FC);
        if (wide) {
            indexBox.setPadding(Ui.dp(6), Ui.dp(14), Ui.dp(6), Ui.dp(10));
            outer.addView(indexBox, new LinearLayout.LayoutParams(Ui.dp(126),
                    ViewGroup.LayoutParams.MATCH_PARENT));
        } else {
            indexBox.setPadding(Ui.dp(8), Ui.dp(8), Ui.dp(8), Ui.dp(8));
            ScrollView ix = new ScrollView(getContext());
            ix.setHorizontalScrollBarEnabled(false);
            ix.addView(indexBox, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            outer.addView(ix, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        }

        scroll = new ScrollView(getContext());
        scroll.setVerticalScrollBarEnabled(false);
        outer.addView(scroll, new LinearLayout.LayoutParams(
                wide ? 0 : ViewGroup.LayoutParams.MATCH_PARENT,
                wide ? ViewGroup.LayoutParams.MATCH_PARENT : 0,
                wide ? 1f : 1f));
'''
if old_head not in src:
    print("!! build() 头部未匹配")
else:
    src = src.replace(old_head, new_head, 1)
    print("分栏结构已插入")

# ---------- 2) 每张卡片登记到索引 ----------
# AI 卡片
src = src.replace('''        // ================= AI =================
        LinearLayout ai = card();''',
'''        // ================= AI =================
        LinearLayout ai = card();''')
# 在 ai / cfgCard / upd 建好后登记（利用已有的变量名）
src = src.replace('''        LinearLayout.LayoutParams aiLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aiLp.bottomMargin = Ui.dp(11);
        col.addView(ai, aiLp);''',
'''        LinearLayout.LayoutParams aiLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aiLp.bottomMargin = Ui.dp(11);
        col.addView(ai, aiLp);
        addIndex("AI 自动填充", ai);''')

src = src.replace('''        cfgCardLp.bottomMargin = Ui.dp(11);
        col.addView(cfgCard, cfgCardLp);''',
'''        cfgCardLp.bottomMargin = Ui.dp(11);
        col.addView(cfgCard, cfgCardLp);
        addIndex("输出格式", cfgCard);''')

src = src.replace('''        updLp.bottomMargin = Ui.dp(11);
        col.addView(upd, updLp);''',
'''        updLp.bottomMargin = Ui.dp(11);
        col.addView(upd, updLp);
        addIndex("更新内容", upd);''')

# ---------- 3) 字段 + addIndex + 高亮 ----------
if "private void addIndex(" not in src:
    helper = '''
    // ------------------------------------------------------------------ 左侧索引

    private final java.util.List<View> indexItems = new java.util.ArrayList<View>();
    private final java.util.List<View> indexTargets = new java.util.ArrayList<View>();

    private void addIndex(String title, final View target) {
        if (indexBox == null) return;
        final TextView item = new TextView(getContext());
        item.setText(title);
        item.setTextSize(13);
        item.setGravity(android.view.Gravity.CENTER_VERTICAL);
        item.setPadding(Ui.dp(12), Ui.dp(11), Ui.dp(8), Ui.dp(11));
        item.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (scroll != null) scroll.smoothScrollTo(0, Math.max(0, target.getTop() - Ui.dp(6)));
                styleIndex(indexItems.indexOf(item));
            }
        });
        indexItems.add(item);
        indexTargets.add(target);
        indexBox.addView(item, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        if (indexItems.size() == 1) styleIndex(0);
    }

    /** 只让选中的那一条是高亮蓝底 */
    private void styleIndex(int active) {
        for (int i = 0; i < indexItems.size(); i++) {
            TextView t = (TextView) indexItems.get(i);
            boolean on = (i == active);
            t.setTextColor(on ? Ui.ACCENT : Ui.TEXT_BODY);
            t.setTypeface(on ? android.graphics.Typeface.DEFAULT_BOLD
                    : android.graphics.Typeface.DEFAULT);
            t.setBackground(on ? Ui.round(0xFFFFFFFF, 9) : null);
        }
    }

    /** 滚动时跟着高亮当前卡片 */
    private void attachScrollSpy() {
        if (scroll == null || indexTargets.isEmpty()) return;
        scroll.getViewTreeObserver().addOnScrollChangedListener(
                new android.view.ViewTreeObserver.OnScrollChangedListener() {
            @Override public void onScrollChanged() {
                int y = scroll.getScrollY() + Ui.dp(40);
                int active = 0;
                for (int i = 0; i < indexTargets.size(); i++) {
                    if (indexTargets.get(i).getTop() <= y) active = i;
                }
                styleIndex(active);
            }
        });
    }
'''
    anchor = "    private void loadValues() {"
    src = src.replace(anchor, helper + "\n" + anchor, 1)
    print("索引辅助方法已插入")

# 字段声明
if "private ScrollView scroll;" not in src:
    src = src.replace("    private TextView heading(String text) {",
                      "    private ScrollView scroll;\n    private LinearLayout indexBox;\n\n    private TextView heading(String text) {", 1)

io.open(path, "w", encoding="utf-8", newline="\n").write(src)
print("SettingsView 索引处理完成")
