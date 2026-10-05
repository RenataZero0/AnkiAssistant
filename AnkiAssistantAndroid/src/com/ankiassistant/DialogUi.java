package com.ankiassistant;

import android.app.Activity;
import android.app.Dialog;
import android.graphics.Typeface;
import android.text.method.LinkMovementMethod;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * 统一弹窗样式。
 *
 * 全应用只有这一种弹窗：白底圆角卡片 + 加粗标题 + 可选说明/内容/单选列表 + 右下角文字按钮。
 * 目的就是让所有弹窗长得一样、留白一致，不再用系统 AlertDialog 那套灰扑扑的样式。
 *
 * 用法：
 * <pre>
 * new DialogUi.Builder(act)
 *     .title("删除格式")
 *     .message("删除后无法恢复。")
 *     .negative("取消", null)
 *     .positive("删除", new Runnable() { public void run() { ... } })
 *     .show();
 * </pre>
 */
public class DialogUi {

    /** 单选列表回调 */
    public interface Picker {
        void onPick(int which);
    }

    public static class Builder {
        private final Activity act;
        private String title;
        private CharSequence message;
        private boolean messageIsHtml;
        private View content;
        private String[] items;
        private int[] itemColors;
        private int checked;
        private Picker picker;
        private String posText, neuText, negText;
        private Runnable posAct, neuAct, negAct;
        private boolean wide;

        public Builder(Activity act) { this.act = act; }

        public Builder title(String t) { this.title = t; return this; }
        public Builder message(CharSequence m) { this.message = m; return this; }
        public Builder html(CharSequence m) { this.message = m; this.messageIsHtml = true; return this; }
        public Builder content(View v) { this.content = v; return this; }
        /** 每一行前面画一个色点（例如皮肤列表） */
        public Builder choiceColors(int[] colors) { this.itemColors = colors; return this; }

        public Builder choices(String[] items, int checked, Picker p) {
            this.items = items; this.checked = checked; this.picker = p; return this;
        }
        /** 更宽的弹窗（内容较长时用） */
        public Builder wide() { this.wide = true; return this; }

        public Builder positive(String text, Runnable action) {
            this.posText = text; this.posAct = action; return this;
        }
        public Builder neutral(String text, Runnable action) {
            this.neuText = text; this.neuAct = action; return this;
        }
        public Builder negative(String text, Runnable action) {
            this.negText = text; this.negAct = action; return this;
        }

