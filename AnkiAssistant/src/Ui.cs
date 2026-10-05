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
            // 后台线程也会进来（例如后台预渲染），字典得加锁；
            // 返回的是共享对象，调用方**不要 Dispose**（释放掉之后再用就是「参数无效」）。
            lock (Fonts)
            {
                Font hit;
                if (Fonts.TryGetValue(key, out hit)) return hit;
                Font made = new Font(FontName, pt * FontBoost, bold ? FontStyle.Bold : FontStyle.Regular,
                                     GraphicsUnit.Point);
                Fonts[key] = made;
                return made;
            }
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

        /// <summary>
        /// 自绘控件要先铺一层父容器的底色（控件自己是 Transparent 背景，
        /// 直接画会在透明层上留下上一次的残留）。Card 上取卡片底色，其它取 BackColor。
        /// </summary>
        public static Color ParentFill(Control c)
        {
            if (c == null) return BG;
            Control p = c.Parent;
            if (p == null) return BG;
            Color bg = p.BackColor;
            if (bg == Color.Transparent)
            {
                Card card = p as Card;
                return card != null ? card.Fill : BG;
            }
            return bg;
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

            const int WM_MOUSEWHEEL = 0x020A;
            const int WM_NCLBUTTONDOWN = 0x00A1;
            public const int HTCAPTION = 2;

            [DllImport("user32.dll")]
            static extern bool ReleaseCapture();

            /// <summary>把滚轮消息转给别的窗口（输入框里的滚轮要转给外层滚动容器）。</summary>
            public static void SendWheel(IntPtr hWnd, int delta, Point screenPt)
            {
                try
                {
                    IntPtr lp = (IntPtr)((screenPt.Y << 16) | (screenPt.X & 0xFFFF));
                    SendMessage(hWnd, WM_MOUSEWHEEL, (IntPtr)(delta << 16), lp);
                }
                catch { }
            }

            /// <summary>
            /// 交给系统自己的标题栏拖动循环：这样才有 Aero Snap、
            /// 以及「最大化状态下拖动自动还原」这套系统行为，比手搓 SetBounds 稳。
            /// </summary>
            public static void BeginMove(Control c)
            {
                try
                {
                    if (c == null || !c.IsHandleCreated) return;
                    ReleaseCapture();
                    SendMessage(c.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
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
        bool _secret;                // 密码框开关。**不能拿 Inner.UseSystemPasswordChar 当状态**：
                                     // 显示占位文字时它被临时关掉，用它判断就再也恢复不回掩码（密码明文）
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
            _secret = secret;
            Inner.UseSystemPasswordChar = secret;
            Inner.GotFocus += delegate { _focus = true; ClearPh(); Invalidate(); };
            Inner.LostFocus += delegate { _focus = false; if (Inner.Text.Length == 0) ShowPh(); Invalidate(); };
            Inner.MouseEnter += delegate { _hover = true; Invalidate(); };
            Inner.MouseLeave += delegate { _hover = false; Invalidate(); };
            Inner.TextChanged += delegate
            {
                if (_phHold) return;
                if (_phOn) { _phOn = false; Inner.ForeColor = Ui.INK; }
                if (_secret && !Inner.UseSystemPasswordChar) Inner.UseSystemPasswordChar = true;
                if (TextChanged != null) TextChanged(this, EventArgs.Empty);
            };
            Inner.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (KeyDown != null) KeyDown(this, e);
            };
            // 滚轮必须转给外层滚动容器：TextBox 自己会把 WM_MOUSEWHEEL 吃掉，
            // 光标停在输入框上时整页就滚不动了（无滚动条的框更没有理由吃它）。
            Inner.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (Inner.ScrollBars != ScrollBars.None) return;
                Control p = Parent;
                while (p != null && !(p is ScrollHost)) p = p.Parent;
                if (p == null || !p.IsHandleCreated) return;
                Point sp = Inner.PointToScreen(e.Location);
                Ui.Native.SendWheel(p.Handle, e.Delta, sp);
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
            get { return _secret; }
            set { _secret = value; Inner.UseSystemPasswordChar = value && !_phOn; }
        }

        /// <summary>内部 TextBox 现在到底有没有在遮点（显示占位文字时会临时不遮）。给自检用。</summary>
        public bool IsMasked
        {
            get { return Inner.UseSystemPasswordChar; }
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
            // 密码框里塞占位文字会被系统按密码盖成"••••"，先把密码掩码关掉，
            // 等下 ClearPh() 再按 _secret 恢复（注意别拿 Inner.UseSystemPasswordChar 当状态，那时它已经是 false）。
            if (_secret) Inner.UseSystemPasswordChar = false;
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
            if (_secret && !Inner.UseSystemPasswordChar) Inner.UseSystemPasswordChar = true;
        }

        /// <summary>
        /// 多行框里当前文字全部铺开需要多高（物理像素，不含 PadY 内边距）。
        /// 关掉原生滚动条之后，靠这个把框和卡片撑高，交给外层滚动容器接管。
        /// </summary>
        public int ContentHeight()
        {
            if (Inner == null) return 0;
            if (!Inner.Multiline) return Inner.PreferredHeight;
            int w = Width - Ui.Px(PadX) * 2;
            if (w < Ui.Px(24)) w = Ui.Px(24);
            string s = Inner.Text;
            if (s == null || s.Length == 0) return Inner.PreferredHeight;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Size sz = TextRenderer.MeasureText(g, s, Inner.Font,
                        new Size(w, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding |
                        TextFormatFlags.TextBoxControl | TextFormatFlags.NoClipping);
                    int h = sz.Height + Ui.Px(4);
                    return h < Inner.PreferredHeight ? Inner.PreferredHeight : h;
                }
            }
            catch { return Inner.PreferredHeight; }
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

    // ==================================================================== 自绘控件

    /// <summary>
    /// 自绘复选框：圆角小方框 + 主色填充 + 白色对勾。
    ///
    /// 原生 CheckBox 是系统主题画的（浅色下是一块带白心的 3D 小方块，深色皮肤下更突兀），
    /// 而且虚线焦点框去不掉，所以全工程统一用它。
    /// 用法和 CheckBox 一样：<c>Checked</c> 属性 + <c>CheckedChanged</c> 事件。
    /// </summary>
    public class Check : Control
    {
        bool _checked;
        bool _hover;

        /// <summary>方框边长（dp）。</summary>
        public int Box = 18;

        public event EventHandler CheckedChanged;

        public Check()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Ui.TEXT_BODY;
            Font = Ui.F(9f);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                Checked = !Checked;
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        public override string ToString() { return Text; }

        /// <summary>文字相对控件左边的缩进（方框 + 间距），排版器要拿它对宽度。</summary>
        public static int TextInset()
        {
            return Ui.Px(27);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Ui.ParentFill(this))) g.FillRectangle(b, ClientRectangle);

            int side = Ui.Px(Box);
            if (side > Height) side = Height;
            if (side < Ui.Px(10)) side = Ui.Px(10);
            var box = new Rectangle(0, (Height - side) / 2, side, side);

            Color fill, edge;
            if (!Enabled)
            {
                fill = _checked ? Ui.LINE : Ui.PANEL;
                edge = Ui.LINE;
            }
            else if (_checked)
            {
                fill = _hover ? Ui.ACCENT_DARK : Ui.ACCENT;
                edge = fill;
            }
            else
            {
                fill = _hover ? Ui.ACCENT_SOFT : Ui.CARD;
                edge = _hover ? Ui.ACCENT : Ui.LINE;
            }

            Ui.FillRound(g, box, Ui.Px(5), fill);
            Ui.StrokeRound(g, box, Ui.Px(5), edge, (_hover || _checked) ? 1.4f : 1f);

            if (_checked)
            {
                Color tick = Enabled ? Ui.WHITE : Ui.SUB;
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(tick, Math.Max(1.6f, side * 0.13f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    Point a = new Point(box.X + (int)(side * 0.24), box.Y + (int)(side * 0.52));
                    Point m = new Point(box.X + (int)(side * 0.43), box.Y + (int)(side * 0.72));
                    Point z = new Point(box.X + (int)(side * 0.78), box.Y + (int)(side * 0.28));
                    g.DrawLines(p, new Point[] { a, m, z });
                }
                g.SmoothingMode = old;
            }

            if (Text != null && Text.Length > 0)
            {
                int tx = box.Right + Ui.Px(9);
                var tr = new Rectangle(tx, 0, Math.Max(1, Width - tx), Height);
                Ui.TextVC(g, Text, Font, Enabled ? ForeColor : Ui.TEXT_DIM, tr);
            }
        }
    }

    /// <summary>
    /// 自绘下拉选择：收起态 = 圆角浅底 + 当前值 + 右侧主色小三角；
    /// 点开弹一个自绘浮层列表（不是系统 ComboBox 的下拉窗口 —— 那个在深色皮肤下是一块系统灰，
    /// 而且带 Win7 的 3D 凸起箭头）。
    ///
    /// 语义照抄 ComboBox 常用子集：<c>SetItems</c> / <c>SelectedIndex</c> / <c>SelectedItem</c>
    /// / <c>SelectedIndexChanged</c>。
    /// </summary>
    public class Select : Control
    {
        string[] _items = new string[0];
        int _index = -1;
        bool _hover;
        SelectPopup _pop;

        public int Radius = 10;
        public int PadX = 11;
        public string Placeholder = "请选择";

        public event EventHandler SelectedIndexChanged;

        public Select()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.F(9.5f);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public string[] Items { get { return _items; } }
        public int ItemCount { get { return _items.Length; } }

        public string SelectedItem
        {
            get { return (_index >= 0 && _index < _items.Length) ? _items[_index] : ""; }
        }

        public int SelectedIndex
        {
            get { return _index; }
            set { SetIndex(value, true); }
        }

        /// <summary>换一组选项，不发事件（先换列表再设选中项，免得半路回调到旧的索引上）。</summary>
        public void SetItems(string[] items)
        {
            _items = items == null ? new string[0] : items;
            _index = -1;
            Invalidate();
        }

        void SetIndex(int i, bool fire)
        {
            if (i < -1) i = -1;
            if (i >= _items.Length) i = _items.Length - 1;
            if (i == _index) return;
            _index = i;
            Invalidate();
            if (fire && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        public void Drop()
        {
            if (_pop != null) { _pop.Close(); return; }
            if (!Enabled || _items.Length == 0) return;
            Form owner = FindForm();
            SelectPopup p = new SelectPopup(this, _items, _index);
            _pop = p;
            p.FormClosed += delegate
            {
                _pop = null;
                Invalidate();
                int pick = p.Picked;
                if (pick >= 0) SetIndex(pick, true);
            };
            if (owner != null) p.Show(owner); else p.Show();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                Drop();
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down)
            {
                if (!Enabled) { base.OnKeyDown(e); return; }
                Focus();
                Drop();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Ui.ParentFill(this))) g.FillRectangle(b, ClientRectangle);

            var r = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, r, Ui.Px(Radius), Enabled ? Ui.PANEL : Ui.BG);
            Ui.StrokeRound(g, r, Ui.Px(Radius), _hover ? Ui.ACCENT : Ui.LINE, _hover ? 1.4f : 1f);

            int arrow = Ui.Px(9);
            int ax = Width - Ui.Px(PadX) - arrow;
            int ay = (Height - Ui.Px(5)) / 2;
            if (ax < Ui.Px(4)) ax = Ui.Px(4);
            using (var b = new SolidBrush(Enabled ? Ui.ACCENT : Ui.TEXT_DIM))
                g.FillPolygon(b, new Point[] {
                    new Point(ax, ay),
                    new Point(ax + arrow, ay),
                    new Point(ax + arrow / 2, ay + Ui.Px(5)) });

            string txt = SelectedItem;
            Color tc = Ui.INK;
            if (txt.Length == 0) { txt = Placeholder; tc = Ui.TEXT_DIM; }
            if (!Enabled) tc = Ui.TEXT_DIM;
            var tr = new Rectangle(Ui.Px(PadX), 0,
                Math.Max(1, Width - Ui.Px(PadX) * 2 - arrow - Ui.Px(8)), Height);
            Ui.TextVC(g, txt, Font, tc, tr);
        }
    }

    /// <summary>下拉浮层：自绘卡片列表，点外面或按 Esc 关掉。</summary>
    class SelectPopup : Form
    {
        const int MaxRows = 8;

        readonly string[] _items;
        int _hot, _sel, _scroll;
        readonly int _rowH;

        /// <summary>被选中的索引；-1 表示用户没选（点了外面 / 按了 Esc）。</summary>
        public int Picked = -1;

        public SelectPopup(Select src, string[] items, int selected)
        {
            _items = items;
            _sel = selected;
            _hot = selected;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.CARD;
            ForeColor = Ui.INK;
            Font = Ui.F(9.5f);
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _rowH = Ui.Px(30);
            int rows = items.Length;
            int vis = MaxRows;
            if (rows < vis) vis = rows;
            if (vis < 1) vis = 1;
            int w = src.Width;
            if (w < Ui.Px(160)) w = Ui.Px(160);
            ClientSize = new Size(w, vis * _rowH + Ui.Px(8));

            if (_sel > vis - 1) _scroll = _sel - vis + 1;
            if (_scroll < 0) _scroll = 0;

            Point at = src.PointToScreen(new Point(0, src.Height + Ui.Px(4)));
            Rectangle wa = Screen.FromControl(src).WorkingArea;
            if (at.Y + Height > wa.Bottom) at = src.PointToScreen(new Point(0, -Height - Ui.Px(4)));
            if (at.X + Width > wa.Right) at.X = wa.Right - Width;
            if (at.X < wa.Left) at.X = wa.Left;
            if (at.Y < wa.Top) at.Y = wa.Top;
            Location = at;
        }

        int VisibleRows
        {
            get
            {
                int vis = (ClientSize.Height - Ui.Px(8)) / _rowH;
                if (vis > _items.Length) vis = _items.Length;
                return vis < 1 ? 1 : vis;
            }
        }

        int IndexAt(int y)
        {
            int i = (y - Ui.Px(4)) / _rowH;
            if (i < 0) return -1;
            i += _scroll;
            if (i >= _items.Length) return -1;
            return i;
        }

        void Pick(int i)
        {
            if (i < 0 || i >= _items.Length) return;
            Picked = i;
            Close();
        }

        void MoveHot(int d)
        {
            int i = _hot + d;
            if (i < 0) i = 0;
            if (i >= _items.Length) i = _items.Length - 1;
            _hot = i;
            int vis = VisibleRows;
            if (_hot < _scroll) _scroll = _hot;
            if (_hot > _scroll + vis - 1) _scroll = _hot - vis + 1;
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Activate(); Focus(); } catch { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.Down) { MoveHot(1); return; }
            if (e.KeyCode == Keys.Up) { MoveHot(-1); return; }
            if (e.KeyCode == Keys.Enter) { Pick(_hot); return; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != _hot) { _hot = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Pick(IndexAt(e.Y));
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = _items.Length - VisibleRows;
            if (max > 0)
            {
                if (e.Delta > 0) _scroll--; else _scroll++;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            var full = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, full, Ui.Px(10), Ui.CARD);
            Ui.StrokeRound(g, full, Ui.Px(10), Ui.LINE, 1f);

            int pad = Ui.Px(4);
            int vis = VisibleRows;
            for (int r = 0; r < vis; r++)
            {
                int idx = _scroll + r;
                if (idx >= _items.Length) break;
                var row = new Rectangle(pad, pad + r * _rowH, Width - pad * 2, _rowH);
                bool hot = (idx == _hot);
                bool on = (idx == _sel);
                if (hot || on)
                    Ui.FillRound(g, new Rectangle(row.X, row.Y + 1, row.Width, row.Height - 2),
                        Ui.Px(7), Ui.ACCENT_SOFT);
                Ui.TextVC(g, _items[idx], Font, (hot || on) ? Ui.ACCENT : Ui.INK,
                    new Rectangle(row.X + Ui.Px(10), row.Y,
                        Math.Max(1, row.Width - Ui.Px(16)), row.Height));
            }

            if (_items.Length > vis)
            {
                int trackH = Height - pad * 2;
                int th = (int)((long)trackH * vis / _items.Length);
                if (th < Ui.Px(18)) th = Ui.Px(18);
                int max = _items.Length - vis;
                int top = pad + (trackH - th) * _scroll / Math.Max(1, max);
                Ui.FillRound(g, new Rectangle(Width - Ui.Px(6), top, Ui.Px(3), th), Ui.Px(2), Ui.SUB);
            }
        }
    }

    // ==================================================================== 可滚动内容区

    /// <summary>
    /// 自绘滚动容器。
    ///
    /// 不用 <c>Panel.AutoScroll</c> 的原因：它按子控件的 Anchor/Dock 算总高，卡片一改宽就容易
    /// 冒出原生横向条；而且那条系统灰箭头滚动条跟自绘卡片摆在一起非常出土（深色皮肤下更明显）。
    /// 这里只有自己画的细拇指：Ui.PANEL 底槽 + Ui.SUB 拇指，鼠标进面板才显形。
    ///
    /// <c>AutoLayout = true</c>（默认）：<c>Add()</c> 进来的控件按登记顺序竖着摞，宽度按容器算；
    /// <c>AutoLayout = false</c>：控件自己用绝对坐标摆，容器只负责整体位移 + 画条。
    /// </summary>
    public class ScrollHost : Control
    {
        readonly System.Collections.Generic.List<Control> _cards = new System.Collections.Generic.List<Control>();
        readonly Panel _body;
        int _scroll, _contentH, _thumbH, _thumbTop, _dragOffset, _trackH;
        int _scrollX, _contentW, _hThumbW, _hThumbLeft, _hDragOffset, _hTrackW;
        bool _dragging, _hDragging, _trackHot;

        public int Gap = 14;                        // 卡片间距（dp）
        public Padding BodyPadding = new Padding(16, 14, 16, 16);
        public int MaxContentWidth;                 // >0 时限宽 + 居中
        public bool AutoLayout = true;
        public bool Horizontal;                     // 允许横向滚动（原生 AutoScroll 的兜底）

        public event EventHandler ScrollChanged;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.BG;

            _body = new Panel();
            _body.BackColor = Ui.BG;
            Controls.Add(_body);

            MouseDown += OnTrackDown;
            MouseMove += OnTrackMove;
            MouseUp += delegate { TrackUp(); };
            MouseLeave += delegate { _dragging = false; _hDragging = false; };
        }

        public int ContentHeight { get { return _contentH; } }
        public int ContentWidth { get { return _contentW; } }

        /// <summary>真正装内容的容器。AutoLayout=false 时把子控件加到这里（别加到 ScrollHost 本身）。</summary>
        public Panel ContentHost { get { return _body; } }
        public int ScrollY { get { return _scroll; } }
        public int ScrollX { get { return _scrollX; } }
        public int MaxScroll { get { return Math.Max(0, _contentH - ClientSize.Height); } }
        public int MaxScrollX { get { return Math.Max(0, _contentW - ClientSize.Width); } }

        public void Add(Control c)
        {
            if (c == null || _cards.Contains(c)) return;
            if (c.Parent != _body) _body.Controls.Add(c);
            _cards.Add(c);
        }

        public void ClearContent()
        {
            _cards.Clear();
            _body.Controls.Clear();
            _contentH = 0;
            _contentW = 0;
            _scroll = 0;
            _scrollX = 0;
            _body.Top = 0;
            _body.Left = 0;
            _trackH = 0;
            _hTrackW = 0;
        }

        /// <summary>按当前宽度摆好所有卡片，算出内容总高和滚动条。</summary>
        public void Recalc()
        {
            if (_body == null) return;
            Ui.Native.Freeze(_body);
            try
            {
                // 正文面板 _body 是 ScrollHost 的子控件，子控件永远盖在父控件的 OnPaint 之上，
                // 所以必须从 _body 里让出一条给滑块，否则滑块画了也看不见。
                int innerW = ClientSize.Width - Ui.Px(BarGutter);
                if (innerW < Ui.Px(40)) innerW = Ui.Px(40);
                int innerH = Horizontal ? ClientSize.Height - Ui.Px(BarGutter) : ClientSize.Height;
                if (innerH < Ui.Px(40)) innerH = Ui.Px(40);

                if (AutoLayout)
                {
                    int x = BodyPadding.Left;
                    int w = innerW - BodyPadding.Left - BodyPadding.Right;
                    if (MaxContentWidth > 0 && w > MaxContentWidth)
                    {
                        w = MaxContentWidth;
                        x = (innerW - w) / 2;             // 大屏居中，别缩在左边一条
                    }
                    if (w > 0)
                    {
                        int y = BodyPadding.Top;
                        for (int i = 0; i < _cards.Count; i++)
                        {
                            Control c = _cards[i];
                            if (c == null || c.IsDisposed) continue;
                            c.SetBounds(x, y, w, Math.Max(1, c.Height));
                            y += c.Height + Ui.Px(Gap);
                        }
                        _contentH = _cards.Count > 0
                            ? y - Ui.Px(Gap) + BodyPadding.Bottom
                            : BodyPadding.Bottom;
                        _contentW = innerW;
                    }
                }
                else
                {
                    // 绝对定位模式：直接量 _body 里的子控件（它们没经过 Add() 登记）
                    MeasureChildren();
                }

                int bw = Horizontal && _contentW > innerW ? _contentW : innerW;
                if (bw < 1) bw = 1;
                _body.SetBounds(-_scrollX, -_scroll, bw, Math.Max(_contentH, innerH));
            }
            finally { Ui.Native.Unfreeze(_body); }

            if (_scroll > MaxScroll) _scroll = MaxScroll;
            if (_scrollX > MaxScrollX) _scrollX = MaxScrollX;
            _body.Left = -_scrollX;
            _body.Top = -_scroll;
            ComputeThumb();
            Invalidate();
        }

        /// <summary>滚动条的纵向让位（dp）：正文面板要窄这么多，滑块才露得出来。</summary>
        public const int BarGutter = 13;

        void TrackUp()
        {
            _dragging = false;
            _hDragging = false;
        }

        internal void SetTrackHot(bool hot)
        {
            if (_trackHot == hot) return;
            _trackHot = hot;
            Invalidate();
        }

        /// <summary>滚轮逻辑单独抽出来，外面（输入框）也要能触发。</summary>
        internal void WheelScroll(MouseEventArgs e)
        {
            if (((ModifierKeys & Keys.Shift) == Keys.Shift) && MaxScrollX > 0)
                ScrollToX(_scrollX + (e.Delta > 0 ? -Ui.Px(70) : Ui.Px(70)));
            else if (MaxScroll > 0)
                ScrollTo(_scroll + (e.Delta > 0 ? -Ui.Px(70) : Ui.Px(70)));
        }

        public void ScrollTo(int y)
        {
            int max = MaxScroll;
            if (y < 0) y = 0;
            if (y > max) y = max;
            if (y != _scroll)
            {
                _scroll = y;
                _body.Top = -_scroll;
                ComputeThumb();
                Invalidate();
                if (ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
            }
        }

        public void ScrollToX(int x)
        {
            int max = MaxScrollX;
            if (x < 0) x = 0;
            if (x > max) x = max;
            if (x != _scrollX)
            {
                _scrollX = x;
                _body.Left = -_scrollX;
                ComputeThumb();
                Invalidate();
                if (ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            WheelScroll(e);
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalc();
        }

        // -------- 自绘滚动条 --------

        /// <summary>量一遍所有子控件的右下边界（绝对定位模式用）。</summary>
        void MeasureChildren()
        {
            int maxR = 0, maxB = 0;
            foreach (Control c in _body.Controls)
            {
                if (c == null || c.IsDisposed) continue;
                if (c.Right > maxR) maxR = c.Right;
                if (c.Bottom > maxB) maxB = c.Bottom;
            }
            _contentW = maxR;
            _contentH = maxB + BodyPadding.Bottom;
        }

        void ComputeThumb()
        {
            int trackH = ClientSize.Height - Ui.Px(12);
            if (MaxScroll <= 0 || trackH < Ui.Px(40)) { _trackH = 0; }
            else
            {
                _trackH = Math.Max(Ui.Px(30), (int)((long)trackH * ClientSize.Height / Math.Max(1, _contentH)));
                _thumbH = _trackH;
                _thumbTop = Ui.Px(6) + (trackH - _trackH) * _scroll / Math.Max(1, MaxScroll);
            }

            int trackW = ClientSize.Width - Ui.Px(12);
            if (!Horizontal || MaxScrollX <= 0 || trackW < Ui.Px(40)) { _hTrackW = 0; }
            else
            {
                _hTrackW = Math.Max(Ui.Px(30), (int)((long)trackW * ClientSize.Width / Math.Max(1, _contentW)));
                _hThumbW = _hTrackW;
                _hThumbLeft = Ui.Px(6) + (trackW - _hTrackW) * _scrollX / Math.Max(1, MaxScrollX);
            }
        }

        int TrackX() { return ClientSize.Width - Ui.Px(11); }
        int TrackY() { return ClientSize.Height - Ui.Px(11); }

        void OnTrackDown(object s, MouseEventArgs e)
        {
            if (_trackH > 0 && e.X >= TrackX() && !(Horizontal && _hTrackW > 0 && e.Y >= TrackY()))
            {
                if (e.Y >= _thumbTop && e.Y <= _thumbTop + _trackH)
                {
                    _dragging = true;
                    _dragOffset = e.Y - _thumbTop;
                }
                else ScrollTo(_scroll + (e.Y < _thumbTop ? -1 : 1) * ClientSize.Height * 4 / 5);
                return;
            }
            if (_hTrackW > 0 && e.Y >= TrackY())
            {
                if (e.X >= _hThumbLeft && e.X <= _hThumbLeft + _hTrackW)
                {
                    _hDragging = true;
                    _hDragOffset = e.X - _hThumbLeft;
                }
                else ScrollToX(_scrollX + (e.X < _hThumbLeft ? -1 : 1) * ClientSize.Width * 4 / 5);
            }
        }

        void OnTrackMove(object s, MouseEventArgs e)
        {
            if (_dragging && _trackH > 0)
            {
                int room = ClientSize.Height - Ui.Px(12) - _trackH;
                int max = MaxScroll;
                if (room > 0 && max > 0)
                {
                    int y = e.Y - _dragOffset - Ui.Px(6);
                    if (y < 0) y = 0;
                    if (y > room) y = room;
                    ScrollTo(y * max / room);
                }
            }
            if (_hDragging && _hTrackW > 0)
            {
                int room = ClientSize.Width - Ui.Px(12) - _hTrackW;
                int max = MaxScrollX;
                if (room > 0 && max > 0)
                {
                    int x = e.X - _hDragOffset - Ui.Px(6);
                    if (x < 0) x = 0;
                    if (x > room) x = room;
                    ScrollToX(x * max / room);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 绝对定位模式没人保证调过 Recalc（比如正文是边加边长大的弹窗），
            // 画之前重量一次，滑块才不会平白无故消失。
            if (!AutoLayout) MeasureChildren();
            ComputeThumb();

            Graphics g = e.Graphics;
            if (_trackH > 0)
            {
                // 底槽用极淡的一层，滑块的对比度才够
                Ui.FillRound(g,
                    new Rectangle(TrackX() + Ui.Px(2), Ui.Px(6), Ui.Px(6), ClientSize.Height - Ui.Px(12)),
                    Ui.Px(3), _trackHot ? Ui.PANEL : BackColor);
                Ui.FillRound(g,
                    new Rectangle(TrackX() + Ui.Px(_trackHot ? 2 : 3), _thumbTop, Ui.Px(_trackHot ? 6 : 4), _trackH),
                    Ui.Px(_trackHot ? 3 : 2), _trackHot ? Ui.ACCENT : Ui.SUB);
            }
            if (_hTrackW > 0)
            {
                Ui.FillRound(g,
                    new Rectangle(Ui.Px(6), TrackY() + Ui.Px(2), ClientSize.Width - Ui.Px(12) - Ui.Px(BarGutter), Ui.Px(6)),
                    Ui.Px(3), _trackHot ? Ui.PANEL : BackColor);
                Ui.FillRound(g,
                    new Rectangle(_hThumbLeft, TrackY() + Ui.Px(_trackHot ? 2 : 3), _hThumbW, Ui.Px(_trackHot ? 6 : 4)),
                    Ui.Px(_trackHot ? 3 : 2), _trackHot ? Ui.ACCENT : Ui.SUB);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { SetTrackHot(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            TrackUp();
            SetTrackHot(false);
            base.OnMouseLeave(e);
        }
    }
}
