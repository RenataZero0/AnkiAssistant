package com.ankiassistant;

import android.app.Activity;

import java.util.ArrayList;
import java.util.List;

/**
 * 皮肤 / 主题。
 *
 * 之前 Ui 里的配色是写死的常量，这里改成"可切换的调色板"：
 * 每个主题给出一整套颜色，启动时（以及切换后重建界面时）灌进 {@link Ui}。
 * 深色主题还会把制卡编辑区 / 浏览详情的网页一起切成暗色（见 editor.html 的 setTheme）。
 */
public class Theme {

    public final String id;
    public final String name;
    public final boolean dark;

    public final int bg, card, panel, ink, sub, line;
    public final int accent, accentDark, accentSoft;
    public final int green, greenSoft, amber, amberSoft, red, redSoft;
    public final int textBody, textDim;

    private Theme(String id, String name, boolean dark,
                  int bg, int card, int panel, int ink, int sub, int line,
                  int accent, int accentDark, int accentSoft,
                  int green, int greenSoft, int amber, int amberSoft, int red, int redSoft,
                  int textBody, int textDim) {
        this.id = id;
        this.name = name;
        this.dark = dark;
        this.bg = bg; this.card = card; this.panel = panel;
        this.ink = ink; this.sub = sub; this.line = line;
        this.accent = accent; this.accentDark = accentDark; this.accentSoft = accentSoft;
        this.green = green; this.greenSoft = greenSoft;
        this.amber = amber; this.amberSoft = amberSoft;
        this.red = red; this.redSoft = redSoft;
        this.textBody = textBody; this.textDim = textDim;
    }

    /** 全部皮肤（顺序就是设置里的顺序） */
    public static List<Theme> all() {
        List<Theme> out = new ArrayList<Theme>();
        out.add(new Theme("blue", "默认蓝", false,
                0xFFF3F5F9, 0xFFFFFFFF, 0xFFF6F9FE, 0xFF1B2432, 0xFF71809A, 0xFFE5E9F0,
                0xFF3568E8, 0xFF2B52BC, 0xFFE8EFFE,
                0xFF21A366, 0xFFE4F5EC, 0xFFDE9420, 0xFFFDF2DF, 0xFFE0533F, 0xFFFBE9E5,
                0xFF3C4A60, 0xFF9AA6B8));
        out.add(new Theme("forest", "森林绿", false,
                0xFFF1F6F2, 0xFFFFFFFF, 0xFFF3F8F4, 0xFF17241C, 0xFF6C8377, 0xFFDEE8E1,
                0xFF1F9D6B, 0xFF157C53, 0xFFE2F4EC,
                0xFF1F9D6B, 0xFFE2F4EC, 0xFFCB8A22, 0xFFFBF1DD, 0xFFD5503C, 0xFFFBE7E3,
                0xFF354A3E, 0xFF93A79A));
        out.add(new Theme("sunset", "暖阳橙", false,
                0xFFFAF6F1, 0xFFFFFFFF, 0xFFFBF6F0, 0xFF2A211A, 0xFF8A7A69, 0xFFEDE4D9,
                0xFFE07A2F, 0xFFBC5F19, 0xFFFDEDDD,
                0xFF2F9E63, 0xFFE3F3EA, 0xFFD08A1E, 0xFFFCEFDB, 0xFFD0503C, 0xFFFBE6E1,
                0xFF4C4034, 0xFFA79A8B));
        out.add(new Theme("violet", "玫瑰紫", false,
                0xFFF6F4FB, 0xFFFFFFFF, 0xFFF7F5FC, 0xFF211B33, 0xFF7E7595, 0xFFE6E1F2,
                0xFF7C5CFF, 0xFF5F42D8, 0xFFEDE8FF,
                0xFF2AA36B, 0xFFE3F5EB, 0xFFD5931F, 0xFFFCF0DA, 0xFFDC4F58, 0xFFFBE7E9,
                0xFF3F3856, 0xFF9C93B2));
        out.add(new Theme("dark", "深色", true,
                0xFF10131A, 0xFF181C25, 0xFF20252F, 0xFFE9EEF7, 0xFF9AA6BC, 0xFF2A3040,
                0xFF6E9BFF, 0xFF4E7BEC, 0xFF1E2A44,
                0xFF43C08B, 0xFF14301F, 0xFFE0A94A, 0xFF33280F, 0xFFFF7B72, 0xFF3A1D1A,
                0xFFC6CEDC, 0xFF7C8798));
        return out;
    }

    public static Theme byId(String id) {
        for (Theme t : all()) {
            if (t.id.equals(id)) return t;
        }
        return all().get(0);
    }

    /** 把主题灌进 Ui 的调色板（界面里所有颜色都从这里取） */
    public void apply() {
        Ui.BG = bg;
        Ui.CARD = card;
        Ui.INK = ink;
        Ui.SUB = sub;
        Ui.LINE = line;
        Ui.ACCENT = accent;
        Ui.ACCENT_DARK = accentDark;
        Ui.ACCENT_SOFT = accentSoft;
        Ui.GREEN = green;
        Ui.GREEN_SOFT = greenSoft;
        Ui.AMBER = amber;
        Ui.AMBER_SOFT = amberSoft;
        Ui.RED = red;
        Ui.RED_SOFT = redSoft;
        Ui.TEXT_BODY = textBody;
        Ui.TEXT_DIM = textDim;
        Ui.PANEL = panel;
        Ui.WHITE = 0xFFFFFFFF;
        Ui.DARK = dark;
    }


    /** 给网页（editor.html）用的 CSS 变量，JSON 字符串；由 CreateView 推进去 */
    public String webVars() {
        int r = (ink >> 16) & 0xFF, g = (ink >> 8) & 0xFF, b = ink & 0xFF;
        StringBuilder sb = new StringBuilder();
        sb.append('{');
        sb.append("\"bg\":\"").append(hex(bg)).append("\",");
        sb.append("\"card\":\"").append(hex(card)).append("\",");
        sb.append("\"panel\":\"").append(hex(panel)).append("\",");
        sb.append("\"ink\":\"").append(hex(ink)).append("\",");
        sb.append("\"body\":\"").append(hex(textBody)).append("\",");
        sb.append("\"sub\":\"").append(hex(sub)).append("\",");
        sb.append("\"dim\":\"").append(hex(textDim)).append("\",");
        sb.append("\"line\":\"").append(hex(line)).append("\",");
        sb.append("\"accent\":\"").append(hex(accent)).append("\",");
        sb.append("\"accentSoft\":\"").append(hex(accentSoft)).append("\",");
        sb.append("\"pressed\":\"").append(hex(accentSoft)).append("\",");
        sb.append("\"amber\":\"").append(hex(amber)).append("\",");
        sb.append("\"shadow\":\"rgba(").append(r).append(',').append(g).append(',')
          .append(b).append(",.22)\"");
        sb.append('}');
        return sb.toString();
    }

    private static String hex(int c) {
        return String.format("#%06X", c & 0xFFFFFF);
    }

    public static Theme apply(Activity act, Store store) {
        Theme t = byId(store.theme());
        t.apply();
        return t;
    }
}
