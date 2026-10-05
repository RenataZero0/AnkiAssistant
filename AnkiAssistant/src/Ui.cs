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
        public static Color SUB = Color.FromArgb(0x5C, 0x6A, 0x80);
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
        public static Color TEXT_DIM = Color.FromArgb(0x7A, 0x87, 0x9C);
        public static Color WHITE = Color.White;

        /// <summary>DPI 缩放系数（96 DPI = 1.0）。启动时用 Program.InitDpi 设好。</summary>
        public static float S = 1f;

        public static string FontName = "Microsoft YaHei UI";

        // ===== 尺寸 / 字体 =====
        /// <summary>逻辑像素 → 物理像素。所有手写坐标都要过这一层。</summary>
        public static int Px(double v) { return (int)Math.Round(v * S); }

        /// <summary>字号整体放大系数。中文字形在小字号下容易「糊」，统一抬一档。</summary>
        public static float FontBoost = 1.1f;

        // 字体必须缓存：自绘控件每次 OnPaint 都会要字体，而 Font 包着一个 HFONT 句柄，
        // 不停 new 而不释放会把进程的 GDI 句柄吃光（用一会儿就整片界面卡住、画不出来）。
        static readonly System.Collections.Generic.Dictionary<string, Font> Fonts =
            new System.Collections.Generic.Dictionary<string, Font>();

        public static Font F(float pt, bool bold = false)
        {
            string key = FontName + "|" + (int)Math.Round(pt * 1000) + "|" + (bold ? 1 : 0) + "|" +
                         (int)Math.Round(FontBoost * 1000);
            Font hit;
            if (Fonts.TryGetValue(key, out hit)) return hit;
            Font made = new Font(FontName, pt * FontBoost, bold ? FontStyle.Bold : FontStyle.Regular,
                                 GraphicsUnit.Point);
            Fonts[key] = made;
            return made;
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

        /// <summary>自绘文字统一走 GDI 的 TextRenderer —— 小字号下比 GDI+ DrawString 清楚得多（不糊）。</summary>
        static TextFormatFlags TFlags(bool verticalCenter, bool ellipsis)
        {
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (verticalCenter) fl |= TextFormatFlags.VerticalCenter;
            if (ellipsis) fl |= TextFormatFlags.EndEllipsis;
            return fl;
        }

        static Size MeasureGdi(Graphics g, string s, Font f)
        {
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        /// <summary>左上角起画一行文字。</summary>
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, new Point(x, y), c, TFlags(false, false));
        }

        /// <summary>在 (cx, cy) 居中画一行文字。</summary>
        public static void TextC(Graphics g, string s, Font f, Color c, int cx, int cy)
        {
            if (string.IsNullOrEmpty(s)) return;
            Size sz = MeasureGdi(g, s, f);
            Text(g, s, f, c, cx - sz.Width / 2, cy - sz.Height / 2);
        }

        /// <summary>在矩形里垂直居中、左对齐画一行（超出用省略号）。</summary>
        public static void TextVC(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 4) return;
            TextRenderer.DrawText(g, s, f, r, c, TFlags(true, true) | TextFormatFlags.Left);
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
                    if (MeasureGdi(g, test, f).Width > width && cur.Length > 0)
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

    /// <summary>
    /// 圆角输入框：外圈是自绘的圆角底 + 描边（聚焦时描边变主色），里面塞一个无边框 TextBox。
    ///
    /// 原生 TextBox 的 FixedSingle / Fixed3D 边框在自绘卡片里非常突兀，所以一律用这个类，
    /// 不要再直接 new TextBox 摆到界面上。用法和 TextBox 基本一样（.Text / .TextChanged /
    /// .ReadOnly / .Multiline），另外多了 Placeholder 与 Secret。
    /// </summary>
    public class Input : Control
    {
        public readonly TextBox Inner;
        /// <summary>圆角半径（逻辑像素）。</summary>
        public int Radius = 10;
        /// <summary>底色（逻辑像素的内边距见 PadX / PadY）。</summary>
        public Color Fill = Ui.PANEL;
        public int PadX = 11;
        public int PadY = 7;

        bool _focus;
        bool _hover;
        bool _phOn;                  // 现在框里显示的是不是灰色占位文字
        bool _phHold;                // 正在替占位文字改 Text，不往外转发事件
        string _placeholder = "";

        /// <summary>替掉基类的同名事件，转发内部 TextBox 的 TextChanged。</summary>
        public new event EventHandler TextChanged;

        /// <summary>同上，转发内部 TextBox 的 KeyDown（回车提交之类都挂这里）。</summary>
        public new event KeyEventHandler KeyDown;

        public Input() : this(false, false) { }

        public Input(bool multiline, bool secret)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;

            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.BackColor = Fill;
            Inner.ForeColor = Ui.INK;
            Inner.Font = Ui.F(10f);
            Inner.Multiline = multiline;
            Inner.WordWrap = multiline;
            Inner.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
            Inner.UseSystemPasswordChar = secret;
            Inner.GotFocus += delegate { _focus = true; ClearPh(); Invalidate(); };
            Inner.LostFocus += delegate { _focus = false; if (Inner.Text.Length == 0) ShowPh(); Invalidate(); };
            Inner.MouseEnter += delegate { _hover = true; Invalidate(); };
            Inner.MouseLeave += delegate { _hover = false; Invalidate(); };
            Inner.TextChanged += delegate
            {
                if (_phHold) return;
                if (_phOn) { _phOn = false; Inner.ForeColor = Ui.INK; }
                if (TextChanged != null) TextChanged(this, EventArgs.Empty);
            };
            Inner.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (KeyDown != null) KeyDown(this, e);
            };
            Controls.Add(Inner);
        }

        public bool Multiline { get { return Inner.Multiline; } }
        /// <summary>多行时才有效：true 自动折行，false 靠横向滚动。</summary>
        public bool WordWrap
        {
            get { return Inner.WordWrap; }
            set { Inner.WordWrap = value; }
        }
        public bool ReadOnly
        {
            get { return Inner.ReadOnly; }
            set { Inner.ReadOnly = value; }
        }
        public bool Secret
        {
            get { return Inner.UseSystemPasswordChar; }
            set { Inner.UseSystemPasswordChar = value; }
        }
        public ScrollBars Scrollbars
        {
            get { return Inner.ScrollBars; }
            set { Inner.ScrollBars = value; }
        }

        /// <summary>底色（同时同步给内部 TextBox，另外还要能换主题）。</summary>
        public Color BoxFill
        {
            get { return Fill; }
            set { Fill = value; Inner.BackColor = value; Invalidate(); }
        }

        /// <summary>空白且未聚焦时显示的灰色提示（借框内的字来画，所以取 Text 会拿到空串）。</summary>
        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value == null ? "" : value;
                if (Inner.Text.Length == 0 && !_focus) ShowPh();
            }
        }

        public override string Text
        {
            get { return _phOn ? "" : Inner.Text; }
            set
            {
                ClearPh();
                Inner.Text = value == null ? "" : value;
                if (Inner.Text.Length == 0 && !_focus) ShowPh();
            }
        }

        /// <summary>把光标交给内部 TextBox（焦点在自绘外框上时调用）。</summary>
        public void FocusInner() { Inner.Focus(); }

        public void SelectAllText() { ClearPh(); Inner.SelectAll(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Inner.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Inner.Enabled = Enabled;
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Inner.Font = Font;
            LayoutInner();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutInner();
        }

        void LayoutInner()
        {
            if (Inner == null) return;
            int x = Ui.Px(PadX), y = Ui.Px(PadY);
            int w = Width - x * 2, h;
            if (Inner.Multiline) h = Height - y * 2;
            else h = Inner.PreferredHeight;
            if (h < 1) h = 1;
            if (!Inner.Multiline) y = Math.Max(0, (Height - h) / 2);
            if (w < 1) w = 1;
            Inner.Bounds = new Rectangle(x, y, w, h);
        }

        /// <summary>把灰色占位文字塞进输入框（用 _phHold 挡住这次事件，免得外面当成用户输入）。</summary>
        void ShowPh()
        {
            if (_placeholder.Length == 0 || Inner.Text.Length > 0) return;
            _phOn = true;
            _phHold = true;
            Inner.ForeColor = Ui.TEXT_DIM;
            Inner.Text = _placeholder;
            _phHold = false;
            Inner.SelectionStart = 0;
            Inner.SelectionLength = 0;
        }

        void ClearPh()
        {
            if (!_phOn) return;
            _phOn = false;
            _phHold = true;
            Inner.Text = "";
            _phHold = false;
            Inner.ForeColor = Ui.INK;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Ui.BG;
            if (Parent != null)
            {
                bg = Parent.BackColor;
                if (bg == Color.Transparent)
                {
                    var c = Parent as Card;
                    if (c != null) bg = c.Fill;
                    else bg = Ui.BG;
                }
            }
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);

            var r = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, r, Ui.Px(Radius), Enabled ? Fill : Ui.BG);
            Color edge = _focus ? Ui.ACCENT : (_hover ? Ui.SUB : Ui.LINE);
            Ui.StrokeRound(g, r, Ui.Px(Radius), edge, _focus ? 1.6f : 1f);
        }
    }
}
