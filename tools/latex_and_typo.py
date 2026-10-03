"""详情渲染与转换的两处修正：
   1) LaTeX 合并：老卡里 "\(A\),\\quad \(B\)" 这种被切断的写法，把中间的 \\命令 并回同一个数学环境
      → \\quad 才会真正渲染成空格
   2) 排版：标签与内容同一行（蓝色粗标签 + 紧接内容），字号/行距调顺
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------- BrowseView ----------
p = APP + r"\src\com\ankiassistant\BrowseView.java"
s = io.open(p, encoding="utf-8").read()

# 1a) sanitizeField 里加 LaTeX 合并
old_san = '''        s = s.replaceAll("(?i)</?(anki-mathjax|latex_formula|mathjax_formula)[^>]*>", "");
        return s;'''
new_san = '''        s = s.replaceAll("(?i)</?(anki-mathjax|latex_formula|mathjax_formula)[^>]*>", "");
        // 老卡里常见的写法："\\(f = \\mu N\\),\\quad \\(f_k = \\mu_k N\\)" —— \\quad 落在数学环境外面，
        // 只会当普通文字显示。这里把只含 LaTeX 命令的间隙并回同一个数学环境。
        for (int i = 0; i < 8; i++) {
            String merged = s.replaceAll("\\\\)([^()]*\\\\\\\\[a-zA-Z]+[^()]*)\\\\\\\\(", "$1");
            if (merged.equals(s)) break;
            s = merged;
        }
        return s;'''
if old_san in s:
    s = s.replace(old_san, new_san, 1)
    print("渲染侧 LaTeX 合并已加")
else:
    print("!! sanitizeField 未匹配")

# 2) 排版：标签与内容同行
old_css = '''                + "<style>"
                + "html{-webkit-text-size-adjust:100%}"
                + "body{font-family:Roboto,'Noto Sans CJK SC','Source Han Sans SC',"
                + "'PingFang SC','Microsoft YaHei',sans-serif;font-size:16.5px;"
                + "line-height:1.78;color:#1b2432;margin:0;padding:6px 2px 28px;background:#fff;"
                + "letter-spacing:.1px;word-break:break-word}"
                + ".fld{margin:0 0 16px}"
                + ".lab{display:block;color:#3568e8;font-weight:600;font-size:13.5px;"
                + "letter-spacing:.4px;margin-bottom:3px}"
                + ".val{display:block}"
                + ".meta{color:#8b98ac;font-size:12.5px;margin:0 0 10px}"
                + "hr{border:none;border-top:1px solid #edf1f7;margin:0 0 16px}"
                + "code{background:#f3f5f9;border-radius:4px;padding:1px 5px}"
                + "mjx-container{overflow-x:auto;overflow-y:hidden}"
                + "</style></head><body>"'''
new_css = '''                + "<style>"
                + "html{-webkit-text-size-adjust:100%}"
                + "body{font-family:'Noto Sans CJK SC','Source Han Sans SC','PingFang SC',"
                + "Roboto,'Microsoft YaHei',sans-serif;font-size:16px;"
                + "line-height:1.72;color:#1b2432;margin:0;padding:4px 2px 26px;background:#fff;"
                + "letter-spacing:.1px;word-break:break-word}"
                + ".fld{margin:0 0 14px}"
                + ".lab{color:#3568e8;font-weight:600;font-size:13.5px;"
                + "letter-spacing:.3px;margin-right:7px;white-space:nowrap}"
                + ".val{color:#1b2432}"
                + ".meta{color:#8b98ac;font-size:12.5px;margin:0 0 10px}"
                + "hr{border:none;border-top:1px solid #edf1f7;margin:0 0 14px}"
                + "code{background:#f3f5f9;border-radius:4px;padding:1px 5px}"
                + "mjx-container{overflow-x:auto;overflow-y:hidden}"
                + "</style></head><body>"'''
if old_css in s:
    s = s.replace(old_css, new_css, 1)
    print("排版 CSS 已改（标签同行、字号 16px、行距 1.72）")
else:
    print("!! CSS 未匹配")

# 标签回到同一行显示
s = s.replace('''body.append("<div class=\\"fld\\"><span class=\\"lab\\">").append(esc(k))
                    .append("</span><span class=\\"val\\">")''',
'''body.append("<div class=\\"fld\\"><span class=\\"lab\\">【").append(esc(k))
                    .append("】</span><span class=\\"val\\">")''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)

# ---------- NoteMigrator：转换时也做同样的合并 ----------
p2 = APP + r"\src\com\ankiassistant\NoteMigrator.java"
s2 = io.open(p2, encoding="utf-8").read()
if "mergeSplitLatex" not in s2:
    s2 = s2.replace('''    /** 把背面拆成 字段名 -> 内容 */''',
'''    /** 把被切断的 LaTeX 接回去："\\\\(A\\\\),\\\\quad \\\\(B\\\\)" → "\\\\(A,\\\\quad B\\\\)" */
    public static String mergeSplitLatex(String s) {
        if (s == null) return null;
        String cur = s;
        for (int i = 0; i < 8; i++) {
            String merged = cur.replaceAll("\\\\)([^()]*\\\\\\\\[a-zA-Z]+[^()]*)\\\\\\\\(", "$1");
            if (merged.equals(cur)) break;
            cur = merged;
        }
        return cur;
    }

    /** 把背面拆成 字段名 -> 内容 */''')
    s2 = s2.replace('''            String body = t.substring(end + 1).trim();''',
                    '''            String body = mergeSplitLatex(t.substring(end + 1).trim());''')
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("转换侧 LaTeX 合并已加")
