using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 自己画的 Markdown 阅读器：标题、列表、引用、表格、粗体、行内代码、代码块。
    ///
    /// 为什么不用 WebBrowser 控件：它依赖 IE 内核、字体和滚动都和主界面格格不入，
    /// 而且嵌进圆角卡片里会露出白色直角。这里手绘+自管滚动，省事也更统一。
    /// </summary>
    public class MarkdownView : Control
    {
        string _md = "";
        readonly List<Block> _blocks = new List<Block>();
        int _scroll;
        int _contentH;
        bool _dirty = true;

        class Block
        {
            public string Kind;      // h1 / h2 / h3 / p / li / quote / code / table / hr
            public string Text = "";
            public string[] Cells;   // 表格行
            public bool Header;
        }

        public MarkdownView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.CARD;
            Font = Ui.F(9.5f);
        }

        public string Markdown
        {
            get { return _md; }
            set { _md = value ?? ""; _dirty = true; _scroll = 0; Invalidate(); }
        }

        // ===== 解析 =====
        void Parse()
        {
            _blocks.Clear();
            string[] lines = _md.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            var code = new System.Text.StringBuilder();
            var table = new List<string[]>();

            Action flushTable = delegate
            {
                if (table.Count == 0) return;
                foreach (string[] row in table)
                    _blocks.Add(new Block { Kind = "table", Cells = row, Header = table.IndexOf(row) == 0 });
                table.Clear();
            };

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                string t = ln.TrimEnd();

                if (t.TrimStart().StartsWith("```"))
                {
                    if (inCode) { _blocks.Add(new Block { Kind = "code", Text = code.ToString().TrimEnd() }); code.Length = 0; }
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { code.AppendLine(ln); continue; }

                if (t.Trim().Length == 0) { flushTable(); continue; }
                if (t.Trim() == "---" || t.Trim() == "***") { flushTable(); _blocks.Add(new Block { Kind = "hr" }); continue; }

                if (t.StartsWith("|"))
                {
                    // 表格：| a | b |，分隔行（|---|）跳过
                    if (t.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0) continue;
                    string[] parts = t.Trim().Trim('|').Split('|');
                    for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim();
                    table.Add(parts);
                    continue;
                }
                flushTable();

                if (t.StartsWith("### ")) _blocks.Add(new Block { Kind = "h3", Text = t.Substring(4) });
                else if (t.StartsWith("## ")) _blocks.Add(new Block { Kind = "h2", Text = t.Substring(3) });
                else if (t.StartsWith("# ")) _blocks.Add(new Block { Kind = "h1", Text = t.Substring(2) });
                else if (t.StartsWith("> ")) _blocks.Add(new Block { Kind = "quote", Text = t.Substring(2) });
                else if (t.StartsWith("- ") || t.StartsWith("* ")) _blocks.Add(new Block { Kind = "li", Text = t.Substring(2) });
                else _blocks.Add(new Block { Kind = "p", Text = t });
            }
            flushTable();
            if (inCode && code.Length > 0) _blocks.Add(new Block { Kind = "code", Text = code.ToString().TrimEnd() });
            _dirty = false;
        }

        // ===== 行内 markdown：**粗体**、`代码` 拆成小段 =====
        class Span
        {
            public string Text;
            public bool Bold, Code;
        }

        static List<Span> Spans(string s)
        {
            var out_ = new List<Span>();
            int i = 0;
            var buf = new System.Text.StringBuilder();
            Action flush = delegate { if (buf.Length > 0) { out_.Add(new Span { Text = buf.ToString() }); buf.Length = 0; } };
            while (i < s.Length)
            {
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
                {
                    int end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > 0)
                    {
                        flush();
                        out_.Add(new Span { Text = s.Substring(i + 2, end - i - 2), Bold = true });
                        i = end + 2;
                        continue;
                    }
                }
                if (s[i] == '`')
                {
                    int end = s.IndexOf('`', i + 1);
                    if (end > 0)
                    {
                        flush();
                        out_.Add(new Span { Text = s.Substring(i + 1, end - i - 1), Code = true });
                        i = end + 1;
                        continue;
                    }
                }
                buf.Append(s[i]); i++;
            }
            flush();
            return out_;
        }

        /// <summary>画一行可换行的富文本，返回占用高度。</summary>
        int DrawRich(Graphics g, string text, Font baseFont, Color color, int x, int y, int width, int lineH)
        {
            List<Span> spans = Spans(text);
            int cx = x, cy = y;
            foreach (Span sp in spans)
            {
                Font f = sp.Code ? new Font("Consolas", baseFont.Size - 0.5f) : baseFont;
                if (sp.Bold) f = new Font(f, FontStyle.Bold);
                string[] words = sp.Code ? new[] { sp.Text } : sp.Text.Split(' ');
                for (int w = 0; w < words.Length; w++)
                {
                    string word = words[w] + (w < words.Length - 1 ? " " : "");
                    SizeF sz = g.MeasureString(word, f);
                    if (cx + sz.Width > x + width && cx > x) { cx = x; cy += lineH; }
                    if (sp.Code)
                    {
                        var box = new Rectangle(cx - Ui.Px(2), cy + Ui.Px(1), (int)sz.Width + Ui.Px(4), (int)sz.Height - Ui.Px(2));
                        Ui.FillRound(g, box, Ui.Px(3), Ui.PANEL);
                    }
                    Ui.Text(g, word, f, sp.Code ? Ui.ACCENT_DARK : color, cx, cy);
                    cx += (int)Math.Ceiling(sz.Width);
                }
            }
            return cy + lineH - y;
        }

        int MeasureRich(Graphics g, string text, Font f, int width, int lineH)
        {
            int lines = 1, used = 0;
            foreach (Span sp in Spans(text))
            {
                Font ff = sp.Bold ? new Font(f, FontStyle.Bold) : f;
                SizeF sz = g.MeasureString(sp.Text, ff);
                used += (int)sz.Width;
                while (used > width) { lines++; used -= width; }
            }
            return lines * lineH;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (_dirty) Parse();

            int pad = Ui.Px(18);
            int width = Width - pad * 2;
            if (width < 40) return;

            int y = pad - _scroll;
            var fH1 = Ui.F(13f, true);
            var fH2 = Ui.F(11.5f, true);
            var fH3 = Ui.F(10f, true);
            var fP = Ui.F(9.5f);
            var fCode = new Font("Consolas", 9f);
            int lineH = Ui.Px(20);

            // 表格列宽：按每行 | 的数量取最大值
            foreach (Block b in _blocks)
            {
                if (b.Kind == "h1")
                {
                    y += Ui.Px(6);
                    y += DrawRich(g, b.Text, fH1, Ui.INK, pad, y, width, Ui.Px(30));
                    y += Ui.Px(8);
                }
                else if (b.Kind == "h2")
                {
                    y += Ui.Px(10);
                    y += DrawRich(g, b.Text, fH2, Ui.INK, pad, y, width, Ui.Px(26));
                    y += Ui.Px(6);
                }
                else if (b.Kind == "h3")
                {
                    y += Ui.Px(6);
                    y += DrawRich(g, b.Text, fH3, Ui.INK, pad, y, width, Ui.Px(24));
                    y += Ui.Px(2);
                }
                else if (b.Kind == "p")
                {
                    y += DrawRich(g, b.Text, fP, Ui.TEXT_BODY, pad, y, width, lineH) + Ui.Px(4);
                }
                else if (b.Kind == "li")
                {
                    using (var br = new SolidBrush(Ui.ACCENT))
                        g.FillEllipse(br, pad + Ui.Px(2), y + Ui.Px(8), Ui.Px(5), Ui.Px(5));
                    y += DrawRich(g, b.Text, fP, Ui.TEXT_BODY, pad + Ui.Px(14), y, width - Ui.Px(14), lineH) + Ui.Px(2);
                }
                else if (b.Kind == "quote")
                {
                    int h = MeasureRich(g, b.Text, fP, width - Ui.Px(16), lineH);
                    Ui.FillRound(g, new Rectangle(pad, y, width, h + Ui.Px(8)), Ui.Px(6), Ui.PANEL);
                    using (var br = new SolidBrush(Ui.ACCENT))
                        g.FillRectangle(br, pad, y + Ui.Px(4), Ui.Px(3), h);
                    DrawRich(g, b.Text, fP, Ui.SUB, pad + Ui.Px(12), y + Ui.Px(4), width - Ui.Px(16), lineH);
                    y += h + Ui.Px(12);
                }
                else if (b.Kind == "code")
                {
                    string[] cl = b.Text.Split('\n');
                    int h = cl.Length * Ui.Px(18) + Ui.Px(16);
                    Ui.FillRound(g, new Rectangle(pad, y, width, h), Ui.Px(8), Ui.PANEL);
                    int cy = y + Ui.Px(8);
                    foreach (string c in cl)
                    {
                        Ui.Text(g, c, fCode, Ui.TEXT_BODY, pad + Ui.Px(10), cy);
                        cy += Ui.Px(18);
                    }
                    y += h + Ui.Px(10);
                }
                else if (b.Kind == "table")
                {
                    int cols = b.Cells.Length;
                    int cw = width / Math.Max(1, cols);
                    var row = new Rectangle(pad, y, width, Ui.Px(26));
                    if (b.Header) Ui.FillRound(g, row, Ui.Px(6), Ui.PANEL);
                    for (int c = 0; c < cols; c++)
                    {
                        var cell = new Rectangle(pad + c * cw + Ui.Px(8), y, cw - Ui.Px(12), Ui.Px(26));
                        Ui.TextVC(g, b.Cells[c], b.Header ? Ui.F(9f, true) : fP.Take(), b.Header ? Ui.INK : Ui.TEXT_BODY, cell);
                    }
                    if (!b.Header)
                    {
                        using (var p = new Pen(Ui.LINE))
                            g.DrawLine(p, pad, y + Ui.Px(26), pad + width, y + Ui.Px(26));
                    }
                    y += Ui.Px(26);
                }
                else if (b.Kind == "hr")
                {
                    y += Ui.Px(10);
                    using (var p = new Pen(Ui.LINE)) g.DrawLine(p, pad, y, pad + width, y);
                    y += Ui.Px(12);
                }
            }
            _contentH = y + _scroll + pad;

            // 滚动条
            if (_contentH > Height)
            {
                int trackH = Height - Ui.Px(8);
                int thumbH = Math.Max(Ui.Px(28), trackH * Height / _contentH);
                int max = _contentH - Height;
                int top = Ui.Px(4) + (max <= 0 ? 0 : (trackH - thumbH) * _scroll / max);
                Ui.FillRound(g, new Rectangle(Width - Ui.Px(7), top, Ui.Px(4), thumbH), Ui.Px(2), Ui.LINE);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_contentH <= Height) return;
            _scroll -= e.Delta;
            int max = _contentH - Height;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); _dirty = true; }
    }

    static class FontExt
    {
        /// <summary>把字体缩到下一个整数号（表格里略微收紧，省得拥挤）。</summary>
        public static Font Take(this Font f)
        {
            return new Font(f.FontFamily, Math.Max(7f, f.Size - 0.5f), f.Style);
        }
    }
}
