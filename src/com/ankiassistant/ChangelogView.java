package com.ankiassistant;

import android.app.AlertDialog;
import android.app.Dialog;
import android.content.Context;
import android.graphics.Typeface;
import android.text.SpannableString;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.text.style.StyleSpan;
import android.text.style.TypefaceSpan;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.Button;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 更新日志阅读器（自绘 Markdown，不依赖 WebView，也不需要联网）。
 *
 * 支持：# / ## / ### 标题、- 与 * 项目符号、&gt; 引用、``` 代码块、| 表格、
 *      **粗体** 与 `等宽` 行内格式。
 * 顶部一排版本标签，点一下直接跳到对应版本。
 *
 * 用法：ChangelogView.show(activity)
 */
public class ChangelogView {

    private ChangelogView() { }

    /**
     * 和 StudyCompanion 完全一致：先把 GitHub 上的最新日志拉下来（成功就写进缓存），
     * 拉不到就用缓存 / APK 内置副本，并在标题下写明来源与原因。
     */
    public static void show(final android.app.Activity act) {
        final Dialog busy = new DialogUi.Builder(act)
                .title("更新内容")
                .message("正在获取最新更新日志…")
                .show();
        busy.setCancelable(false);
        Th.bg(new Runnable() {
            @Override public void run() {
                final String[] err = new String[1];
                final boolean ok = Changelog.fetch(act, err);
                Th.ui(new Runnable() {
                    @Override public void run() {
                        try { busy.dismiss(); } catch (Exception ignored) { }
                        showNow(act, ok ? "" : (err[0] == null ? "拉取失败" : err[0]));
                    }
                });
            }
        });
    }

