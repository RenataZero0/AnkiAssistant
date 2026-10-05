using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 界面基座：调色板 / DPI 缩放 / 圆角绘制 / 自绘控件（Card、Pill）。
    ///
    /// 颜色全部是静态字段，由 <see cref="Theme.Apply"/> 在启动和换肤时灌进来，
    /// 所以界面上不要再写死颜色值，一律用 Ui.XXX。
    /// </summary>
    public static class Ui
    {
        // ===== 调色板（默认蓝，启动时会被当前皮肤覆盖）=====
        public static Color BG = Color.FromArgb(0xF3, 0xF5, 0xF9);
        public static Color CARD = Color.White;
        public static Color PANEL = Color.FromArgb(0xF6, 0xF9, 0xFE);
        public static Color INK = Color.FromArgb(0x1B, 0x24, 0x32);
        public static Color SUB = Color.FromArgb(0x71, 0x80, 0x9A);
        public static Color LINE = Color.FromArgb(0xE5, 0xE9, 0xF0);
        public static Color ACCENT = Color.FromArgb(0x35, 0x68, 0xE8);
        public static Color ACCENT_DARK = Color.FromArgb(0x2B, 0x52, 0xBC);
        public static Color ACCENT_SOFT = Color.FromArgb(0xE8, 0xEF, 0xFE);
        public static Color GREEN = Color.FromArgb(0x21, 0xA3, 0x66);
        public static Color GREEN_SOFT = Color.FromArgb(0xE4, 0xF5, 0xEC);
        public static Color AMBER = Color.FromArgb(0xDE, 0x94, 0x20);
        public static Color AMBER_SOFT = Color.FromArgb(0xFD, 0xF2, 0xDF);
        public static Color RED = Color.FromArgb(0xE0, 0x53, 0x3F);
        public static Color RED_SOFT = Color.FromArgb(0xFB, 0xE9, 0xE5);
        public static Color TEXT_BODY = Color.FromArgb(0x3C, 0x4A, 0x60);
        public static Color TEXT_DIM = Color.FromArgb(0x9A, 0xA6, 0xB8);
        public static Color WHITE = Color.White;

        /// <summary>DPI 缩放系数（96 DPI = 1.0）。启动时用 Program.InitDpi 设好。</summary>
        public static float S = 1f;

        public static string FontName = "Microsoft YaHei UI";

        // ===== 尺寸 / 字体 =====
        /// <summary>逻辑像素 → 物理像素。所有手写坐标都要过这一层。</summary>
        public static int Px(double v) { return (int)Math.Round(v * S); }

        public static Font F(float pt, bool bold = false)
        {
            return new Font(FontName, pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        // ===== 绘制工具 =====
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c, float w = 1f)
        {
            if (r.Width <= 1 || r.Height <= 1) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inner = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath p = Round(inner, radius))
            using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }

        /// <summary>左上角起画一行文字。</summary>
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
            g.TextRenderingHint = old;
        }

        /// <summary>在 (cx, cy) 居中画一行文字。</summary>
        public static void TextC(Graphics g, string s, Font f, Color c, int cx, int cy)
        {
            if (string.IsNullOrEmpty(s)) return;
            SizeF sz = g.MeasureString(s, f);
            Text(g, s, f, c, (int)Math.Round(cx - sz.Width / 2), (int)Math.Round(cy - sz.Height / 2));
        }

        /// <summary>在矩形里垂直居中、左对齐画一行（超出用省略号）。</summary>
        public static void TextVC(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 4) return;
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var fmt = new StringFormat(StringFormatFlags.NoWrap))
            {
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.LineAlignment = StringAlignment.Center;
                using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, fmt);
            }
            g.TextRenderingHint = old;
        }

        /// <summary>按宽度逐字换行（自绘长文本用，比如提示浮窗的正文）。</summary>
        public static string[] Wrap(Graphics g, string text, Font f, int width)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text)) return lines.ToArray();
            foreach (string para in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                var cur = new System.Text.StringBuilder();
                foreach (char ch in para)
                {
                    string test = cur.ToString() + ch;
                    if (g.MeasureString(test, f).Width > width && cur.Length > 0)
                    {
                        lines.Add(cur.ToString());
                        cur.Length = 0;
                    }
                    cur.Append(ch);
                }
                if (cur.Length > 0) lines.Add(cur.ToString());
            }
            return lines.ToArray();
        }

        /// <summary>WinForms 的双缓冲是 protected 属性，只能反射打开。</summary>
        public static void EnableDoubleBuffer(Control c)
        {
            try
            {
                PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(c, true, null);
            }
            catch { }
        }

        /// <summary>画一段带圆点的项目符号列表，返回占用的高度。</summary>
        public static int Bullets(Graphics g, string[] items, Font f, Color dot, Color text,
            int x, int y, int width, int lineGap = 6)
        {
            foreach (string it in items)
            {
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, x, y + Px(7), Px(5), Px(5));
                using (var b = new SolidBrush(text))
                using (var fmt = new StringFormat())
                {
                    fmt.Trimming = StringTrimming.EllipsisCharacter;
                    g.DrawString(it, f, b, new RectangleF(x + Px(14), y, width - Px(14), f.Height * 3), fmt);
                }
                SizeF sz = g.MeasureString(it, f, width - Px(14));
                y += (int)Math.Ceiling(sz.Height) + Px(lineGap);
            }
            return y;
        }

        // ===== 原生 =====
        public static class Native
        {
            [DllImport("user32.dll")]
            static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
            const int WM_SETREDRAW = 0x000B;

            /// <summary>重建控件前冻结重绘，避免整窗闪烁。</summary>
            public static void Freeze(Control c)
            {
                try { SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero); } catch { }
            }

            public static void Unfreeze(Control c)
            {
                try
                {
                    SendMessage(c.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    c.Invalidate(true);
                    c.Update();
                }
                catch { }
            }
        }
    }

    /// <summary>圆角卡片：白色底 + 1px 描边，子控件放里面。</summary>
    public class Card : Panel
    {
        public int Radius = 14;
        public Color Fill = Ui.CARD;
        public Color Border = Ui.LINE;
        public bool ShowBorder = true;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 先用父容器背景铺一遍（圆角外才不会露出花屏）
            if (Parent != null)
            {
                using (var b = new SolidBrush(Parent.BackColor))
                    e.Graphics.FillRectangle(b, ClientRectangle);
            }
            Ui.FillRound(e.Graphics, new Rectangle(0, 0, Width, Height), Ui.Px(Radius), Fill);
            if (ShowBorder) Ui.StrokeRound(e.Graphics, new Rectangle(0, 0, Width, Height), Ui.Px(Radius), Border);
        }
    }

    /// <summary>胶囊按钮：Primary = 主色实心，Toggle = 开关样式，普通 = 白底描边。</summary>
    public class Pill : Control
    {
        public string Url;
        public bool Primary;
        public bool Toggle;
        public bool On;
        bool _hover;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Ui.F(9f);
        }

        /// <summary>按文字算按钮宽度（+ 左右内边距 18）。</summary>
        public static int Measure(string text, Font f)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                return (int)Math.Ceiling(g.MeasureString(text, f).Width) + 18;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Parent != null)
            {
                using (var b = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(b, ClientRectangle);
            }

            Color fill, text, border;
            if (Toggle)
            {
                // 选中态必须跟主题主色走；写死 Ui.GREEN 会在所有皮肤里都是绿的
                // （深色皮肤下尤其突兀）。
                fill = On ? Ui.ACCENT : (_hover ? Ui.ACCENT_SOFT : Ui.CARD);
                text = On ? Ui.WHITE : Ui.INK;
                border = On ? Ui.ACCENT : Ui.LINE;
            }
            else if (Primary)
            {
                fill = _hover ? Ui.ACCENT_DARK : Ui.ACCENT;
                text = Ui.WHITE;
                border = fill;
            }
            else
            {
                fill = _hover ? Ui.ACCENT_SOFT : Ui.CARD;
                text = _hover ? Ui.ACCENT : Ui.SUB;
                border = _hover ? Ui.ACCENT : Ui.LINE;
            }

            int radius = Height / 2;
            Ui.FillRound(g, new Rectangle(0, 0, Width, Height), radius, fill);
            Ui.StrokeRound(g, new Rectangle(0, 0, Width, Height), radius, border);
            Ui.TextC(g, Text, Font, text, Width / 2, Height / 2);
        }
    }
}
