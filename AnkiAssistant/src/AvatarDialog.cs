using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 「账号与头像」弹窗（点顶栏右上角的头像角标打开，见 MainForm 里 _avatar.Click）。
    ///
    /// 头像那套逻辑本来散在设置页里；这个版本把它搬到角标下面，跟 Android 版一致：
    /// 角标就是入口，点开就能换。弹窗管四件事 ——
    ///   · 预览现在是哪张图（自定义 / Gravatar / 字母）；
    ///   · 填 Gravatar 用的邮箱（只用来算 hash，本程序不拿它登录）；
    ///   · 选一张本机图片（写进 Anki 媒体库，跟着 AnkiWeb 同步）；
    ///   · 换回 Gravatar、让 Anki 自己同步一次。
    ///
    /// 这里**没有密码框**：AnkiWeb 的登录与同步在「设置 → 同步 → 登录 / 同步…」里，
    /// 这个弹窗只负责头像本身（邮箱只用来算 Gravatar hash）。
    ///
    /// 几条约定：
    ///   · 联网 / 上传一律不占 UI 线程（用 Dlg.Wait 或 AvatarStore.ResolveAsync）；
    ///   · Ui.F 返回全局缓存字体，绝对不能 Dispose；
    ///   · 自绘文字只走 Ui.Text / Ui.TextC，不用 g.DrawString。
    /// </summary>
    public class AvatarDialog : DlgForm
    {
        /// <summary>正文高度（dp）—— 构造里 FitBody 与布局共用同一个值。</summary>
        const int BodyDp = 410;

        /// <summary>预览方块的边长（dp），圆角半径按它的 30% 取（跟角标一样）。</summary>
        const int PreviewDp = 72;

        readonly int _previewPx = Ui.Px(PreviewDp);

        Input _mail;                // 邮箱输入框
        Label _source;              // 「当前：…」那一行
        Label _status;              // 动作结果那一行（成功绿 / 只在本机琥珀 / 失败红）
        AvatarPreview _preview;     // 自绘预览
        Timer _debounce;            // 输入邮箱时的 800ms 防抖

        bool _busy;                 // 有后台活在跑（跟 Anki 说话的时候）
        bool _loading;              // 灌初始值期间挡住 TextChanged

        public AvatarDialog() : base("账号与头像",
            "头像跟着 Anki 媒体库走，AnkiWeb 同步后手机和别的电脑上也会变", 460)
        {
            // 正文改由 DlgForm 的自绘滚动容器接管，不要再开 AutoScroll
            // （那会露出系统那条带灰色箭头的经典滚动条）。

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            BuildBody();
            BuildButtons();

            FitBody(BodyDp);
            LoadValues();
        }

        // ================================================================ 正文

        void BuildBody()
        {
            int bx = Ui.Px(Pad);
            int w = BodyWidth;
            int right = bx + w;

            // ---- 头像预览（72dp 圆角方形）----
            _preview = new AvatarPreview();
            _preview.Size = new Size(_previewPx, _previewPx);
            _preview.Location = new Point(bx, Ui.Px(8));
            Body.Controls.Add(_preview);

            // ---- 「当前：…」跟在预览右边，垂直居中 ----
            _source = new Label();
            _source.Font = Ui.F(10f);
            _source.ForeColor = Ui.SUB;
            _source.AutoSize = false;
            _source.BackColor = Color.Transparent;
            _source.TextAlign = ContentAlignment.MiddleLeft;
            _source.Location = new Point(bx + _previewPx + Ui.Px(14),
                Ui.Px(8) + (_previewPx - Ui.Px(24)) / 2);
            _source.Size = new Size(right - (bx + _previewPx + Ui.Px(14)), Ui.Px(24));
            Body.Controls.Add(_source);

            // ---- 邮箱 ----
            int yLab = Ui.Px(96);
            var lab = new Label();
            lab.Text = "Gravatar / AnkiWeb 邮箱";
            lab.Font = Ui.F(9f);
            lab.ForeColor = Ui.SUB;
            lab.AutoSize = true;
            lab.BackColor = Color.Transparent;
            lab.Location = new Point(bx, yLab);
            Body.Controls.Add(lab);

            int yBox = yLab + Ui.Px(24);
            _mail = new Input();
            _mail.Font = Ui.F(10f);
            _mail.Location = new Point(bx, yBox);
            _mail.Size = new Size(w, Ui.Px(34));
            _mail.Placeholder = "用来算 Gravatar 头像的邮箱";
            _mail.TextChanged += delegate { OnMailTyped(); };
            _mail.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                _debounce.Stop();       // 回车 = 立刻算一次，不等防抖
                OnMailSettled();
            };
            // 失焦也算「改完了」，走同一条路（防抖收口）
            _mail.Inner.LostFocus += delegate
            {
                if (_loading) return;
                Store.Set("anki.profile", _mail.Text.Trim());
                _debounce.Stop();
                OnMailSettled();
            };
            Body.Controls.Add(_mail);

            var hint = new Label();
            hint.Text = "只用来算头像（Gravatar 的公开算法），本程序不会把邮箱发到别处，也不需要 AnkiWeb 密码。";
            hint.Font = Ui.F(8.5f);
            hint.ForeColor = Ui.TEXT_DIM;
            hint.AutoSize = false;
            hint.BackColor = Color.Transparent;
            hint.TextAlign = ContentAlignment.TopLeft;
            hint.Size = new Size(w, Ui.Px(34));
            hint.Location = new Point(bx, yBox + Ui.Px(38));
            Body.Controls.Add(hint);

            // ---- 动作按钮：两个并排在左，同步单独一行 ----
            int yRow1 = Ui.Px(192);
            int half = (w - Ui.Px(8)) / 2;
            var bPick = MakeActionButton("选择图片…", true);
            bPick.Location = new Point(bx, yRow1);
            bPick.Size = new Size(half, Ui.Px(32));
            bPick.Click += delegate { PickImage(); };
            Body.Controls.Add(bPick);

            var bGrav = MakeActionButton("换回 Gravatar", false);
            bGrav.Location = new Point(bx + half + Ui.Px(8), yRow1);
            bGrav.Size = new Size(w - half - Ui.Px(8), Ui.Px(32));
            bGrav.Click += delegate { UseGravatar(); };
            Body.Controls.Add(bGrav);

            var bSync = MakeActionButton("同步到 AnkiWeb", false);
            bSync.Location = new Point(bx, yRow1 + Ui.Px(40));
            bSync.Size = new Size(w, Ui.Px(32));
            bSync.Click += delegate { SyncNow(); };
            Body.Controls.Add(bSync);

            // ---- 结果 / 状态 ----
            _status = new Label();
            _status.Font = Ui.F(9f);
            _status.ForeColor = Ui.SUB;
            _status.AutoSize = false;
            _status.BackColor = Color.Transparent;
            _status.TextAlign = ContentAlignment.TopLeft;
            _status.Size = new Size(w, Ui.Px(36));
            _status.Location = new Point(bx, yRow1 + Ui.Px(84));
            Body.Controls.Add(_status);

            // ---- 说明：为什么没有密码框 ----
            var note = new Label();
            note.Text = "这里没有密码框。头像存进本程序的收藏库媒体里，跟着 AnkiWeb 同步到手机和别的电脑；" +
                        "AnkiWeb 的登录在「设置 → 同步 → 登录 / 同步…」。点上面的同步按钮，" +
                        "会用已经登录的账号把头像一起传上去。";
            note.Font = Ui.F(8.5f);
            note.ForeColor = Ui.TEXT_DIM;
            note.AutoSize = false;
            note.BackColor = Color.Transparent;
            note.TextAlign = ContentAlignment.TopLeft;
            note.Location = new Point(bx, Ui.Px(322));
            note.Size = new Size(w, Ui.Px(BodyDp - 328));
            Body.Controls.Add(note);
        }

        void BuildButtons()
        {
            // Foot 从右到左排，先加的靠右；「完成」放最右边
            Button("完成", true, DialogResult.OK);
        }

        /// <summary>造一个只干活、不关窗的胶囊按钮（尺寸算法照抄 DlgForm.Button）。</summary>
        Pill MakeActionButton(string text, bool primary)
        {
            var b = new Pill();
            b.Text = text;
            b.Primary = primary;
            b.Font = Ui.F(9.5f, primary);
            b.Size = new Size(Pill.Measure(text, Ui.F(9.5f, primary)) + Ui.Px(16), Ui.Px(32));
            b.Cursor = Cursors.Hand;
            return b;
        }

        // ================================================================ 灌值 / 重画

        void LoadValues()
        {
            _loading = true;
            try { _mail.Text = Store.Get("anki.profile", ""); }
            finally { _loading = false; }
            RefreshUi();
        }

        /// <summary>重画预览 + 刷新「当前：…」，状态行跟着来源退回默认那句话。</summary>
        void RefreshUi()
        {
            _preview.Invalidate();

            _source.Text = SourceLine();
            _source.ForeColor = Ui.SUB;
            _source.Invalidate();

            // 改来源 / 灌值之后，状态行也回到描述来源那句话；上传 / 失败的结果由调用方自己写
            SetStatus(SourceLine(), Ui.SUB);
        }

        /// <summary>「当前：…」那行的文字（读 AvatarStore.Source）。</summary>
        static string SourceLine()
        {
            string src = AvatarStore.Source;
            if (src == "custom") return "当前：自己选的图";
            if (src != null && src.IndexOf("gravatar", StringComparison.OrdinalIgnoreCase) >= 0)
                return "当前：Gravatar 头像";
            return "当前：字母头像";
        }

        /// <summary>后台线程回来时用：弹窗已经关了就直接丢掉。</summary>
        void RefreshSafe()
        {
            if (IsDisposed || Disposing) return;
            try { RefreshUi(); }
            catch { }
        }

        /// <summary>状态行文字（成功绿 / 只在本机琥珀 / 失败红）。</summary>
        void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
            _status.Invalidate();
        }

        // ================================================================ 邮箱

        void OnMailTyped()
        {
            if (_loading) return;
            // 边打边存：邮箱是本地设置，不花什么代价
            Store.Set("anki.profile", _mail.Text.Trim());

            // 停手 800ms 再联网；别每敲一个字符就去打一次 Gravatar
            if (_debounce == null)
            {
                _debounce = new Timer();
                _debounce.Interval = 800;
                _debounce.Tick += delegate
                {
                    _debounce.Stop();
                    OnMailSettled();
                };
            }
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>邮箱改完了：只有「当前不是自定义图」时才重新解析（别拿 Gravatar 盖掉用户自己选的图）。</summary>
        void OnMailSettled()
        {
            if (IsDisposed || Disposing) return;

            string mail = _mail.Text.Trim();
            if (AvatarStore.Source == "custom")
            {
                // 邮箱换了，旧缓存作废（下次 Resolve 会重算），但本机这张图不动
                Store.Set("avatar.email", "");
                RefreshUi();
                return;
            }

            RefreshUi();    // 先把来源刷成最新的，解析结果回来还会再刷一次

            if (mail.Length == 0 || !IsHandleCreated) return;
            AvatarStore.ResolveAsync(this, (MethodInvoker)RefreshSafe);
        }

        // ================================================================ 动作

        void PickImage()
        {
            string path;
            using (var d = new OpenFileDialog())
            {
                d.Title = "选择头像图片";
                d.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" +
                           "|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }

            // 先落本机缓存，再往 Anki 媒体库写：Anki 没开也要能换头像
            try
            {
                // 拷一份再存：Image.FromFile 会让文件一直被占着，用户在原程序里可能还要用
                using (Image raw = Image.FromFile(path))
                using (var copy = new Bitmap(raw))
                    AvatarBadge.SaveImage(copy);
            }
            catch (Exception ex)
            {
                RefreshUi();
                SetStatus("这张图读不进来：" + ex.Message, Ui.RED);
                return;
            }

            RefreshUi();
            SetStatus("图片已存到本机，正在往 Anki 媒体库里写…", Ui.SUB);

            UploadLocal();
        }

        /// <summary>把本机刚存的那张写进 Anki 媒体库（后台，失败只在本弹窗里说一声）。</summary>
        void UploadLocal()
        {
            string err = null;
            SetBusy(true);
            Dlg.LastError = null;   // 静态字段，上一次的错不能算到这一次头上
            Dlg.Wait(this, "账号与头像", "正在把头像写进 Anki 媒体库…", delegate
            {
                using (Image img = AvatarBadge.LoadImage())
                {
                    if (img == null) err = "本机那张读不回来了。";
                    else AvatarStore.Push(img, out err);
                }
            });
            if (err == null && Dlg.LastError != null) err = Dlg.LastError.Message;
            SetBusy(false);

            RefreshUi();
            if (err == null)
            {
                SetStatus("已写入 Anki 媒体库，AnkiWeb 同步后手机上也会变。", Ui.GREEN);
            }
            else
            {
                SetStatus("只存在本机了：" + err, Ui.AMBER);
            }
        }

        void UseGravatar()
        {
            SetBusy(true);
            try
            {
                AvatarStore.DeleteRemote();     // 尽力而为：Anki 没连上也照样换回 Gravatar
            }
            catch (Exception) { }

            AvatarBadge.ClearImage();
            Store.Set("avatar.source", "");
            Store.Set("avatar.email", "");
            SetBusy(false);

            RefreshUi();    // 没有本机图了，来源那行立刻退回「字母头像」

            if (AvatarStore.Email.Length > 0 && IsHandleCreated)
            {
                SetStatus("正在按邮箱取 Gravatar 头像…", Ui.SUB);
                AvatarStore.ResolveAsync(this, (MethodInvoker)RefreshSafe);
            }
            else
            {
                SetStatus("已换回 Gravatar 默认：没填邮箱就先显示字母。", Ui.GREEN);
            }
        }

        /// <summary>等于在 Anki 桌面端点一次同步：AnkiWeb 的登录归 Anki 自己管。</summary>
        void SyncNow()
        {
            if (_busy) return;
            SetBusy(true);
            SetStatus("正在让 Anki 自己同步…", Ui.SUB);

            string err = null;
            Dlg.LastError = null;
            Dlg.Wait(this, "在 Anki 里同步", "正在让 Anki 桌面端同步一次…", delegate
            {
                try { AnkiConn.Sync(); }
                catch (Exception ex) { err = ex.Message; }
            });
            if (err == null && Dlg.LastError != null) err = Dlg.LastError.Message;
            SetBusy(false);

            RefreshUi();
            if (err == null)
                SetStatus("已让 Anki 自己同步了一遍；需要登录的话 Anki 会弹它自己的窗口。", Ui.GREEN);
            else
                SetStatus("同步没成功：" + err, Ui.RED);
        }

        /// <summary>后台活在跑的时候别让人连点（Foot 里的「完成」照旧可用）。</summary>
        void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (Control c in Body.Controls)
            {
                var p = c as Pill;
                if (p != null) p.Enabled = !busy;
            }
        }

        // ================================================================ 对外

        /// <summary>
        /// 造一个已经布局好的弹窗（**不 ShowDialog**）。探针工具靠它拿到实例自己 Show / DrawToBitmap。
        /// </summary>
        public static Form BuildForm()
        {
            return new AvatarDialog();
        }

        /// <summary>
        /// 打开「账号与头像」，关掉之后返回。
        /// 名字跟 Form.Show(IWin32Window) 撞了（那个是"非模态显示"），这里是有意的重载，用 new 挡掉警告。
        /// </summary>
        public new static DialogResult Show(IWin32Window owner)
        {
            using (Form d = BuildForm())
            {
                return d.ShowDialog(owner);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Timer 不停掉，弹窗关掉之后它还会继续 tick
            if (disposing && _debounce != null)
            {
                _debounce.Stop();
                _debounce.Dispose();
                _debounce = null;
            }
            base.Dispose(disposing);
        }

        // ================================================================ 预览方块

        /// <summary>
        /// 72dp 圆角方形预览（圆角半径 = 边长 30%，跟顶栏角标一致）：
        /// 有自定义图就画图（GraphicsPath 裁剪），否则主色底 + 首字母。
        /// </summary>
        class AvatarPreview : Control
        {
            public AvatarPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Ui.CARD;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                if (Parent != null)
                {
                    using (var b = new SolidBrush(Parent.BackColor))
                        g.FillRectangle(b, ClientRectangle);
                }

                var box = new Rectangle(0, 0, Width, Height);
                int radius = (int)Math.Round(Math.Min(Width, Height) * 0.30);
                int side = Math.Min(Width, Height);
                var square = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);

                Image img = AvatarBadge.LoadImage();
                using (GraphicsPath clip = Ui.Round(square, radius))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(clip);
                    if (img != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(img, square);
                    }
                    else
                    {
                        using (var br = new SolidBrush(Ui.ACCENT)) g.FillRectangle(br, square);
                        string letter = Store.Get("anki.profile", "A");
                        if (letter.Length == 0) letter = "A";
                        // Ui.F 是全局缓存字体，不能 Dispose
                        Ui.TextC(g, letter.Substring(0, 1).ToUpperInvariant(),
                            Ui.F(PreviewDp * 0.34f, true), Ui.WHITE, Width / 2, Height / 2);
                    }
                    g.Restore(st);
                }
                if (img != null) img.Dispose();
            }
        }
    }
}