    private static void showNow(final android.app.Activity act, String fetchError) {
        String md = Changelog.local(act);
        if (md == null || md.trim().length() == 0) {
            new DialogUi.Builder(act)
                    .title("更新日志")
                    .message("APK 里没有找到 CHANGELOG.md（构建时没打包进去？）")
                    .positive("知道了", null)
                    .show();
            return;
        }

        LinearLayout root = new LinearLayout(act);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackground(Ui.round(Ui.CARD, 20));

        // ---- 头部：当前版本 ----
        LinearLayout head = new LinearLayout(act);
        head.setOrientation(LinearLayout.VERTICAL);
        head.setBackground(Ui.roundTop(Ui.ACCENT_SOFT, 20));
        head.setPadding(Ui.dp(18), Ui.dp(14), Ui.dp(18), Ui.dp(12));

        TextView title = new TextView(act);
        title.setText("更新内容");
        title.setTextSize(19);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextColor(Ui.INK);
        head.addView(title);

        TextView sub = new TextView(act);
        String src = Changelog.sourceLabel(act);
        if (fetchError != null && fetchError.length() > 0) src += "　·　在线更新失败：" + fetchError;
        sub.setText("当前版本 " + Version.VERSION_TAG + "　·　" + src);
        sub.setTextSize(12);
        sub.setTextColor(Ui.SUB);
        sub.setPadding(0, Ui.dp(4), 0, 0);
        head.addView(sub);
        root.addView(head);

        // ---- 版本标签 ----
        final List<String> versions = versions(md);
        final List<View> anchors = new ArrayList<View>();
        final List<TextView> chips = new ArrayList<TextView>();
        final int[] activeIdx = new int[]{0};
        final boolean[] programmatic = new boolean[]{false};
        final ScrollView scroll = new ScrollView(act);
        scroll.setBackgroundColor(Ui.CARD);

        if (versions.size() > 0) {
            HorizontalScrollView chipsBar = new HorizontalScrollView(act);
            chipsBar.setHorizontalScrollBarEnabled(false);
            chipsBar.setBackgroundColor(Ui.BG);
            final LinearLayout chipRow = new LinearLayout(act);
            chipRow.setOrientation(LinearLayout.HORIZONTAL);
            chipRow.setPadding(Ui.dp(12), Ui.dp(8), Ui.dp(12), Ui.dp(8));
            for (int i = 0; i < versions.size(); i++) {
                final int idx = i;
                final String v = versions.get(i);
                TextView chip = new TextView(act);
                chip.setText("v" + v);
                chip.setTextSize(12.5f);
                chip.setGravity(Gravity.CENTER);
                chip.setPadding(Ui.dp(11), Ui.dp(5), Ui.dp(11), Ui.dp(5));
                chip.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View clicked) {
                        styleChips(chips, idx);
                        activeIdx[0] = idx;
                        if (idx < anchors.size()) {
                            final View target = anchors.get(idx);
                            programmatic[0] = true;
                            scroll.post(new Runnable() {
                                @Override public void run() {
                                    scroll.smoothScrollTo(0, Math.max(0, target.getTop() - Ui.dp(6)));
                                }
                            });
                            // 平滑滚动期间不让"滚动联动"抢走高亮，滚完再交还
                            scroll.postDelayed(new Runnable() {
                                @Override public void run() { programmatic[0] = false; }
                            }, 700);
                        }
                    }
                });
                chips.add(chip);
                LinearLayout.LayoutParams clp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                clp.rightMargin = Ui.dp(7);
                chipRow.addView(chip, clp);
            }
            chipsBar.addView(chipRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            root.addView(chipsBar, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        }

        // ---- 正文 ----
        LinearLayout body = new LinearLayout(act);
        body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(Ui.dp(18), Ui.dp(6), Ui.dp(18), Ui.dp(22));
        render(act, md, body, versions, anchors);
        scroll.addView(body, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        root.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        // 初始高亮最新版本
        if (chips.size() > 0) styleChips(chips, 0);

        // 滚动联动：手动滚动时，高亮跟着"当前看到的版本"走
        scroll.getViewTreeObserver().addOnScrollChangedListener(
                new android.view.ViewTreeObserver.OnScrollChangedListener() {
                    @Override public void onScrollChanged() {
                        if (programmatic[0] || anchors.isEmpty() || chips.isEmpty()) return;
                        int y = scroll.getScrollY();
                        int idx = 0;
                        for (int i = 0; i < anchors.size(); i++) {
                            if (anchors.get(i).getTop() - Ui.dp(10) <= y) idx = i; else break;
                        }
                        if (idx != activeIdx[0]) {
                            activeIdx[0] = idx;
                            styleChips(chips, idx);
                        }
                    }
                });

        // ---- 底部关闭 ----
        Button close = new Button(act);
        close.setText("关闭");
        Ui.secondary(close);
        close.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { /* 由下面的 dialog 引用接管 */ }
        });
        LinearLayout footer = new LinearLayout(act);
        footer.setPadding(Ui.dp(18), Ui.dp(10), Ui.dp(18), Ui.dp(14));
        footer.setBackgroundColor(Ui.CARD);
        footer.addView(close, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        root.addView(footer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        final Dialog[] holder = new Dialog[1];
        close.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (holder[0] != null) holder[0].dismiss();
            }
        });
        Dialog made = new DialogUi.Builder(act).content(root).wide().show();
        holder[0] = made;
        Window w = made.getWindow();
        if (w != null) {
            DisplayMetrics dm = act.getResources().getDisplayMetrics();
            w.setLayout((int) (dm.widthPixels * 0.92), (int) (dm.heightPixels * 0.86));
        }
    }

    // ------------------------------------------------------------ 可复用的 Markdown 视图

    /**
     * 把一段 Markdown 渲染成原生视图（可滚动）。更新日志阅读器与「发现新版本」弹窗共用这一套，
     * 避免把 `## / - / **` 这些记号以纯文本形式丢给用户看。
     *
     * @param paddingDp 左右内边距
     */
    public static View markdownScroll(Context c, String md, int paddingDp) {
        ScrollView sv = new ScrollView(c);
        sv.setBackgroundColor(Ui.CARD);
        LinearLayout box = new LinearLayout(c);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(Ui.dp(paddingDp), Ui.dp(4), Ui.dp(paddingDp), Ui.dp(12));
        render(c, md == null ? "" : md, box, new ArrayList<String>(), new ArrayList<View>());
        sv.addView(box, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return sv;
    }

    // ------------------------------------------------------------ 解析

    /** 只让选中的那个版本标签是蓝底白字，其余恢复成白底灰字 */
    private static void styleChips(List<TextView> chips, int active) {
        for (int i = 0; i < chips.size(); i++) {
            TextView c = chips.get(i);
            boolean on = (i == active);
            c.setTextColor(on ? Ui.WHITE : Ui.SUB);
            c.setTypeface(on ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
            c.setBackground(on ? Ui.press(Ui.ACCENT, 9) : Ui.press(Ui.WHITE, 9));
        }
    }

    private static List<String> versions(String md) {
        List<String> out = new ArrayList<String>();
        Matcher m = Pattern.compile("^##\\s*v([0-9][0-9.]*)", Pattern.MULTILINE).matcher(md);
        while (m.find()) out.add(m.group(1));
        return out;
    }

    /** 逐行把 Markdown 变成原生视图 */
    private static void render(Context c, String md, LinearLayout box,
                               List<String> versions, List<View> anchors) {
        String[] lines = md.replace("\r\n", "\n").split("\n", -1);
        boolean code = false;
        int vi = 0;
        for (int i = 0; i < lines.length; i++) {
            String raw = lines[i];
            String t = raw.trim();

            if (t.startsWith("```")) {           // 代码块开关
                code = !code;
                continue;
            }
            if (code) {
                box.addView(codeLine(c, raw), matchWrap());
                continue;
            }
            if (t.length() == 0) {
                box.addView(space(c, 7));
                continue;
            }
            // 水平分隔线 --- / *** / ___
            if (t.equals("---") || t.equals("***") || t.equals("___")) {
                View hr = new View(c);
                hr.setBackgroundColor(Ui.LINE);
                LinearLayout.LayoutParams hlp = matchWrap();
                hlp.height = Ui.dp(1);
                hlp.topMargin = Ui.dp(10);
                hlp.bottomMargin = Ui.dp(4);
                box.addView(hr, hlp);
                continue;
            }
            // 缩进续行（列表项后面的续写）：对齐到项目符号文字的位置
            if (raw.startsWith("  ") && t.length() > 0) {
                TextView cont = inline(c, t, 13.5f, Ui.TEXT_BODY);
                cont.setPadding(Ui.dp(22), Ui.dp(2), 0, Ui.dp(2));
                box.addView(cont, matchWrap());
                continue;
            }
            // 表格分隔行 |---|---| 直接跳过
            if (t.startsWith("|") && t.replace("|", "").replace("-", "").replace(":", "")
                    .replace(" ", "").length() == 0) {
                continue;
            }
            if (t.startsWith("|")) {
                box.addView(tableLine(c, t), matchWrap());
                continue;
            }
            if (t.startsWith("### ")) {
                box.addView(heading(c, t.substring(4), 14.5f, Ui.INK, Ui.dp(13)), matchWrap());
                continue;
            }
            if (t.startsWith("## ")) {
                String text = t.substring(3);
                View v = versionHeading(c, text);
                anchors.add(v);
                vi++;
                box.addView(v, matchWrap());
                continue;
            }
            if (t.startsWith("# ")) {
                box.addView(heading(c, t.substring(2), 17.5f, Ui.INK, Ui.dp(6)), matchWrap());
                continue;
            }
            if (t.startsWith("> ")) {
                TextView q = base(c, t.substring(2), 13f, Ui.SUB);
                q.setPadding(Ui.dp(12), Ui.dp(4), 0, Ui.dp(4));
                q.setBackground(leftBar(c));
                box.addView(q, matchWrap());
                continue;
            }
            if (t.startsWith("- ") || t.startsWith("* ")) {
                LinearLayout row = new LinearLayout(c);
                row.setOrientation(LinearLayout.HORIZONTAL);
                row.setPadding(Ui.dp(4), Ui.dp(2), 0, Ui.dp(2));

                TextView dot = base(c, "•", 13.5f, Ui.ACCENT);
                dot.setPadding(0, 0, Ui.dp(8), 0);
                row.addView(dot, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

                TextView txt = inline(c, t.substring(2), 13.5f, Ui.TEXT_BODY);
                row.addView(txt, new LinearLayout.LayoutParams(
                        0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                box.addView(row, matchWrap());
                continue;
            }
            box.addView(inline(c, t, 13.5f, Ui.TEXT_BODY), matchWrap());
        }
    }

    private static LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private static View space(Context c, int heightDp) {
        View v = new View(c);
        v.setLayoutParams(new LinearLayout.LayoutParams(1, Ui.dp(heightDp)));
        return v;
    }

    private static TextView base(Context c, String text, float size, int color) {
        TextView t = new TextView(c);
        t.setText(text);
        t.setTextSize(size);
        t.setTextColor(color);
        t.setLineSpacing(Ui.dp(3), 1.12f);
        return t;
    }

    private static TextView heading(Context c, String text, float size, int color, int topMargin) {
        TextView t = base(c, text, size, color);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setPadding(0, topMargin, 0, Ui.dp(2));
        return t;
    }

    /** 版本标题：淡蓝底的整块标题，也是"跳转锚点" */
    private static View versionHeading(Context c, String text) {
        LinearLayout wrap = new LinearLayout(c);
        wrap.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams lp = matchWrap();
        lp.topMargin = Ui.dp(18);
        lp.bottomMargin = Ui.dp(4);
        wrap.setLayoutParams(lp);
        wrap.setBackground(Ui.round(Ui.ACCENT_SOFT, 10));
        wrap.setPadding(Ui.dp(12), Ui.dp(9), Ui.dp(12), Ui.dp(9));

        TextView t = new TextView(c);
        t.setText(inlineText(text, true));
        t.setTextSize(15.5f);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setTextColor(Ui.ACCENT_DARK);
        wrap.addView(t);
        return wrap;
    }

    private static TextView codeLine(Context c, String text) {
        TextView t = new TextView(c);
        t.setText(text);
        t.setTextSize(12f);
        t.setTypeface(Typeface.MONOSPACE);
        t.setTextColor(Ui.TEXT_BODY);
        t.setBackground(Ui.round(Ui.PANEL, 4));
        t.setPadding(Ui.dp(10), Ui.dp(2), Ui.dp(10), Ui.dp(2));
        return t;
    }

    private static TextView tableLine(Context c, String text) {
        String[] cells = text.split("\\|");
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < cells.length; i++) {
            String cell = cells[i].trim();
            if (cell.length() == 0) continue;
            if (sb.length() > 0) sb.append("　|　");
            sb.append(cell);
        }
        TextView t = base(c, sb.toString(), 12.5f, Ui.TEXT_BODY);
        t.setPadding(Ui.dp(4), Ui.dp(3), 0, Ui.dp(3));
        return t;
    }

    /** 引用块：淡灰底 + 左侧留白，视觉上和应用其它地方一致 */
    private static android.graphics.drawable.Drawable leftBar(Context c) {
        return Ui.round(Ui.PANEL, 8);
    }

    // ------------------------------------------------------------ 行内格式

    private static TextView inline(Context c, String text, float size, int color) {
        TextView t = base(c, "", size, color);
        t.setText(inlineText(text, false));
        return t;
    }

    /** 处理 **粗体** 与 `等宽`（去掉标记，用 span 表现） */
    private static CharSequence inlineText(String text, boolean boldAll) {
        String s = text;
        Matcher m = Pattern.compile("\\*\\*(.+?)\\*\\*|`([^`]+)`").matcher(s);
        List<int[]> spans = new ArrayList<int[]>();
        List<Integer> kinds = new ArrayList<Integer>();
        StringBuilder sb = new StringBuilder();
        int last = 0;
        while (m.find()) {
            sb.append(s.substring(last, m.start()));
            int start = sb.length();
            if (m.group(1) != null) {
                sb.append(m.group(1));
                spans.add(new int[]{start, sb.length()});
                kinds.add(1);
            } else {
                sb.append(m.group(2));
                spans.add(new int[]{start, sb.length()});
                kinds.add(2);
            }
            last = m.end();
        }
        sb.append(s.substring(last));
        SpannableString out = new SpannableString(sb.toString());
        if (boldAll) {
            out.setSpan(new StyleSpan(Typeface.BOLD), 0, out.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        }
        for (int i = 0; i < spans.size(); i++) {
            int[] r = spans.get(i);
            if (r[0] >= r[1] || r[1] > out.length()) continue;
            if (kinds.get(i) == 1) {
                out.setSpan(new StyleSpan(Typeface.BOLD), r[0], r[1], Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                out.setSpan(new ForegroundColorSpan(Ui.INK), r[0], r[1], Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            } else {
                out.setSpan(new TypefaceSpan("monospace"), r[0], r[1], Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                out.setSpan(new ForegroundColorSpan(Ui.ACCENT_DARK), r[0], r[1], Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            }
        }
        return out;
    }
}
