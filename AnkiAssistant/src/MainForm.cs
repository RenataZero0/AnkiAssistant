using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 主窗口：自绘标题栏 + 左侧导航 + 内容区 + 底部状态栏。
    ///
    /// 窗体是 <see cref="FormBorderStyle.None"/>：系统那套 Aero/Win7 玻璃标题栏和
    /// 四边描边全部去掉，标题、窗口按钮、边框都自己画，这样深色皮肤才是一条心。
    /// 代价是窗口不能再靠系统做拖动/缩放，所以：
    ///   · 拖动 —— 标题条上按下鼠标交给系统的标题栏拖动循环（<see cref="Ui.Native.BeginMove"/>），
    ///             这样 Aero Snap 和「最大化时拖动自动还原」都是系统原生行为；
    ///   · 缩放 —— 根面板（<see cref="RootPanel"/>）在离边 6dp 内按下时自己接管，见 BeginResize。
    /// </summary>
    public class MainForm : Form
    {
        public static MainForm Instance;

        // ---- WM_NCHITTEST 返回值 ----
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        const int WM_NCHITTEST = 0x0084;

        RootPanel _top, _rail, _content;
        RootPanel _status;
        RailButton _bCreate, _bBrowse, _bSettings;
        AvatarBadge _avatar;
        CapButton _btnMin, _btnMax, _btnClose;
        Icon _brand;
        Rectangle _verRect;
        string _statusMsg = "正在检查 Anki…";
        bool _statusWarn;

        int _ht;
        Point _rsMouse;
        Rectangle _rsBounds;
        long _lastMaxToggle;

        public CreateView Create;
        public BrowseView Browse;
        public SettingsView Settings;

        public MainForm()
        {
            Instance = this;
            Text = "Anki 助手";
            BackColor = Ui.BG;
            Font = Ui.F(9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            Padding = new Padding(Ui.Px(1));    // 留 1px 给自己画描边
            ClientSize = new Size(Ui.Px(1180), Ui.Px(830));
            MinimumSize = new Size(Ui.Px(940), Ui.Px(660));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = AppIcon.Get(32); } catch { }

            BuildTop();
            BuildRail();
            BuildStatus();
            BuildContent();

            Controls.Add(_content);
            Controls.Add(_rail);
            Controls.Add(_top);
            Controls.Add(_status);

            ShowPage(0);
            Shown += delegate
            {
                ApplyMaximizedBounds();
                try { SyncState.Refresh(); } catch { }
                RefreshAvatar();   // 本地缓存 → Anki 媒体库 → Gravatar，后台跑
            };
            FormClosing += delegate
            {
                Store.Set("win.w", Width.ToString());
                Store.Set("win.h", Height.ToString());
                // 内置引擎的收藏库必须显式关掉：rslib 在同一份收藏库上只允许一个打开者，
                // 漏掉这一步，下次启动就会报「Anki already open, or media currently syncing.」
                try { AnkiConn.Close(); } catch { }
            };
        }

        // ===== 无边框窗体仍然要能拖动 / 缩放 =====

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyMaximizedBounds();
        }

        void ApplyMaximizedBounds()
        {
            // 不设的话，无边框窗口最大化会盖住任务栏
            try { MaximizedBounds = Screen.FromControl(this).WorkingArea; } catch { }
        }

        /// <summary>命中测试（屏幕坐标）。最大化时不返回缩放边，因为那时没有边。</summary>
        public int NCHitTest(Point screenPt)
        {
            if (WindowState == FormWindowState.Maximized) return HTCLIENT;
            Point p = PointToClient(screenPt);
            int g = Ui.Px(6);
            bool l = p.X < g, r = p.X >= ClientSize.Width - g;
            bool t = p.Y < g, b = p.Y >= ClientSize.Height - g;
            if (l && t) return HTTOPLEFT;
            if (r && t) return HTTOPRIGHT;
            if (l && b) return HTBOTTOMLEFT;
            if (r && b) return HTBOTTOMRIGHT;
            if (l) return HTLEFT;
            if (r) return HTRIGHT;
            if (t) return HTTOP;
            if (b) return HTBOTTOM;
            return HTCLIENT;
        }

        /// <summary>是不是在可拖动的标题条上（顶部那条，扣掉最上面 6dp 的缩放带）。</summary>
        public bool IsCaption(Control c, Point local)
        {
            if (c != _top) return false;
            if (WindowState == FormWindowState.Maximized) return true;
            return local.Y >= Ui.Px(6);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                m.Result = (IntPtr)NCHitTest(new Point(sx, sy));
                return;
            }
            base.WndProc(ref m);
        }

        public bool Resizing { get { return _ht != 0; } }

        /// <summary>开始拖边缩放（根面板在离边 6dp 内按下时调这里）。</summary>
        public void BeginResize(int ht)
        {
            if (ht == HTCLIENT || WindowState == FormWindowState.Maximized) return;
            _ht = ht;
            _rsMouse = Cursor.Position;
            _rsBounds = Bounds;
            try { Capture = true; } catch { }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_ht != 0) { ResizeFollow(); return; }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_ht != 0)
            {
                _ht = 0;
                try { Capture = false; } catch { }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_ht != 0 && !Capture) _ht = 0;
        }

        void ResizeFollow()
        {
            Point p = Cursor.Position;
            int dx = p.X - _rsMouse.X, dy = p.Y - _rsMouse.Y;
            int x = _rsBounds.X, y = _rsBounds.Y, w = _rsBounds.Width, h = _rsBounds.Height;
            int minW = Math.Max(Ui.Px(320), MinimumSize.Width);
            int minH = Math.Max(Ui.Px(240), MinimumSize.Height);

            bool L = _ht == HTLEFT || _ht == HTTOPLEFT || _ht == HTBOTTOMLEFT;
            bool R = _ht == HTRIGHT || _ht == HTTOPRIGHT || _ht == HTBOTTOMRIGHT;
            bool T = _ht == HTTOP || _ht == HTTOPLEFT || _ht == HTTOPRIGHT;
            bool B = _ht == HTBOTTOM || _ht == HTBOTTOMLEFT || _ht == HTBOTTOMRIGHT;

            if (L) { x += dx; w -= dx; if (w < minW) { x = _rsBounds.Right - minW; w = minW; } }
            if (R) { w += dx; if (w < minW) w = minW; }
            if (T) { y += dy; h -= dy; if (h < minH) { y = _rsBounds.Bottom - minH; h = minH; } }
            if (B) { h += dy; if (h < minH) h = minH; }

            Bounds = new Rectangle(x, y, w, h);
        }

        /// <summary>鼠标经过边/角时换光标。</summary>
        public void ApplyResizeCursor(Control c, Point screenPt)
        {
            if (c == null) return;
            int ht = NCHitTest(screenPt);
            Cursor cur;
            if (ht == HTLEFT || ht == HTRIGHT) cur = Cursors.SizeWE;
            else if (ht == HTTOP || ht == HTBOTTOM) cur = Cursors.SizeNS;
            else if (ht == HTTOPLEFT || ht == HTBOTTOMRIGHT) cur = Cursors.SizeNWSE;
            else if (ht == HTTOPRIGHT || ht == HTBOTTOMLEFT) cur = Cursors.SizeNESW;
            else cur = Cursors.Default;
            if (c.Cursor != cur) c.Cursor = cur;
        }

        /// <summary>标题条双击 = 最大化/还原（两次判定之间防抖，免得连点两次又切回来）。</summary>
        public void ToggleMaximize()
        {
            long now = DateTime.Now.Ticks;
            if (now - _lastMaxToggle < TimeSpan.TicksPerMillisecond * 350) return;
            _lastMaxToggle = now;
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else { ApplyMaximizedBounds(); WindowState = FormWindowState.Maximized; }
            SyncMaxGlyph();
        }

        void SyncMaxGlyph()
        {
            if (_btnMax == null) return;
            _btnMax.Restore = (WindowState == FormWindowState.Maximized);
            _btnMax.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncMaxGlyph();
        }

        /// <summary>不要虚线焦点框。</summary>
        protected override bool ShowFocusCues { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(Ui.BG)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Ui.LINE, 1f))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        // ===== 顶栏 =====

        void BuildTop()
        {
            _top = new RootPanel { Dock = DockStyle.Top, Height = Ui.Px(60), BackColor = Ui.CARD };
            // 走 Icon 本体，不要 ToBitmap()：资源里那份 32bpp 图标
            // 经 ToBitmap() 出来是一团彩色噪点（GDI+ 读 alpha 的老毛病）。
            try { _brand = AppIcon.Get(Ui.Px(24)); } catch { _brand = null; }

            _top.Paint += delegate(object s, PaintEventArgs e) { PaintTop(e.Graphics); };

            _btnMin = new CapButton { Kind = 0 };
            _btnMax = new CapButton { Kind = 1 };
            _btnClose = new CapButton { Kind = 2 };
            _btnClose.Click += delegate { Close(); };
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            _btnMax.Click += delegate { ToggleMaximize(); };
            _top.Controls.Add(_btnMin);
            _top.Controls.Add(_btnMax);
            _top.Controls.Add(_btnClose);

            _avatar = new AvatarBadge();
            // 头像角标就是「账号与头像」的入口（跟 Android 版一致）：点开换图 / 填邮箱。
            _avatar.Click += delegate
            {
                AvatarDialog.Show(this);
                RefreshAvatar();    // 弹窗里可能刚换了图，关掉之后重解析一次角标
            };
            _top.Controls.Add(_avatar);

            _top.Resize += delegate { LayoutTop(); };
            LayoutTop();
        }

        void LayoutTop()
        {
            int bw = Ui.Px(46);
            int h = _top.Height;
            _btnClose.Bounds = new Rectangle(_top.Width - bw, 0, bw, h);
            _btnMax.Bounds = new Rectangle(_top.Width - bw * 2, 0, bw, h);
            _btnMin.Bounds = new Rectangle(_top.Width - bw * 3, 0, bw, h);

            int right = _top.Width - bw * 3;
            _verRect = new Rectangle(right - Ui.Px(12) - Ui.Px(64), Ui.Px(18), Ui.Px(64), Ui.Px(24));
            _avatar.Bounds = new Rectangle(_verRect.Left - Ui.Px(12) - Ui.Px(36), Ui.Px(12),
                Ui.Px(36), Ui.Px(36));
        }

        void PaintTop(Graphics g)
        {
            var head = new Rectangle(0, 0, _top.Width, _top.Height);

            // 品牌块：主色浅底 + 应用图标
            var mark = new Rectangle(Ui.Px(18), Ui.Px(14), Ui.Px(32), Ui.Px(32));
            Ui.FillRound(g, mark, Ui.Px(9), Ui.ACCENT_SOFT);
            if (_brand != null)
            {
                try
                {
                    g.DrawIcon(_brand, new Rectangle(mark.X + Ui.Px(6), mark.Y + Ui.Px(6),
                        Ui.Px(20), Ui.Px(20)));
                }
                catch { Ui.TextC(g, "A", Ui.F(13f, true), Ui.ACCENT, mark.X + mark.Width / 2, mark.Y + mark.Height / 2); }
            }
            else
            {
                Ui.TextC(g, "A", Ui.F(13f, true), Ui.ACCENT, mark.X + mark.Width / 2, mark.Y + mark.Height / 2);
            }

            Ui.TextVC(g, "Anki 助手", Ui.F(13f, true), Ui.INK,
                new Rectangle(Ui.Px(62), 0, Math.Max(Ui.Px(80), _verRect.Left - Ui.Px(72)), _top.Height));

            // 版本徽标
            Ui.FillRound(g, _verRect, Ui.Px(12), Ui.PANEL);
            Ui.StrokeRound(g, _verRect, Ui.Px(12), Ui.LINE, 1f);
            Ui.TextC(g, GitHub.Clean(GitHub.VersionTag), Ui.F(8.5f), Ui.SUB,
                _verRect.X + _verRect.Width / 2, _verRect.Y + _verRect.Height / 2);

            using (var p = new Pen(Ui.ACCENT, 1.4f))
                g.DrawLine(p, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        }

        // ===== 侧栏 =====

        void BuildRail()
        {
            _rail = new RootPanel { Dock = DockStyle.Left, Width = Ui.Px(92), BackColor = Ui.CARD, Padding = new Padding(Ui.Px(10), Ui.Px(14), Ui.Px(10), Ui.Px(10)) };
            _rail.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Ui.LINE))
                    e.Graphics.DrawLine(p, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height);
            };

            _bCreate = new RailButton("create", "制卡") { Dock = DockStyle.Top, Height = Ui.Px(66) };
            _bBrowse = new RailButton("browse", "浏览") { Dock = DockStyle.Top, Height = Ui.Px(66) };
            _bSettings = new RailButton("settings", "设置") { Dock = DockStyle.Top, Height = Ui.Px(66) };
            _bCreate.Click += delegate { ShowPage(0); };
            _bBrowse.Click += delegate { ShowPage(1); };
            _bSettings.Click += delegate { ShowPage(2); };

            _rail.Controls.Add(_bSettings);
            _rail.Controls.Add(_bBrowse);
            _rail.Controls.Add(_bCreate);
        }

        // ===== 底部状态栏 =====

        void BuildStatus()
        {
            _status = new RootPanel { Dock = DockStyle.Bottom, Height = Ui.Px(34), BackColor = Ui.CARD };
            _status.Cursor = Cursors.Hand;
            _status.Click += delegate { ShowPage(2); };
            _status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Ui.LINE))
                    e.Graphics.DrawLine(p, 0, 0, _status.Width, 0);
                Ui.TextVC(e.Graphics, _statusMsg, Ui.F(8.5f), _statusWarn ? Ui.RED : Ui.SUB,
                    new Rectangle(Ui.Px(18), 0, Math.Max(Ui.Px(60), _status.Width - Ui.Px(24)), _status.Height));
            };
        }

        void BuildContent()
        {
            _content = new RootPanel { Dock = DockStyle.Fill, BackColor = Ui.BG, Padding = new Padding(Ui.Px(16), Ui.Px(14), Ui.Px(16), Ui.Px(12)) };
            Create = new CreateView { Dock = DockStyle.Fill, Visible = false };
            Browse = new BrowseView { Dock = DockStyle.Fill, Visible = false };
            Settings = new SettingsView { Dock = DockStyle.Fill, Visible = false };
            _content.Controls.Add(Settings);
            _content.Controls.Add(Browse);
            _content.Controls.Add(Create);
        }

        // ===== 切页 =====
        public void ShowPage(int i)
        {
            _bCreate.Selected = i == 0; _bBrowse.Selected = i == 1; _bSettings.Selected = i == 2;
            _bCreate.Invalidate(); _bBrowse.Invalidate(); _bSettings.Invalidate();
            Create.Visible = i == 0; Browse.Visible = i == 1; Settings.Visible = i == 2;
            if (i == 0) Create.Activate();
            else if (i == 1) Browse.Activate();
            else Settings.Activate();
        }

        /// <summary>底部状态栏文字（各页面都会改它）。</summary>
        public void SetStatus(string text, bool warn = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { SetStatus(text, warn); }); return; }
            _statusMsg = text;
            _statusWarn = warn;
            if (_status != null) _status.Invalidate();
        }

        /// <summary>引擎状态变了以后刷新角标和状态栏。</summary>
        public void NotifySyncState()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { NotifySyncState(); }); return; }
            _avatar.Invalidate();
            _avatar.Refresh2();
            SetStatus(SyncState.StatusText(), SyncState.Kind == SyncKind.Error);
        }

        /// <summary>重新解析头像（本地缓存 → Anki 媒体库 → Gravatar）并重画角标。</summary>
        public void RefreshAvatar()
        {
            if (IsDisposed) return;
            AvatarStore.ResolveAsync(_avatar, (MethodInvoker)delegate
            {
                if (_avatar != null) _avatar.Refresh2();
            });
        }

        /// <summary>AnkiWeb 账号与同步（顶栏头像 / 状态栏点进来）。</summary>
        public void ShowAccountDialog()
        {
            WebDialog.Show(this);
        }
    }

    /// <summary>
    /// 主窗口的根面板。无边框窗体没有非客户区，系统的 WM_NCHITTEST 也不会落到窗体自己身上
    /// （命中的是盖在最上面的子面板），所以这四个贴边的面板统一在按下时把拖动/缩放接过来。
    /// </summary>
    public class RootPanel : Panel
    {
        public RootPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            MainForm f = FindForm() as MainForm;
            if (f != null && e.Button == MouseButtons.Left)
            {
                if (f.IsCaption(this, e.Location))
                {
                    if (e.Clicks >= 2) f.ToggleMaximize();
                    else Ui.Native.BeginMove(f);      // 系统标题栏拖动：Aero Snap + 最大化拖拽还原
                    return;
                }
                int ht = f.NCHitTest(PointToScreen(e.Location));
                if (ht != MainForm.HTCLIENT) { f.BeginResize(ht); return; }
            }
            base.OnMouseDown(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            MainForm f = FindForm() as MainForm;
            if (f != null && f.IsCaption(this, PointToClient(Cursor.Position)))
            {
                f.ToggleMaximize();   // ToggleMaximize 内部有 350ms 防抖，不会把上面那次抵消掉
                return;
            }
            base.OnDoubleClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            MainForm f = FindForm() as MainForm;
            if (f != null && !f.Resizing) f.ApplyResizeCursor(this, PointToScreen(e.Location));
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            MainForm f = FindForm() as MainForm;
            if (f != null && !f.Resizing && Cursor != Cursors.Default) Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }
    }

    /// <summary>自绘的窗口按钮（最小化 / 最大化-还原 / 关闭），图标用 GDI+ 路径画。</summary>
    public class CapButton : Control
    {
        /// <summary>0 = 最小化，1 = 最大化/还原，2 = 关闭。</summary>
        public int Kind;

        /// <summary>Kind==1 时画「还原」图标而不是「最大化」。</summary>
        public bool Restore;

        bool _hover;

        public CapButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.CARD;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            bool close = Kind == 2;
            using (var b = new SolidBrush(_hover ? (close ? Ui.RED : Ui.ACCENT_SOFT) : Ui.CARD))
                g.FillRectangle(b, ClientRectangle);

            Color ink = _hover ? (close ? Ui.WHITE : Ui.ACCENT) : Ui.SUB;
            int s = Ui.Px(10);
            int x = (Width - s) / 2, y = (Height - s) / 2;
            int o = Ui.Px(3);

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(ink, 1.3f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                if (Kind == 0)
                {
                    g.DrawLine(p, x, y + s / 2, x + s, y + s / 2);
                }
                else if (Kind == 2)
                {
                    g.DrawLine(p, x, y, x + s, y + s);
                    g.DrawLine(p, x + s, y, x, y + s);
                }
                else if (!Restore)
                {
                    g.DrawRectangle(p, x, y, s, s);
                }
                else
                {
                    g.DrawRectangle(p, x + o, y, s - o, s - o);
                    g.DrawLine(p, x, y + o, x, y + s);
                    g.DrawLine(p, x, y + s, x + s - o, y + s);
                }
            }
            g.SmoothingMode = old;
        }
    }
}