        public Dialog show() {
            final Dialog dlg = new Dialog(act);
            dlg.requestWindowFeature(Window.FEATURE_NO_TITLE);

            LinearLayout root = new LinearLayout(act);
            root.setOrientation(LinearLayout.VERTICAL);
            root.setBackground(Ui.round(Ui.CARD, 20));
            root.setPadding(Ui.dp(20), Ui.dp(18), Ui.dp(20), Ui.dp(10));

            if (title != null && title.length() > 0) {
                TextView t = new TextView(act);
                t.setText(title);
                t.setTextColor(Ui.INK);
                t.setTextSize(17.5f);
                t.setTypeface(Typeface.DEFAULT_BOLD);
                root.addView(t);
            }

            LinearLayout body = new LinearLayout(act);
            body.setOrientation(LinearLayout.VERTICAL);
            boolean bodyEmpty = true;

            if (message != null && message.length() > 0) {
                TextView m = new TextView(act);
                if (messageIsHtml) {
                    m.setText(message);
                    m.setMovementMethod(LinkMovementMethod.getInstance());
                } else {
                    m.setText(message);
                }
                m.setTextColor(Ui.TEXT_BODY);
                m.setTextSize(14);
                m.setLineSpacing(Ui.dp(4), 1f);
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                lp.topMargin = Ui.dp(10);
                body.addView(m, lp);
                bodyEmpty = false;
            }

            if (content != null) {
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                lp.topMargin = Ui.dp(14);
                body.addView(content, lp);
                bodyEmpty = false;
            }

            if (items != null && items.length > 0) {
                LinearLayout list = new LinearLayout(act);
                list.setOrientation(LinearLayout.VERTICAL);
                LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                llp.topMargin = Ui.dp(12);
                body.addView(list, llp);
                for (int i = 0; i < items.length; i++) {
                    final int idx = i;
                    final boolean on = (i == checked);
                    LinearLayout row = new LinearLayout(act);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setGravity(Gravity.CENTER_VERTICAL);
                    row.setPadding(Ui.dp(12), Ui.dp(12), Ui.dp(10), Ui.dp(12));
                    row.setBackground(Ui.press(on ? Ui.ACCENT_SOFT : 0x00000000, 12));

                    if (itemColors != null && i < itemColors.length) {
                        View dot = new View(act);
                        dot.setBackground(Ui.round(itemColors[i], 7));
                        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(
                                Ui.dp(14), Ui.dp(14));
                        dlp.rightMargin = Ui.dp(11);
                        row.addView(dot, dlp);
                    }

                    TextView label = new TextView(act);
                    label.setText(items[i]);
                    label.setTextSize(15);
                    label.setTextColor(on ? Ui.ACCENT : Ui.INK);
                    label.setTypeface(on ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
                    row.addView(label, new LinearLayout.LayoutParams(0,
                            ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                    if (on) {
                        TextView tick = new TextView(act);
                        tick.setText("✓");
                        tick.setTextColor(Ui.ACCENT);
                        tick.setTextSize(16);
                        tick.setTypeface(Typeface.DEFAULT_BOLD);
                        row.addView(tick);
                    }
                    row.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            dlg.dismiss();
                            if (picker != null) picker.onPick(idx);
                        }
                    });
                    LinearLayout.LayoutParams rlp = new LinearLayout.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                    rlp.bottomMargin = Ui.dp(4);
                    list.addView(row, rlp);
                }
                bodyEmpty = false;
            }

            if (!bodyEmpty) {
                // 内容太高时内部滚动，不撑破屏幕
                ScrollView sc = new ScrollView(act);
                sc.setVerticalScrollBarEnabled(false);
                sc.addView(body, new ViewGroup.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
                root.addView(sc, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
            } else {
                View spacer = new View(act);
                root.addView(spacer, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(4)));
            }

            LinearLayout actions = new LinearLayout(act);
            actions.setOrientation(LinearLayout.HORIZONTAL);
            actions.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams alp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            alp.topMargin = Ui.dp(8);
            root.addView(actions, alp);

            addAction(actions, negText, Ui.SUB, false, dlg, negAct);
            addAction(actions, neuText, Ui.SUB, false, dlg, neuAct);
            addAction(actions, posText, Ui.ACCENT, true, dlg, posAct);

            dlg.setContentView(root);
            Window w = dlg.getWindow();
            if (w != null) {
                w.setBackgroundDrawableResource(android.R.color.transparent);
                w.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
                WindowManager.LayoutParams lp = w.getAttributes();
                lp.dimAmount = 0.42f;
                int width = (int) (act.getResources().getDisplayMetrics().widthPixels
                        * (wide ? 0.78f : 0.86f));
                int max = Ui.dp(wide ? 720 : 520);
                lp.width = Math.min(width, max);
                lp.height = ViewGroup.LayoutParams.WRAP_CONTENT;
                w.setAttributes(lp);
            }
            dlg.show();
            return dlg;
        }

        private void addAction(LinearLayout row, String text, int color, boolean bold,
                               final Dialog dlg, final Runnable action) {
            if (text == null || text.length() == 0) return;
            Button b = new Button(act);
            b.setText(text);
            b.setAllCaps(false);
            b.setTextSize(14.5f);
            b.setTextColor(color);
            b.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
            b.setBackground(Ui.press(0x00000000, 10));
            b.setPadding(Ui.dp(10), Ui.dp(8), Ui.dp(10), Ui.dp(8));
            b.setMinWidth(0);
            b.setMinimumWidth(0);
            b.setSingleLine(true);          // 中文按钮不能折成"登录并同/步"
            b.setAllCaps(false);
            b.setStateListAnimator(null);
            b.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    dlg.dismiss();
                    if (action != null) action.run();
                }
            });
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.leftMargin = Ui.dp(2);
            row.addView(b, lp);
        }
    }

    /** 简易输入行（弹窗里用），带统一样式 */
    public static FrameLayout inputWrap(Activity act, View input) {
        FrameLayout wrap = new FrameLayout(act);
        wrap.setBackground(Ui.roundStroke(Ui.PANEL, Ui.LINE, 12));
        wrap.setPadding(Ui.dp(12), Ui.dp(2), Ui.dp(12), Ui.dp(2));
        wrap.addView(input, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return wrap;
    }

    /** 弹窗里的小节标题 */
    public static TextView label(Activity act, String text) {
        TextView t = new TextView(act);
        t.setText(text);
        t.setTextColor(Ui.SUB);
        t.setTextSize(12.5f);
        t.setPadding(Ui.dp(2), Ui.dp(10), 0, Ui.dp(4));
        return t;
    }
}
