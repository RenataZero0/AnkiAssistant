using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 「AnkiWeb 账号与同步」弹窗。
    ///
    /// 内置引擎（rslib_aa.dll）自己带一份收藏库，所以这个程序可以像手机版一样
    /// 直接拿邮箱 + 密码登录 AnkiWeb，再自己把收藏库同步上去 —— 不再需要桌面端在后台跑。
    ///
    /// 几条约定：
    ///   · 报告里的信息全部来自 AnkiSync（hkey / user / endpoint 都在 Store 里）；
    ///   · 登录、同步、退出登录一律在后台线程跑，结果用 BeginInvoke 回 UI，
    ///     主按钮在忙的时候禁用并改成「…中」，绝不卡界面；
    ///   · 全量同步是**破坏性**的（一边覆盖另一边），方向必须先问清楚、再确认一次；
    ///   · 界面上只谈 AnkiWeb 账号与同步，本机连接的问题不归这个弹窗管。
    /// </summary>
    public class WebDialog : DlgForm
    {
        // Store 里记上次同步时间（Unix 秒的字符串）。AnkiSync 不写这个键，由本弹窗维护。
        const string LastSyncKey = "anki.web.lastSync";

        /// <summary>正文高度（dp）。构造里 FitBody 与布局共用同一个值，别改一处忘一处。</summary>
        const int BodyDp = 574;

        /// <summary>状态卡片高度（dp）。大字 + 三行灰字 + 折行结果 + 右下两个按钮，装得下。</summary>
        const int StatusDp = 132;

        /// <summary>卡片内边距（dp）与两段之间的竖向间隙（dp）。</summary>
        const int InsetDp = 16;
        const int RowGapDp = 8;

        // ---- 状态区 ----
        Panel _status;                  // 状态卡片：大字状态 + 一行灰字 + 两个动作按钮
        Pill _bRecheck, _bLogout;

        // ---- 登录区 ----
        Card _login;
        Label _labMail, _labPass;
        Input _mail, _pass, _endpoint;
        Pill _bLogin;
        Disclosure _adv;                // 「服务器地址」折叠头
        FlowLayoutPanel _loginRow;

        // ---- 同步区 ----
        Card _sync;
        Label _syncHead;
        Check _withMedia;
        Pill _bSync;
        FlowLayoutPanel _syncRow;

        bool _busy;                     // 有后台活在跑
        float _lastSync;                // 上次同步时间（Unix 秒；0 = 没记录）
        string _result = "";            // 最近一次同步的结果 / 报错，显示在状态区
        bool _resultBad;                // _result 是不是坏消息（红字）

        public WebDialog() : base("AnkiWeb 账号与同步", null, 480)
        {
            // 基类在 sub = null 时标题区只有 56 高；这里撑到 74 并**追加**一行说明
            // （用 += 而不是 =，别把基类的标题盖掉）。
            Head.Height = Ui.Px(74);
            Head.Paint += delegate(object s, PaintEventArgs e)
            {
                Ui.Text(e.Graphics, "内置引擎自带收藏库，可以直接登录 AnkiWeb 同步",
                    Ui.F(9f), Ui.SUB, Ui.Px(Pad), Ui.Px(44));
            };

            Scroller.AutoLayout = true;
            Scroller.Gap = 14;
            Scroller.BodyPadding = new Padding(Ui.Px(Pad), Ui.Px(12), Ui.Px(Pad), Ui.Px(16));

            KeyPreview = true;          // Esc 关窗
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            BuildStatusCard();
            BuildLoginCard();
            BuildSyncCard();

            Button("关闭", false, DialogResult.Cancel);

            FitBody(BodyDp);
            Resize += delegate { FormLayout(); };
            FormLayout();

            RefreshStatus();

            Shown += delegate { RefreshStatus(); };
        }

        // ================================================================ 对外

        /// <summary>
        /// 造一个已经布局好的弹窗（**不 ShowDialog**）。截图探针靠它拿到实例自己 Show / DrawToBitmap。
        /// </summary>
        public static Form BuildForm()
        {
            return new WebDialog();
        }

        /// <summary>
        /// 打开「AnkiWeb 账号与同步」，关掉之后返回。
        /// 名字跟 Form.Show(IWin32Window) 撞了（那个是"非模态显示"），这里是有意的重载，用 new 挡掉警告。
        /// </summary>
        public new static DialogResult Show(IWin32Window owner)
        {
            using (Form d = BuildForm())
            {
                return d.ShowDialog(owner);
            }
        }

        // ================================================================ 状态卡片

        void BuildStatusCard()
        {
            _status = new Panel();
            _status.BackColor = Ui.CARD;
            _status.Paint += delegate(object s, PaintEventArgs e) { PaintStatus(e.Graphics, _status.Width); };

            _bRecheck = StatusAction("重新检查", false);
            _bRecheck.Click += delegate { RefreshStatus(); };

            _bLogout = StatusAction("退出登录", false);
            _bLogout.Click += delegate { Logout(); };

            Scroller.Add(_status);
        }

        /// <summary>状态卡片里的动作按钮（点了不关窗，所以不能走父类的 Button）。</summary>
        Pill StatusAction(string text, bool primary)
        {
            var b = new Pill { Text = text, Primary = primary, Font = Ui.F(9f) };
            b.Size = new Size(Pill.Measure(text, Ui.F(9f)) + Ui.Px(16), Ui.Px(30));
            _status.Controls.Add(b);
            return b;
        }

        /// <summary>
        /// 状态卡片重画：大字 + 圆点 + 一行灰字（刷新只重画这里，不重建弹窗）。
        /// 宽度必须由调用方把**状态卡片自己**的宽度传进来：这个是窗体的方法，
        /// 方法里的 this.Width 是窗体客户区宽度（720），当成卡片宽度用会把字画到卡片外面去。
        /// </summary>
        void PaintStatus(Graphics g, int panelW)
        {
            Color dot;
            if (!Engine.Available) dot = Ui.RED;
            else if (AnkiSync.LoggedIn()) dot = Ui.GREEN;
            else dot = Ui.AMBER;

            Font fBig = Ui.F(11f, true);        // Ui.F 返回全局缓存字体，**不要 Dispose**
            // 邮箱可能很长：交给 GDI 的 EndEllipsis 在宽度内截断成「…」，绝不顶出卡片右边
            Size sz = TextEllipsis(g, BigStatus(), fBig, Ui.INK, 0, Ui.Px(2), panelW - Ui.Px(22));

            // 圆点跟在字后面；sz 就是刚画出来那行字的实际宽度（含省略号）
            int d = Ui.Px(9);
            using (var b = new SolidBrush(dot))
                g.FillEllipse(b, sz.Width + Ui.Px(5),
                    Ui.Px(2) + (Ui.Px(17) - d) / 2, d, d);

            int y = Ui.Px(28);
            Font fSmall = Ui.F(8.5f);
            foreach (string line in SubLines())
            {
                TextEllipsis(g, line, fSmall, Ui.SUB, 0, y, panelW - Ui.Px(4));
                y += Ui.Px(15);
            }

            // 最近一次同步的结果 / 报错（长文案让它自己折行，不裁）
            if (_result != null && _result.Length > 0)
            {
                y += Ui.Px(3);
                int w = Width - Ui.Px(4);
                if (w < Ui.Px(80)) w = Ui.Px(80);
                Font fRes = Ui.F(8.5f, true);
                Color c = _resultBad ? Ui.RED : Ui.GREEN;
                foreach (string line in Ui.Wrap(g, _result, fRes, w))
                {
                    Ui.Text(g, line, fRes, c, 0, y);
                    y += Ui.Px(14);
                }
            }
        }

        /// <summary>
        /// 单行自绘文字 + 末尾省略号，返回实际画出来的那行字的尺寸。
        /// 状态区的文案可能很长（超长邮箱、超长引擎报错），必须先截断再画。
        /// 这里把截断交给 GDI 自己的 EndEllipsis，而不是自己量宽度再切字符串：
        /// Graphics.MeasureString 走 GDI+，和 Ui.Text 真正用的 GDI 度量对不上，
        /// 按 GDI+ 量出来的宽度切完照样会被画到框外。
        /// </summary>
        static Size TextEllipsis(Graphics g, string s, Font f, Color c, int x, int y, int maxW)
        {
            if (s == null) s = "";
            if (maxW < Ui.Px(40)) maxW = Ui.Px(40);
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            Size box = new Size(maxW, Math.Max(Ui.Px(18), f.Height));
            TextRenderer.DrawText(g, s, f, new Rectangle(x, y, box.Width, box.Height), c, fl);
            return TextRenderer.MeasureText(g, s, f, box, fl);
        }

        /// <summary>状态区那行大字。</summary>
        string BigStatus()
        {
            if (!Engine.Available) return "内置引擎不可用";
            if (AnkiSync.LoggedIn())
            {
                string u = AnkiSync.User();
                return u.Length > 0 ? ("已登录：" + u) : "已登录 AnkiWeb";
            }
            return "未登录 AnkiWeb";
        }

        /// <summary>状态区下面那几行灰字（最多三行）。</summary>
        string[] SubLines()
        {
            string engine = EngineLabel();
            if (!Engine.Available) return new string[] { "内置引擎不可用：" + Engine.UnavailableReason };
            if (AnkiSync.LoggedIn())
            {
                string time = LastSyncText();
                if (time.Length == 0) return new string[] { engine };
                return new string[] { engine, "上次同步：" + time };
            }
            return new string[] { engine };
        }

        /// <summary>
        /// 卡片宽度跟着弹窗走，卡片里面的东西（输入框、按钮行）宽度也要跟着改 ——
        /// 写死 Ui.Px(480-32) 在 150% DPI 的机器上会算出比卡片还宽的行，按钮会被卡片裁掉半边。
        /// </summary>
        void FormLayout()
        {
            int w = ClientSize.Width - Ui.Px(Pad * 2);
            if (w < Ui.Px(200)) w = Ui.Px(200);

            // 卡片里面的可用宽度（左右各留 InsetDp；右侧再多留 RowGapDp，
            // 因为 FlowLayoutPanel 会把自己第一个子控件的 Margin.Right 也算进去）
            int inner = w - Ui.Px(InsetDp * 2) - Ui.Px(RowGapDp);
            if (inner < Ui.Px(80)) inner = Ui.Px(80);

            _status.SetBounds(0, 0, w, Ui.Px(StatusDp));
            int by = _status.Height - Ui.Px(34) - Ui.Px(RowGapDp);
            _bLogout.Location = new Point(w - Ui.Px(InsetDp) - _bLogout.Width, by);
            _bRecheck.Location = new Point(_bLogout.Left - Ui.Px(RowGapDp) - _bRecheck.Width, by);

            // ---- 登录卡片 ----
            _mail.Location = new Point(Ui.Px(InsetDp), Ui.Px(34));
            _mail.Width = inner;
            _pass.Location = new Point(Ui.Px(InsetDp), Ui.Px(104));
            _pass.Width = inner;
            _adv.Width = inner;
            _adv.Location = new Point(Ui.Px(InsetDp), Ui.Px(154));
            _loginRow.Location = new Point(Ui.Px(InsetDp), Ui.Px(224));
            _loginRow.Width = inner;

            // ---- 同步卡片 ----
            _withMedia.Location = new Point(Ui.Px(InsetDp), Ui.Px(46));
            _withMedia.Width = inner;
            _syncRow.Location = new Point(Ui.Px(InsetDp), Ui.Px(82));
            _syncRow.Width = inner;
            _syncHead.Width = inner;

            Scroller.Recalc();
        }

        // ================================================================ 登录卡片

        void BuildLoginCard()
        {
            _login = new Card();

            _labMail = MkLabel("AnkiWeb 邮箱", 9f, Ui.SUB);
            _labMail.Location = new Point(Ui.Px(InsetDp), Ui.Px(14));

            _mail = MkInput(false);
            _mail.Location = new Point(Ui.Px(InsetDp), Ui.Px(34));
            _mail.Placeholder = "你在 ankiweb.net 注册的邮箱";
            _mail.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); }
            };

            _labPass = MkLabel("密码", 9f, Ui.SUB);
            _labPass.Location = new Point(Ui.Px(InsetDp), Ui.Px(84));

            _pass = MkInput(true);
            _pass.Location = new Point(Ui.Px(InsetDp), Ui.Px(104));
            _pass.Placeholder = "不会存进设置，只用来换一张登录凭证";
            _pass.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); }
            };

            // ---- 「服务器地址」折叠区（默认收起，一般不用改）----
            _endpoint = MkInput(false);
            _endpoint.Placeholder = AnkiSync.DefEndpoint;
            _endpoint.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); }
            };
            _endpoint.TextChanged += delegate
            {
                // 边打边存。空值不写：手一抖清空了，别把还能用的地址冲掉。
                string ep = _endpoint.Text.Trim();
                if (ep.Length > 0) Store.Set("anki.web.endpoint", ep);
            };

            _adv = new Disclosure("服务器地址（一般不用改）", _endpoint);
            _adv.Location = new Point(Ui.Px(InsetDp), Ui.Px(154));

            // 底部动作按钮靠右下（FlowLayoutPanel 从右往左排，右边缘跟卡片内边距对齐）
            _loginRow = new FlowLayoutPanel();
            _loginRow.FlowDirection = FlowDirection.RightToLeft;
            _loginRow.WrapContents = false;
            _loginRow.BackColor = Color.Transparent;
            _loginRow.Location = new Point(Ui.Px(InsetDp), Ui.Px(224));
            _loginRow.Size = new Size(Ui.Px(480 - InsetDp * 2), Ui.Px(34));

            _bLogin = new Pill { Text = "登录并同步", Primary = true, Font = Ui.F(9.5f, true) };
            _bLogin.Size = new Size(Pill.Measure(_bLogin.Text, _bLogin.Font) + Ui.Px(16), Ui.Px(34));
            _bLogin.Margin = new Padding(Ui.Px(RowGapDp), 0, 0, 0);
            _bLogin.Click += delegate { DoLogin(); };
            _loginRow.Controls.Add(_bLogin);

            _login.Controls.Add(_labMail);
            _login.Controls.Add(_mail);
            _login.Controls.Add(_labPass);
            _login.Controls.Add(_pass);
            _login.Controls.Add(_adv);
            _login.Controls.Add(_loginRow);
            _login.Height = Ui.Px(258);   // 固定高度：折叠区收起时下面留白，展开时地址输入框正好落在里面

            _mail.Text = AnkiSync.User();
            _endpoint.Text = AnkiSync.Endpoint();

            Scroller.Add(_login);
        }

        // ================================================================ 同步卡片

        void BuildSyncCard()
        {
            _sync = new Card();

            _syncHead = MkLabel("与 AnkiWeb 同步", 10f, Ui.INK);
            _syncHead.Font = Ui.F(10f, true);
            _syncHead.Location = new Point(Ui.Px(InsetDp), Ui.Px(14));

            _withMedia = new Check { Text = "连媒体一起同步（声音、图片；第一次会比较慢）" };
            _withMedia.Font = Ui.F(9f);
            // 默认勾上：没存过这个偏好时当它是「勾着的」，用户明确关过就还是关着
            _withMedia.Checked = Store.GetBool("sync.media", true);
            _withMedia.CheckedChanged += delegate { Store.SyncMedia = _withMedia.Checked; };
            _withMedia.Location = new Point(Ui.Px(InsetDp), Ui.Px(46));
            _withMedia.Size = new Size(Ui.Px(440), Ui.Px(26));

            _syncRow = new FlowLayoutPanel();
            _syncRow.FlowDirection = FlowDirection.RightToLeft;
            _syncRow.WrapContents = false;
            _syncRow.BackColor = Color.Transparent;
            _syncRow.Location = new Point(Ui.Px(InsetDp), Ui.Px(82));
            _syncRow.Size = new Size(Ui.Px(480 - InsetDp * 2), Ui.Px(34));

            _bSync = new Pill { Text = "立即同步", Primary = true, Font = Ui.F(9.5f, true) };
            _bSync.Size = new Size(Pill.Measure(_bSync.Text, _bSync.Font) + Ui.Px(16), Ui.Px(34));
            _bSync.Margin = new Padding(Ui.Px(RowGapDp), 0, 0, 0);
            _bSync.Click += delegate { SyncStart(AnkiSync.FullMode.Ask); };
            _syncRow.Controls.Add(_bSync);

            _sync.Controls.Add(_syncHead);
            _sync.Controls.Add(_withMedia);
            _sync.Controls.Add(_syncRow);
            _sync.Height = Ui.Px(130);

            Scroller.Add(_sync);
        }

        // ================================================================ 动作

        /// <summary>按当前的引擎 / 登录状态刷新状态卡片和按钮可用性。</summary>
        void RefreshStatus()
        {
            string v = Store.Get("anki.web.lastSync", "");
            _lastSync = 0f;
            if (v.Length > 0)
            {
                // C# 5 没有 TryParse(string, out float) 单参重载，得带着 NumberStyles / IFormatProvider 走。
                // 存进去的是 InvariantCulture 的小数，读也要用同一个 culture，不然法语/德语系统上小数点会认错。
                float parsed;
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
                    _lastSync = parsed;
            }

            bool engine = Engine.Available;
            bool logged = AnkiSync.LoggedIn();

            _bRecheck.Enabled = !_busy;
            _bLogout.Enabled = !_busy && logged;
            _bLogin.Enabled = !_busy && engine;
            SetPillText(_bLogin, _busy ? "登录中…" : "登录并同步", Ui.F(9.5f, true));
            _bSync.Enabled = !_busy && engine && logged;
            SetPillText(_bSync, _busy ? "同步中…" : (logged ? "立即同步" : "先登录 AnkiWeb"),
                Ui.F(9.5f, true));

            _bLogin.Invalidate();
            _bSync.Invalidate();
            _bRecheck.Invalidate();
            _bLogout.Invalidate();
            _status.Invalidate();
            if (_syncRow != null) _syncRow.PerformLayout();     // 按钮换文字后重新靠右排
            if (_loginRow != null) _loginRow.PerformLayout();
            FormLayout();       // 状态文字行数会变，卡片高矮跟着调
        }

        /// <summary>
        /// 换按钮文字时把宽度一起量准 —— 按钮宽度只按初始文字算过一次，
        /// 之后换成更长的文字（"立即同步" → "先登录 AnkiWeb"）就会被裁掉尾巴。
        /// </summary>
        static void SetPillText(Pill b, string text, Font f)
        {
            int need = Pill.Measure(text, f) + Ui.Px(16);
            if (b.Width < need) b.Width = need;
            b.Text = text;
            b.Invalidate();
        }

        /// <summary>后台活跑着的时候禁掉按钮，别让人连点。</summary>
        void SetBusy(bool busy)
        {
            _busy = busy;
            _mail.Enabled = !busy;
            _pass.Enabled = !busy;
            _endpoint.Enabled = !busy;
            _withMedia.Enabled = !busy;
            RefreshStatus();
        }

        // -------- 登录 --------

        void DoLogin()
        {
            if (_busy) return;
            if (!Engine.Available)
            {
                _result = "内置引擎不可用，没法登录 AnkiWeb。";
                _resultBad = true;
                RefreshStatus();
                Dlg.Info(this, "登录不了 AnkiWeb", null,
                    "内置引擎不可用：" + Engine.UnavailableReason, 90);
                return;
            }

            string user = _mail.Text.Trim();
            string pass = _pass.Text;
            string ep = _endpoint.Text.Trim();
            if (ep.Length == 0) ep = AnkiSync.DefEndpoint;
            Store.Set("anki.web.endpoint", ep);

            if (user.Length == 0)
            {
                Dlg.Info(this, "还差一样", null, "请先填 AnkiWeb 邮箱。", 60);
                _mail.FocusInner();
                return;
            }
            if (pass.Length == 0)
            {
                Dlg.Info(this, "还差一样", null, "请先填 AnkiWeb 密码。", 60);
                _pass.FocusInner();
                return;
            }

            SetBusy(true);
            _result = "正在登录 AnkiWeb…";
            _resultBad = false;
            _status.Invalidate();

            var t = new Thread(delegate()
            {
                string err = null;
                try
                {
                    // rslib 必须先打开收藏库，登录/同步这类 RPC 才走得通；
                    // 启动流程里通常已经打开了，这里兜一道底（打开失败会抛中文说明）。
                    if (!AnkiConn.Opened) AnkiConn.EnsureOpen();
                    AnkiSync.Login(user, pass);
                }
                catch (Exception ex) { err = ex.Message; }
                Post(delegate()
                {
                    _pass.Text = "";
                    SetBusy(false);
                    if (err != null)
                    {
                        _result = err;
                        _resultBad = true;
                        _status.Invalidate();
                        Dlg.Info(this, "登录 AnkiWeb 没成功", null, err, 90);
                        return;
                    }
                    _result = "已登录 " + AnkiSync.User() + "，接着同步一次…";
                    _resultBad = false;
                    _status.Invalidate();
                    SyncStart(AnkiSync.FullMode.Ask);      // 登录成功顺手同步一次
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void Logout()
        {
            if (_busy) return;
            bool ok = Dlg.Confirm(this, "退出 AnkiWeb", "退出之后要再同步就得重新填一次密码。",
                "只会删掉存在本机的那张登录凭证（hkey），不动本机的收藏库 —— " +
                "卡片、牌组、复习记录都还在，也不会去改 AnkiWeb 上的东西。", "退出登录", "取消", false);
            if (!ok) return;

            AnkiSync.Logout();
            _result = "已退出登录。";
            _resultBad = false;
            RefreshStatus();
        }

        // -------- 同步 --------

        /// <summary>
        /// 跑一次同步（后台线程）。
        /// fullMode = Ask 时，AnkiSync 需要全量同步会抛 FullSyncRequired，这里转成「问用户方向」。
        /// </summary>
        void SyncStart(AnkiSync.FullMode fullMode)
        {
            if (_busy) return;
            if (!AnkiSync.LoggedIn())
            {
                _result = "还没登录 AnkiWeb —— 先在上面填邮箱和密码点「登录并同步」。";
                _resultBad = true;
                RefreshStatus();
                Dlg.Info(this, "还没登录 AnkiWeb", null,
                    "先在邮箱和密码里填上 AnkiWeb 账号，点「登录并同步」，然后才能同步收藏库。", 70);
                return;
            }
            if (!Engine.Available)
            {
                _result = "内置引擎不可用，同步不了。";
                _resultBad = true;
                RefreshStatus();
                Dlg.Info(this, "同步不了", null, "内置引擎不可用：" + Engine.UnavailableReason, 90);
                return;
            }

            bool media = _withMedia.Checked;

            if (fullMode != AnkiSync.FullMode.Ask)
            {
                // 全量同步是破坏性动作，动手前再确认一次（方向越具体越好）
                string side = fullMode == AnkiSync.FullMode.Upload
                    ? "用本机收藏库覆盖 AnkiWeb 上的那一份"
                    : "用 AnkiWeb 上的收藏库覆盖本机这一份";
                bool ok = Dlg.Confirm(this, "确认全量同步", "这一步是破坏性的，做完就回不去了。",
                    side + "。\n" +
                    "被覆盖那一边多出来的卡片、牌组和复习记录会全部消失，不能撤销。\n" +
                    "同步过程中不要关掉这个窗口。", "开始全量同步", "再想想", true);
                if (!ok) { SyncDoneNote("已取消全量同步。"); return; }
            }

            SetBusy(true);
            _result = fullMode == AnkiSync.FullMode.Ask ? "正在同步…" : "正在做全量同步…";
            _resultBad = false;
            _status.Invalidate();

            var t = new Thread(delegate()
            {
                AnkiSync.Outcome oc = null;
                AnkiSync.FullSyncRequired need = null;
                string err = null;
                try
                {
                    if (!AnkiConn.Opened) AnkiConn.EnsureOpen();   // 同登录：没有打开的收藏库，同步 RPC 走不通
                    oc = AnkiSync.Sync(fullMode, media);
                }
                catch (AnkiSync.FullSyncRequired ex) { need = ex; }
                catch (Exception ex) { err = ex.Message; }

                bool needFull = need != null;
                bool onlyDown = need != null && need.DownloadOnly;
                string needWhy = need != null ? need.Message : null;
                AnkiSync.Outcome got = oc;
                string fail = err;

                Post(delegate()
                {
                    if (needFull)
                    {
                        SetBusy(false);
                        FullPick(needWhy, onlyDown);
                        return;
                    }
                    Finish(got, fail);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>同步收尾：把结果写进状态区，刷新时间戳。</summary>
        void Finish(AnkiSync.Outcome oc, string err)
        {
            SetBusy(false);

            string msg;
            if (err != null) { msg = err; _resultBad = true; }
            else if (oc != null && oc.DidFullSync)
            {
                if (oc.Message != null && oc.Message.Length > 0) msg = oc.Message;
                else msg = oc.Uploaded ? "全量同步完成：本机收藏库已覆盖 AnkiWeb。" : "全量同步完成：AnkiWeb 的收藏库已下载到本机。";
                _resultBad = false;
            }
            else
            {
                if (oc != null && oc.Message != null && oc.Message.Length > 0) msg = oc.Message;
                else msg = "同步完成。" + AnkiSync.Describe();
                _resultBad = false;
            }

            // 只有真的同步成功才记时间戳 —— 失败也写的话，「上次同步：刚刚」就是骗人的。
            if (err == null)
                Store.Set(LastSyncKey, NowSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            _result = msg;
            RefreshStatus();
        }

        /// <summary>用户自己取消全量同步时，只改状态区文字。</summary>
        void SyncDoneNote(string text)
        {
            _result = text;
            _resultBad = false;
            RefreshStatus();
        }

        /// <summary>问用户全量同步往哪个方向走。</summary>
        void FullPick(string why, bool downloadOnly)
        {
            if (IsDisposed) return;

            string[] items = new string[]
            {
                "从 AnkiWeb 下载（覆盖本机）",
                "上传本机（覆盖 AnkiWeb）"
            };
            string[] notes = new string[]
            {
                "用 AnkiWeb 上的收藏库替换这台电脑上的，先确认在别处改过的东西都同步上去了。",
                downloadOnly
                    ? "AnkiWeb 上的收藏库是空的，这次没法往上覆盖。"
                    : "用这台电脑上的收藏库替换 AnkiWeb 上的，别处还没同步过来的改动会丢。"
            };

            string sub = "这台电脑的收藏库和 AnkiWeb 上的差别太大，需要做一次全量同步。" +
                         (downloadOnly ? "这次只能从 AnkiWeb 往下载。" : "");
            if (why != null && why.Length > 0) sub = why;

            int pick = Dlg.Choose(this, "需要一次全量同步",
                sub + "　选一个方向，下一步还会再确认一次。", items, notes, 0);

            if (pick < 0) { SyncDoneNote("已取消全量同步。"); return; }
            if (pick == 1 && downloadOnly)
            {
                Dlg.Info(this, "这次只能下载", null,
                    "AnkiWeb 上的收藏库是空的，只能用「从 AnkiWeb 下载（覆盖本机）」。", 70);
                SyncDoneNote("这次只能从 AnkiWeb 下载 —— 重新点「立即同步」再选一次方向。");
                return;
            }

            SyncStart(pick == 1 ? AnkiSync.FullMode.Upload : AnkiSync.FullMode.Download);
        }

        // ================================================================ 小工具

        /// <summary>跨线程把动作丢回 UI 线程（弹窗已关掉就丢掉）。</summary>
        void Post(MethodInvoker fn)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(fn);
            }
            catch (Exception) { }   // 弹窗已经关了
        }

        static float NowSeconds()
        {
            return (float)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        /// <summary>上次同步时间的可读文本；没记录就返回空串。</summary>
        string LastSyncText()
        {
            if (_lastSync <= 0f) return "";
            try
            {
                DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(_lastSync).ToLocalTime();
                TimeSpan ago = DateTime.Now - t;
                if (ago.TotalSeconds < 60) return "刚刚";
                if (ago.TotalMinutes < 60) return (int)ago.TotalMinutes + " 分钟前";
                if (ago.TotalHours < 24) return (int)ago.TotalHours + " 小时前";
                return t.ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception) { return ""; }
        }

        /// <summary>把引擎自报的长版本串收成一行「rslib anki 26.09.3」。</summary>
        static string EngineLabel()
        {
            string raw = "";
            try { raw = Engine.Version(); }
            catch (Exception) { raw = ""; }
            string core = AnkiCore(raw);
            return core.Length > 0 ? ("内置引擎可用（" + core + "）") : "内置引擎可用";
        }

        /// <summary>从 "aa-ffi 0.1.0 / rslib anki 26.09.3 (build ) / windows-x86_64" 里取 "rslib anki 26.09.3"。</summary>
        static string AnkiCore(string raw)
        {
            if (raw == null) return "";
            string[] parts = raw.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.IndexOf("anki", StringComparison.OrdinalIgnoreCase) >= 0) return p;
            }
            if (raw.Length > 48) return raw.Substring(0, 48);
            return raw;
        }

        /// <summary>造一个自绘标签（Label 在自绘卡片上要把背景设成透明）。</summary>
        static Label MkLabel(string text, float pt, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(pt);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.UseMnemonic = false;
            return l;
        }

        /// <summary>造一个圆角输入框（宽度在建卡片时按弹窗宽度定）。</summary>
        Input MkInput(bool secret)
        {
            var box = new Input();
            box.Font = Ui.F(10f);
            box.Secret = secret;
            if (secret)
            {
                // Input 显示灰色占位文字时会临时把密码掩码关掉（好让占位文字读得出来），
                // 但恢复的那句判断读的又是掩码自己，关掉以后就恢复不回来了 —— 实测
                // 占位文字一显示过，之后敲进去的密码就是明文。这里在获得焦点 / 按键 /
                // 文本变化的时刻把掩码重新按上，保证密码永远是圆点。
                // （Ui.cs 不在本文件的改动范围内，所以在这里补。）
                box.Enter += delegate { box.Secret = true; };
                box.KeyPress += delegate { box.Secret = true; };
                box.TextChanged += delegate
                {
                    if (box.Focused || box.ContainsFocus) box.Secret = true;
                };
            }
            box.Size = new Size(Ui.Px(200), Ui.Px(34));
            return box;
        }

        // ================================================================ 折叠区

        /// <summary>
        /// 可展开 / 收起的区块：一行标题（左边小三角）+ 收纳在下面的内容控件。
        /// 收起时把内容控件藏起来，卡片是固定高度的，展开时正好填满，不用回调去改宿主高度。
        /// </summary>
        class Disclosure : Control
        {
            readonly Control _body;
            readonly string _label = "";
            int _bodyH = 34;
            bool _open, _hover;

            public Disclosure(string title, Control body)
            {
                _label = title == null ? "" : title;
                _body = body;
                Height = Ui.Px(28);
                // SupportsTransparentBackColor 必须在设 BackColor 之前打开，
                // 否则 set_BackColor 直接抛 ArgumentException（控件不支持透明的背景色）。
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                if (_body != null)
                {
                    _bodyH = _body.Height;
                    _body.Visible = false;
                    Controls.Add(_body);
                }
            }

            public bool Expanded { get { return _open; } }

            public void SetExpanded(bool open)
            {
                _open = open;
                // 展开高度 = 标题行 28dp + 6dp 间距 + 内容控件自己的高度
                int want = Ui.Px(28) + Ui.Px(6) + _bodyH;
                Height = open ? want : Ui.Px(28);
                if (_body != null)
                {
                    _body.Visible = open;
                    _body.Location = new Point(0, Ui.Px(28 + 6));
                    _body.Width = Width;
                }
                Invalidate();
                PerformLayout();
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) SetExpanded(!_open);
                base.OnMouseDown(e);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if (keyData == Keys.Space || keyData == Keys.Enter) return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    SetExpanded(!_open);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var b = new SolidBrush(Ui.ParentFill(this))) g.FillRectangle(b, ClientRectangle);

                // 左边一个小三角（收起朝右、展开朝下），跟自绘下拉的箭头一个风格
                int s = Ui.Px(8);
                int ax = 0, ay = Ui.Px(28) / 2 - s / 2;
                var pts = _open
                    ? new Point[] { new Point(ax, ay), new Point(ax + s, ay), new Point(ax + s / 2, ay + s / 2) }
                    : new Point[] { new Point(ax, ay), new Point(ax + s / 2, ay + s / 2), new Point(ax, ay + s) };
                using (var b = new SolidBrush(_hover ? Ui.ACCENT : Ui.SUB)) g.FillPolygon(b, pts);

                Ui.Text(g, _label, Ui.F(9f), _hover ? Ui.ACCENT : Ui.SUB, Ui.Px(14), Ui.Px(6));

                // 收起时补一句说明，免得看起来像坏掉的空行
                if (!_open)
                    Ui.Text(g, "默认 " + AnkiSync.DefEndpoint, Ui.F(8f), Ui.TEXT_DIM,
                        Ui.Px(14) + ((int)Math.Ceiling(g.MeasureString(_label, Ui.F(9f)).Width)) + Ui.Px(8),
                        Ui.Px(9));
            }
        }
    }
}
