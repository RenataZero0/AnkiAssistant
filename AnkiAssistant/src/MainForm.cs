using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 主窗口：顶栏 + 左侧导航 + 内容区 + 底部状态栏。
    ///
    /// 三个页面（制卡 / 浏览 / 设置）都是 Panel，切页只是换 Visible，
    /// 各自在 Activate() 里刷新自己需要的数据。
    /// </summary>
    public class MainForm : Form
    {
        public static MainForm Instance;

        Panel _top, _rail, _content, _status;
        Label _statusText;
        RailButton _bCreate, _bBrowse, _bSettings;
        AvatarBadge _avatar;

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
                try { SyncState.Refresh(); } catch { }
                RefreshAvatar();   // 本地缓存 → Anki 媒体库 → Gravatar，后台跑
            };
            FormClosing += delegate { Store.Set("win.w", Width.ToString()); Store.Set("win.h", Height.ToString()); };
        }

        // ===== 顶栏 =====
        void BuildTop()
        {
            _top = new Panel { Dock = DockStyle.Top, Height = Ui.Px(60), BackColor = Ui.CARD };
            _top.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Ui.ACCENT, 1.4f))
                    e.Graphics.DrawLine(p, 0, _top.Height - 1, _top.Width, _top.Height - 1);
            };
            var title = new Label
            {
                Text = "Anki 助手",
                Font = Ui.F(13f, true),
                ForeColor = Ui.INK,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(Ui.Px(22), Ui.Px(18))
            };
            _top.Controls.Add(title);

            var ver = new Label
            {
                Text = GitHub.Clean(GitHub.VersionTag),
                Font = Ui.F(8.5f),
                ForeColor = Ui.SUB,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Ui.PANEL,
                Size = new Size(Ui.Px(64), Ui.Px(24)),
                Location = new Point(_top.Width, Ui.Px(18)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            ver.Location = new Point(ClientSize.Width - Ui.Px(86), Ui.Px(18));
            _top.Controls.Add(ver);
            _top.Resize += delegate { ver.Location = new Point(_top.Width - Ui.Px(86), Ui.Px(18)); };

            _avatar = new AvatarBadge();
            _avatar.Size = new Size(Ui.Px(36), Ui.Px(36));
            _avatar.Location = new Point(_top.Width - Ui.Px(136), Ui.Px(12));
            _avatar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _avatar.Click += delegate { ShowConnDialog(); };
            _top.Controls.Add(_avatar);
            _top.Resize += delegate { _avatar.Location = new Point(_top.Width - Ui.Px(136), Ui.Px(12)); };
        }

        // ===== 侧栏 =====
        void BuildRail()
        {
            _rail = new Panel { Dock = DockStyle.Left, Width = Ui.Px(92), BackColor = Ui.CARD, Padding = new Padding(Ui.Px(10), Ui.Px(14), Ui.Px(10), Ui.Px(10)) };
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
            _status = new Panel { Dock = DockStyle.Bottom, Height = Ui.Px(34), BackColor = Ui.CARD };
            _status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Ui.LINE))
                    e.Graphics.DrawLine(p, 0, 0, _status.Width, 0);
            };
            _statusText = new Label
            {
                Text = "正在检查 Anki…",
                Font = Ui.F(8.5f),
                ForeColor = Ui.SUB,
                AutoSize = false,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.Px(18), 0, 0, 0)
            };
            _statusText.Click += delegate { ShowPage(2); };
            _statusText.Cursor = Cursors.Hand;
            _status.Controls.Add(_statusText);
        }

        void BuildContent()
        {
            _content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.BG, Padding = new Padding(Ui.Px(16), Ui.Px(14), Ui.Px(16), Ui.Px(12)) };
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
            _statusText.Text = text;
            _statusText.ForeColor = warn ? Ui.RED : Ui.SUB;
        }

        /// <summary>AnkiConnect 状态变了以后刷新角标和状态栏。</summary>
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

        void ShowConnDialog()
        {
            using (var d = new ConnDialog()) d.ShowDialog(this);
        }
    }
}
