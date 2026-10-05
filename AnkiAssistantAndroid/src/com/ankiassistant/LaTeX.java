package com.ankiassistant;

/**
 * 老卡片里的 LaTeX 清理：
 *   · 去掉 <inline_latex_formula> 这类自定义包壳
 *   · 把被切断的数学环境接回去："\\(f = \\mu N\\),\\quad \\(f_k = \\mu_k N\\)"
 *     → "\\(f = \\mu N,\\quad f_k = \\mu_k N\\)"，这样 \\quad 才会真正渲染成空格
 */
public class LaTeX {

    public static String clean(String v) {
        if (v == null) return "";
        String s = v.replaceAll("(?i)</?(inline_latex_formula|anki-mathjax|latex_formula|mathjax_formula)[^>]*>", "");
        return mergeSplit(s);
    }

    /** 把只含 LaTeX 命令的间隙并回同一个数学环境 */
    public static String mergeSplit(String s) {
        if (s == null) return null;
        String cur = s;
        for (int i = 0; i < 8; i++) {
            String merged = cur.replaceAll("\\)([^()]*\\\\[a-zA-Z]+[^()]*)\\(", "$1");
            if (merged.equals(cur)) break;
            cur = merged;
        }
        return cur;
    }
}
