using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 统一的弹窗外壳：白底卡片、加粗标题、可选说明、正文区、右下角按钮。
    ///
    /// 内容太高时正文区自己滚（内容控件负责），弹窗本身不会超出屏幕 70% 高。
    /// 所有弹窗都从这里派生，别再用系统 MessageBox —— 那个在中文 Windows 上
    /// 字体和按钮风格跟主界面完全不是一套。
    /// </summary>
    public class DlgForm : Form
    {
        protected DlgHead Head;
        /// <summary>正文容器（绝对定位）。它是 Scroller 内部那块会滚的面板。</summary>
        public Panel Body;
        /// <summary>正文外面的自绘滚动条外壳（替代 Panel.AutoScroll 的经典滚动条）。</summary>
        public ScrollHost Scroller;
        protected FlowLayoutPanel Foot;
        public int Pad = 24;
        CapButton _close;

        public DlgForm(string title, string sub, int widthDp)
        {
            // 跟主窗口一样去掉系统标题栏：标题、关闭按钮、边框都自己画
            FormBorderStyle = FormBorderStyle.None;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.CARD;
            ForeColor = Ui.INK;
            Font = Ui.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Padding = new Padding(Ui.Px(1));   // 留 1px 画描边
            ClientSize = new Size(Ui.Px(widthDp), Ui.Px(120));

            Head = new DlgHead { Dock = DockStyle.Top, BackColor = Ui.CARD, Height = Ui.Px(56) };
            Head.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Text(g, title, Ui.F(13f, true), Ui.INK, Ui.Px(Pad), Ui.Px(20));
                if (!string.IsNullOrEmpty(sub))
                    Ui.Text(g, sub, Ui.F(9f), Ui.SUB, Ui.Px(Pad), Ui.Px(44));
                using (var p = new Pen(Ui.ACCENT, 1.2f))
                    g.DrawLine(p, 0, Head.Height - 1, Head.Width, Head.Height - 1);
            };
            if (!string.IsNullOrEmpty(sub)) Head.Height = Ui.Px(74);

            _close = new CapButton { Kind = 2 };
            _close.Click += delegate
            {
                if (DialogResult == DialogResult.None) DialogResult = DialogResult.Cancel;
                Close();
            };
            Head.Controls.Add(_close);
            Head.Resize += delegate { LayoutHead(); };
            LayoutHead();

            Foot = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Ui.CARD,
                Height = Ui.Px(58),
                Padding = new Padding(Ui.Px(10), Ui.Px(8), Ui.Px(Pad - 6), Ui.Px(10)),
                WrapContents = false
            };
            // 正文外面套一层自绘滚动容器。以前是 Body.AutoScroll = true，
            // 内容一高就露出系统那条带灰色箭头的滚动条，跟自绘的界面格格不入。
            Scroller = new ScrollHost();
            Scroller.Dock = DockStyle.Fill;
            Scroller.BackColor = Ui.CARD;
            Scroller.AutoLayout = false;      // 正文都是绝对定位，别去动它们
            Scroller.Horizontal = false;      // 弹窗不做横向滚动
            Scroller.BodyPadding = new Padding(0);
            Body = Scroller.ContentHost;
            Body.BackColor = Ui.CARD;
            Body.ControlAdded += delegate { RecalcBody(); };
            Body.ControlRemoved += delegate { RecalcBody(); };
            Scroller.Resize += delegate { RecalcBody(); };

            Controls.Add(Scroller);
            Controls.Add(Foot);
            Controls.Add(Head);
        }

        /// <summary>正文内容或窗口尺寸变了之后重算滚动范围。</summary>
        public void RecalcBody()
        {
            if (Scroller != null && !Scroller.IsDisposed) Scroller.Recalc();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalcBody();
        }

        /// <summary>加一个按钮。primary = 主色实心。</summary>
        public Pill Button(string text, bool primary, DialogResult result)
        {
            var b = new Pill { Text = text, Primary = primary, Font = Ui.F(9.5f, primary) };
            b.Size = new Size(Pill.Measure(text, Ui.F(9.5f, primary)) + Ui.Px(16), Ui.Px(34));
            b.Margin = new Padding(Ui.Px(8), 0, 0, 0);
            b.Click += delegate
            {
                DialogResult = result;
                Close();
            };
            Foot.Controls.Add(b);
            return b;
        }

        /// <summary>正文区宽度（已扣掉左右留白）。</summary>
        public int BodyWidth { get { return ClientSize.Width - Ui.Px(Pad * 2); } }

        /// <summary>按正文需要的高度收窗口，最高不超过屏幕的 70%。</summary>
        public void FitBody(int bodyHeightDp)
        {
            int max = (int)(Screen.FromControl(this).WorkingArea.Height * 0.70);
            int h = Head.Height + Ui.Px(bodyHeightDp) + Foot.Height;
            ClientSize = new Size(ClientSize.Width, Math.Min(h, max));
            RecalcBody();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Icon = AppIcon.Get(32); } catch { }
            RecalcBody();
        }

        void LayoutHead()
        {
            if (_close == null || Head == null) return;
            int bw = Ui.Px(42);
            _close.Bounds = new Rectangle(Head.Width - bw, Ui.Px(4), bw, Ui.Px(38));
        }

        /// <summary>弹窗没有虚线焦点框。</summary>
        protected override bool ShowFocusCues { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Ui.LINE, 1f))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>弹窗标题条：整条按住就能拖动窗口（走系统标题栏拖动循环）。弹窗不可缩放，双击不做任何事。</summary>
    public class DlgHead : Panel
    {
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Form f = FindForm();
            if (f != null && e.Button == MouseButtons.Left)
            {
                Ui.Native.BeginMove(f);
                return;
            }
            base.OnMouseDown(e);
        }
    }

    /// <summary>常用弹窗。</summary>
    public static class Dlg
    {
        // ===== 纯文字/说明 =====
        public static void Info(IWin32Window owner, string title, string sub, string markdown, int bodyDp = 240)
        {
            using (var d = new DlgForm(title, sub, 460))
            {
                var md = new MarkdownView
                {
                    Dock = DockStyle.None,
                    Location = new Point(Ui.Px(d.Pad), Ui.Px(10)),
                    Size = new Size(d.BodyWidth, Ui.Px(bodyDp)),
                    Markdown = markdown ?? ""
                };
                d.Body.Controls.Add(md);
                d.Button("知道了", true, DialogResult.OK);
                d.FitBody(bodyDp + 20);
                d.ShowDialog(owner);
            }
        }

        /// <summary>居中显示一段滚动的长文（更新日志之类）。</summary>
        public static void Markdown(IWin32Window owner, string title, string sub, string markdown)
        {
            int max = (int)(Screen.PrimaryScreen.WorkingArea.Height * 0.62);
            int lines = 0;
            if (!string.IsNullOrEmpty(markdown)) lines = markdown.Replace("\r\n", "\n").Split('\n').Length;
            int want = Math.Min(max, Math.Max(Ui.Px(200), lines * Ui.Px(22)));
            Info(owner, title, sub, markdown, want);
        }

        // ===== 确认 =====
        public static bool Confirm(IWin32Window owner, string title, string sub,
            string okText = "确定", string cancelText = "取消", bool danger = false)
        {
            using (var d = new DlgForm(title, sub, 420))
            {
                var ok = d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                if (danger) ok.ForeColor = Ui.RED;
                d.AcceptButton = null;
                d.FitBody(8);
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        // ===== 确认（带正文）=====
        /// <summary>带正文的确认框：sub 是标题下的说明，message 是正文段落。</summary>
        public static bool Confirm(IWin32Window owner, string title, string sub, string message,
            string okText, string cancelText, bool danger)
        {
            using (var d = new DlgForm(title, sub, 460))
            {
                int lines = 0;
                foreach (string part in (message ?? "").Split('\n'))
                    lines += Math.Max(1, (int)Math.Ceiling(part.Length / 34.0));
                if (lines < 1) lines = 1;

                var lbl = new Label();
                lbl.AutoSize = false;
                lbl.Dock = DockStyle.Top;
                lbl.Font = Ui.F(10f);
                lbl.ForeColor = Ui.TEXT_BODY;
                lbl.BackColor = Color.Transparent;
                lbl.Height = Ui.Px(lines * 23);
                lbl.Text = message;
                d.Body.Controls.Add(lbl);

                var ok = d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                if (danger) ok.ForeColor = Ui.RED;
                d.AcceptButton = null;
                d.FitBody(lines * 23 + 8);
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        // ===== 单选列表 =====
        /// <summary>返回选中下标，取消返回 -1。dots 给每项前面画一个色点（选皮肤用）。</summary>
        public static int Choose(IWin32Window owner, string title, string sub, string[] items,
            string[] notes = null, int selected = -1, Color[] dots = null)
        {
            // 注意：这里的单位是 dp，下面摆控件时才乘 Ui.Px
            int rowH = 46;
            int bodyH = Math.Min(320, items.Length * rowH + 8);
            int result = -1;

            using (var d = new DlgForm(title, sub, 420))
            {
                // 自绘列表，连滚动条一起画（ListBox 那条系统滚动条太扎眼）
                var list = new ChooseList
                {
                    RowH = 46,
                    Notes = notes,
                    Dots = dots,
                    Location = new Point(Ui.Px(d.Pad - 4), Ui.Px(6)),
                    Size = new Size(d.BodyWidth + Ui.Px(8), Ui.Px(bodyH))
                };
                list.Items.AddRange(items);
                if (selected >= 0 && selected < items.Length) list.SelectedIndex = selected;
                list.Pick += delegate
                {
                    if (list.SelectedIndex >= 0) { result = list.SelectedIndex; d.DialogResult = DialogResult.OK; d.Close(); }
                };
                d.Body.Controls.Add(list);

                d.Button("确定", true, DialogResult.OK).Click += delegate
                {
                    result = list.SelectedIndex;
                };
                d.Button("取消", false, DialogResult.Cancel);
                d.FitBody(bodyH + 16);
                d.Shown += delegate { list.Focus(); };
                if (d.ShowDialog(owner) == DialogResult.OK) return result;
                return -1;
            }
        }

        // ===== 表单（多字段输入）=====
        /// <summary>返回各字段的值，取消返回 null。secret[i] = true 的字段用密码框。</summary>
        public static string[] Form(IWin32Window owner, string title, string sub,
            string[] labels, string[] values, bool[] secret = null,
            string okText = "确定", string cancelText = "取消", int widthDp = 460, string hint = null)
        {
            int y = Ui.Px(8);
            var boxes = new List<Input>();
            using (var d = new DlgForm(title, sub, widthDp))
            {
                if (!string.IsNullOrEmpty(hint))
                {
                    var lb = new Label
                    {
                        Text = hint,
                        Font = Ui.F(8.5f),
                        ForeColor = Ui.SUB,
                        AutoSize = false,
                        BackColor = Color.Transparent,
                        Location = new Point(Ui.Px(d.Pad), y),
                        Size = new Size(d.BodyWidth, Ui.Px(34))
                    };
                    d.Body.Controls.Add(lb);
                    y += Ui.Px(38);
                }

                for (int i = 0; i < labels.Length; i++)
                {
                    var lab = new Label
                    {
                        Text = labels[i],
                        Font = Ui.F(9f),
                        ForeColor = Ui.SUB,
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        Location = new Point(Ui.Px(d.Pad), y + Ui.Px(2))
                    };
                    d.Body.Controls.Add(lab);
                    y += Ui.Px(24);

                    var tb = new Input();
                    tb.Text = (values != null && i < values.Length) ? (values[i] ?? "") : "";
                    tb.Font = Ui.F(10f);
                    tb.Location = new Point(Ui.Px(d.Pad), y);
                    tb.Size = new Size(d.BodyWidth, Ui.Px(36));
                    if (secret != null && i < secret.Length && secret[i]) tb.Secret = true;
                    d.Body.Controls.Add(tb);
                    boxes.Add(tb);
                    y += Ui.Px(48);
                }

                d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                // y 是像素，FitBody 收的是 dp。原来写的是 y / Ui.Px(1)，
                // 在 150% DPI 下 Ui.Px(1) 是 2，等于把弹窗高度砍掉一半，最后一个输入框永远被裁。
                d.FitBody((int)Math.Round(y / (double)(Ui.S <= 0f ? 1f : Ui.S)) + 8 + 8);
                if (boxes.Count > 0) d.AcceptButton = null;
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                var outp = new string[boxes.Count];
                for (int i = 0; i < boxes.Count; i++) outp[i] = boxes[i].Text;
                return outp;
            }
        }

        // ===== 等着（后台干活）=====
        /// <summary>把活放到后台线程跑，UI 上显示一句提示；干完自动关。</summary>
        public static void Wait(IWin32Window owner, string title, string sub, Action job)
        {
            using (var d = new DlgForm(title, sub, 380))
            {
                var spin = new Label
                {
                    Text = "请稍候…",
                    Font = Ui.F(9.5f),
                    ForeColor = Ui.SUB,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };
                d.Body.Controls.Add(spin);
                d.FitBody(70);
                d.Shown += delegate
                {
                    var t = new Thread(delegate()
                    {
                        try { job(); }
                        catch (Exception ex) { LastError = ex; }
                        finally
                        {
                            try { d.BeginInvoke((MethodInvoker)delegate { d.Close(); }); }
                            catch { }
                        }
                    });
                    t.IsBackground = true;
                    t.Start();
                };
                d.ShowDialog(owner);
            }
        }

        /// <summary>Wait 里后台任务抛出的异常（供调用方判断成败）。</summary>
        public static Exception LastError;
    }

    /// <summary>
    /// 自绘单选列表（给 Dlg.Choose 用）。
    /// 之所以不用 ListBox：它的竖向滚动条是系统的，带灰色上下箭头，
    /// 跟这套自绘界面放在一起特别扎眼。这里连滚动条一起画。
    /// </summary>
    public class ChooseList : Control
    {
        public readonly List<string> Items = new List<string>();
        public string[] Notes;
        public Color[] Dots;
        public int RowH = 46;                 // 行高（dp）

        int _index = -1;
        int _hot = -1;
        int _scroll;                          // 顶部第一行的下标
        int _thumbTop, _thumbH;
        bool _trackHot, _dragging;
        int _dragOffset;

        /// <summary>双击或回车时抛出（调用方据此收窗口）。</summary>
        public event EventHandler Pick;

        public ChooseList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            BackColor = Ui.CARD;
            Font = Ui.F(9.5f);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                if (v == _index) return;
                _index = v;
                EnsureVisible();
                Invalidate();
            }
        }

        public string SelectedItem
        {
            get { return (_index >= 0 && _index < Items.Count) ? Items[_index] : null; }
        }

        int RowPx { get { return Ui.Px(RowH); } }
        int VisibleRows { get { return Math.Max(1, Height / RowPx); } }
        int MaxScroll { get { return Math.Max(0, Items.Count - VisibleRows); } }

        void EnsureVisible()
        {
            if (_index < 0) return;
            if (_index < _scroll) _scroll = _index;
            else if (_index >= _scroll + VisibleRows) _scroll = _index - VisibleRows + 1;
            ClampScroll();
        }

        void ClampScroll()
        {
            if (_scroll < 0) _scroll = 0;
            int max = MaxScroll;
            if (_scroll > max) _scroll = max;
        }

        void ComputeThumb()
        {
            int trackH = Height - Ui.Px(12);
            if (MaxScroll <= 0 || Items.Count <= 0 || trackH < Ui.Px(40)) { _thumbH = 0; _thumbTop = 0; return; }
            _thumbH = Math.Max(Ui.Px(30), (int)((long)trackH * VisibleRows / Math.Max(1, Items.Count)));
            _thumbTop = Ui.Px(6) + (int)((long)(trackH - _thumbH) * _scroll / Math.Max(1, MaxScroll));
        }

        int TrackX() { return Width - Ui.Px(9); }
        bool InTrack(int x) { return _thumbH > 0 && x >= TrackX(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ComputeThumb();   // 画之前现算滑块，别指望 Resize/Invalidate 的时序
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            int rh = RowPx;
            int last = Math.Min(Items.Count, _scroll + VisibleRows + 1);
            for (int i = _scroll; i < last; i++)
            {
                int top = (i - _scroll) * rh;
                var row = new Rectangle(0, top, Width, rh - 1);
                bool sel = (i == _index);
                if (sel) Ui.FillRound(g, new Rectangle(row.X + 2, row.Y + 2, row.Width - 6, row.Height - 6), Ui.Px(8), Ui.ACCENT_SOFT);
                else if (i == _hot) Ui.FillRound(g, new Rectangle(row.X + 2, row.Y + 2, row.Width - 6, row.Height - 6), Ui.Px(8), Theme.Alpha(Ui.ACCENT, 20));

                int tx = Ui.Px(12);
                if (Dots != null && i < Dots.Length && Dots[i] != Color.Empty)
                {
                    using (var b = new SolidBrush(Dots[i]))
                        g.FillEllipse(b, tx, row.Y + rh / 2 - Ui.Px(6), Ui.Px(12), Ui.Px(12));
                    tx += Ui.Px(22);
                }
                string note = (Notes != null && i < Notes.Length) ? Notes[i] : "";
                if (string.IsNullOrEmpty(note))
                {
                    Ui.Text(g, Items[i], Ui.F(9.5f), sel ? Ui.ACCENT : Ui.INK, tx, row.Y + rh / 2 - Ui.Px(9));
                }
                else
                {
                    Ui.Text(g, Items[i], Ui.F(9.5f, true), sel ? Ui.ACCENT : Ui.INK, tx, row.Y + Ui.Px(7));
                    Ui.Text(g, note, Ui.F(8.5f), Ui.SUB, tx, row.Y + Ui.Px(25));
                }
            }

            if (_thumbH > 0)
            {
                Ui.FillRound(g, new Rectangle(TrackX() + Ui.Px(1), Ui.Px(6), Ui.Px(6), Height - Ui.Px(12)),
                    Ui.Px(3), _trackHot ? Ui.PANEL : BackColor);
                Ui.FillRound(g, new Rectangle(TrackX() + Ui.Px(_trackHot ? 1 : 2), _thumbTop, Ui.Px(_trackHot ? 6 : 4), _thumbH),
                    Ui.Px(_trackHot ? 3 : 2), _trackHot ? Ui.ACCENT : Ui.SUB);
            }
        }

        int IndexAt(int y)
        {
            int i = _scroll + (y + Ui.Px(4)) / RowPx;
            if (i < 0 || i >= Items.Count) return -1;
            return i;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (InTrack(e.X))
            {
                if (e.Y >= _thumbTop && e.Y <= _thumbTop + _thumbH)
                {
                    _dragging = true;
                    _dragOffset = e.Y - _thumbTop;
                }
                else ScrollByRows(e.Y < _thumbTop ? -VisibleRows : VisibleRows);
                return;
            }
            int i = IndexAt(e.Y);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && _thumbH > 0)
            {
                int room = Height - Ui.Px(12) - _thumbH;
                int max = MaxScroll;
                if (room > 0 && max > 0)
                {
                    int y = e.Y - _dragOffset - Ui.Px(6);
                    if (y < 0) y = 0;
                    if (y > room) y = room;
                    int v = (int)((long)y * max / room);
                    if (v != _scroll) { _scroll = v; Invalidate(); }
                }
                return;
            }
            int i = IndexAt(e.Y);
            if (i != _hot) { _hot = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _trackHot = false;
            _dragging = false;
            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _trackHot = true;
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            int i = IndexAt(PointToClient(Cursor.Position).Y);
            if (i >= 0) SelectedIndex = i;
            if (_index >= 0 && Pick != null) Pick(this, EventArgs.Empty);
        }

        void ScrollByRows(int rows)
        {
            int v = _scroll + rows;
            if (v < 0) v = 0;
            if (v > MaxScroll) v = MaxScroll;
            if (v != _scroll) { _scroll = v; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll <= 0) return;
            ScrollByRows(e.Delta > 0 ? -3 : 3);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int n = Items.Count;
            if (n == 0) return;
            if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(n - 1, (_index < 0 ? -1 : _index) + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, (_index < 0 ? 1 : _index) - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { SelectedIndex = 0; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { SelectedIndex = n - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_index >= 0 && Pick != null) Pick(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
            ComputeThumb();
            Invalidate();
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);
            ComputeThumb();
        }
    }
}
