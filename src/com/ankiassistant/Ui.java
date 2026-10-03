package com.ankiassistant;

import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.util.TypedValue;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;

/**
 * 配色与控件样式。配色与 StudyCompanion 保持同一套设计语言。
 * 全部用系统自带控件 + GradientDrawable，不依赖任何 support library。
 */
public class Ui {

    public static int BG = 0xFFF3F5F9;
    public static int CARD = 0xFFFFFFFF;
    public static int INK = 0xFF1B2432;
    public static int SUB = 0xFF71809A;
    public static int LINE = 0xFFE5E9F0;
    public static int ACCENT = 0xFF3568E8;
    public static int ACCENT_DARK = 0xFF2B52BC;
    public static int ACCENT_SOFT = 0xFFE8EFFE;
    public static int GREEN = 0xFF21A366;
    public static int GREEN_SOFT = 0xFFE4F5EC;
    public static int AMBER = 0xFFDE9420;
    public static int AMBER_SOFT = 0xFFFDF2DF;
    public static int RED = 0xFFE0533F;
    public static int RED_SOFT = 0xFFFBE9E5;
    public static int TEXT_BODY = 0xFF3C4A60;
    public static int TEXT_DIM = 0xFF9AA6B8;
    public static boolean DARK = false;
    /** 次级面板色（索引栏、徽标、输入框底等） */
    public static int PANEL = 0xFFF6F9FE;
    public static int WHITE = 0xFFFFFFFF;

    /** 压暗（用于按下态） */
    /** 给颜色加透明度（0..255） */
    public static int alpha(int color, int a) {
        return (color & 0x00FFFFFF) | ((a & 0xFF) << 24);
    }

    public static int dim(int color, float f) {
        int a = (color >>> 24) & 0xFF;
        int r = (color >>> 16) & 0xFF;
        int g = (color >>> 8) & 0xFF;
        int b = color & 0xFF;
        return (((int) (a * f)) << 24) | (r << 16) | (g << 8) | b;
    }

    public static int dp(float v) {
        return Math.round(v * android.content.res.Resources.getSystem().getDisplayMetrics().density);
    }

    /** 纯色圆角背景 */
    public static GradientDrawable round(int color, float radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radiusDp));
        return d;
    }

    /** 描边圆角背景 */
    /** 只有上面两个角是圆的（弹窗里的软色头部用） */
    public static GradientDrawable roundTop(int color, float radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        float r = dp(radiusDp);
        g.setCornerRadii(new float[]{r, r, r, r, 0, 0, 0, 0});
        return g;
    }

    public static GradientDrawable roundStroke(int color, int stroke, float radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radiusDp));
        d.setStroke(dp(1), stroke);
        return d;
    }

    /** 按钮：正常色 + 按下变暗 */
    public static StateListDrawable press(int color, float radiusDp) {
        StateListDrawable s = new StateListDrawable();
        s.addState(new int[]{android.R.attr.state_pressed}, round(dim(color, 0.85f), radiusDp));
        s.addState(new int[]{}, round(color, radiusDp));
        return s;
    }

    /** 按钮：正常色 + 按下变暗 + 描边（浅色按钮放在白卡片上也能看清边界） */
    public static StateListDrawable pressStroke(int color, int stroke, float radiusDp) {
        StateListDrawable s = new StateListDrawable();
        s.addState(new int[]{android.R.attr.state_pressed},
                roundStroke(dim(color, 0.90f), stroke, radiusDp));
        s.addState(new int[]{}, roundStroke(color, stroke, radiusDp));
        return s;
    }

    /** 实心按钮 */
    public static void btn(Button b, int bg, int fg) {
        b.setBackground(press(bg, 10));
        b.setTextColor(fg);
        b.setAllCaps(false);
        b.setPadding(dp(14), dp(9), dp(14), dp(9));
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
    }

    /** 主按钮（蓝底白字） */
    public static void primary(Button b) { btn(b, ACCENT, WHITE); }

    /** 次按钮（面板底 + 主文字色 + 描边，跟随皮肤） */
    public static void secondary(Button b) {
        btn(b, PANEL, INK);
        b.setBackground(pressStroke(PANEL, LINE, 10));
    }

    /** 危险按钮 */
    public static void danger(Button b) { btn(b, RED_SOFT, RED); }

    /** 输入框统一样式：浅底 + 圆角描边（比默认下划线干净） */
    /** 复选框：勾选态用主色，未勾选用线条色（深色皮肤下也能看清） */
    public static void check(android.widget.CheckBox c) {
        int[][] states = new int[][]{
                new int[]{android.R.attr.state_checked},
                new int[]{-android.R.attr.state_checked}
        };
        c.setButtonTintList(new android.content.res.ColorStateList(states,
                new int[]{ACCENT, SUB}));
    }

    public static void field(EditText e) {
        e.setBackground(pressStroke(PANEL, LINE, 10));
        e.setPadding(dp(10), dp(8), dp(10), dp(8));
        e.setTextColor(INK);
    }

    public static void hint(EditText e, String text) {
        e.setHint(text);
        e.setHintTextColor(TEXT_DIM);
    }

    public static void title(TextView t, String text) {
        t.setText(text);
        t.setTextColor(INK);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        t.setTypeface(Typeface.DEFAULT_BOLD);
    }

    public static void body(TextView t, String text) {
        t.setText(text);
        t.setTextColor(TEXT_BODY);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
    }

    /** 白色圆角卡片背景 */
    public static void card(android.view.View v) {
        v.setBackground(round(CARD, 14));
    }
}
