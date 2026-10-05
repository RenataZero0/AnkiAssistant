using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AnkiAssistant.Setup
{
    /// <summary>
    /// 安装向导。配色与主程序保持一致：
    /// 底 #F3F5F9 / 卡片白 / 文字 #1B2432 / 次要 #71809A / 强调 #3568E8 / 分隔线 #E5E9F0
    /// </summary>
    public class WizardForm : Form
    {
        static readonly Color Bg = ColorTranslator.FromHtml("#F3F5F9");
        static readonly Color Ink = ColorTranslator.FromHtml("#1B2432");
        static readonly Color Sub = ColorTranslator.FromHtml("#71809A");
        static readonly Color Accent = ColorTranslator.FromHtml("#3568E8");
        static readonly Color Line = ColorTranslator.FromHtml("#E5E9F0");
        static readonly Color AccentSoft = ColorTranslator.FromHtml("#E8EFFE");

        readonly Panel _body = new Panel();
        readonly Label _step = new Label();
        readonly Button _back = new Button();
        readonly Button _next = new Button();
        readonly Button _cancel = new Button();

        // 页面控件
        Panel _pageWelcome, _pageDir, _pageOpts, _pageBusy, _pageDone;
        TextBox _dirBox;
        CheckBox _desktop, _autostart, _runNow;
        ProgressBar _bar;
        Label _busyText, _doneText;

        int _page = 0;
        int _scale = 1;
        int _shotPage = -1;               // 截图模式正在画第几页（用来定页脚文字）
        bool _shot;                       // 截图模式：只摆界面，不做实际操作

        public bool LaunchAfter { get; private set; }
        public string TargetDir { get; private set; }

        /// <summary>
        /// 离屏截图为指定页面摆好界面（不会真的安装）。
        /// page 用 1..4 计（1 欢迎 / 2 安装位置 / 3 安装选项 / 4 安装中），
        /// 装完那页用 5。
        /// </summary>
        public void PrepareForShot(int page)
        {
            _shot = true;
            if (page < 1) page = 1;
            int n = page - 1;
            if (n > 4) n = 4;
            _shotPage = n;

            if (n == 3)
            {
                // 「安装中」那页在真实流程里一进去就开始装，截图时只伪造进度条
                ShowPage(3, true);
                _bar.Value = 62;
                _busyText.Text = "正在创建快捷方式…";
            }
            else if (n == 4)
            {
                ShowPage(4, true);
                _doneText.Text = "已安装到：\n" + Installer.DefaultDir
                               + "\n\n设置与缓存保存在：" + Installer.UserDataDir;
            }
            else ShowPage(n, true);
        }

        public WizardForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                _scale = Math.Max(1, (int)Math.Round(g.DpiX / 96.0));

            Text = "安装 Anki 助手 AnkiAssistant";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(Px(600), Px(430));
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f * _scale / _scale, FontStyle.Regular);

            BuildHeader();
            BuildBody();
            BuildFooter();

            ShowPage(0);
        }

        int Px(int v) { return v * _scale; }

        // ================================================================ 骨架
        void BuildHeader()
        {
            var head = new Panel();
            head.SetBounds(0, 0, ClientSize.Width, Px(86));
            head.BackColor = Color.White;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(Line, 1))
                    e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            Controls.Add(head);

            var icon = new PictureBox();
            icon.SetBounds(Px(24), Px(20), Px(46), Px(46));
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            try { icon.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
            head.Controls.Add(icon);

            var title = new Label();
            title.Text = "Anki 助手 AnkiAssistant";
            title.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            title.ForeColor = Ink;
            title.SetBounds(Px(84), Px(18), Px(460), Px(28));
            head.Controls.Add(title);

            var sub = new Label();
            sub.Text = "版本 " + SetupInfo.Version + "　·　"
                     + (Installer.AlreadyInstalled ? "已安装，将覆盖升级" : "全新安装");
            sub.ForeColor = Sub;
            sub.SetBounds(Px(86), Px(46), Px(460), Px(22));
            head.Controls.Add(sub);
        }

        void BuildBody()
        {
            _body.SetBounds(0, Px(86), ClientSize.Width, Px(268));
            _body.BackColor = Bg;
            Controls.Add(_body);

            // ---------- 欢迎 ----------
            _pageWelcome = NewPage();
            AddText(_pageWelcome, "这个程序会做什么", 16, true, Ink);
            AddText(_pageWelcome,
                "· 内置官方 Anki 引擎：自己登录 AnkiWeb、自己同步，不需要 AnkiConnect 插件\n" +
                "· 浏览牌组与卡片、新建/编辑卡片，可以交给 AI 帮你写例句和释义\n" +
                "· 内置卡面编辑器，排版、主题、头像都在这里调\n" +
                "· 检查更新并从 GitHub 下载新版本",
                16, false, Sub, 42, 120);
            AddText(_pageWelcome,
                "安装是**每用户**的：装到你的用户目录，不需要管理员权限，也不会弹 UAC。",
                16, false, Sub, 188, 240);

            // ---------- 安装位置 ----------
            _pageDir = NewPage();
            AddText(_pageDir, "选择安装位置", 16, true, Ink);
            AddText(_pageDir, "程序文件会装到这里：", 16, false, Sub, 42);
            _dirBox = new TextBox();
            _dirBox.SetBounds(Px(20), Px(96), Px(430), Px(28));
            _dirBox.Text = Installer.DefaultDir;
            _pageDir.Controls.Add(_dirBox);

            var browse = new Button();
            browse.Text = "浏览…";
            browse.SetBounds(Px(460), Px(95), Px(96), Px(30));
            browse.Click += delegate
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.Description = "选择安装位置";
                    d.SelectedPath = _dirBox.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) _dirBox.Text = d.SelectedPath;
                }
            };
            _pageDir.Controls.Add(browse);

            AddText(_pageDir,
                "设置和缓存保存在\n" + Installer.UserDataDir + "\n卸载时可以选择保留。",
                16, false, Sub, 140, 240);

            // ---------- 选项 ----------
            _pageOpts = NewPage();
            AddText(_pageOpts, "安装选项", 16, true, Ink);
            _desktop = NewCheck(_pageOpts, "创建桌面快捷方式", true, 62);
            _autostart = NewCheck(_pageOpts, "开机自动启动（后台待命，随叫随到）", true, 100);
            AddText(_pageOpts,
                "开始菜单快捷方式一定会创建。\n\n" +
                "开机自启写的是「\"程序路径\" --tray」，可以在应用设置里随时关掉。",
                16, false, Sub, 148, 200);

            // ---------- 安装中 ----------
            _pageBusy = NewPage();
            AddText(_pageBusy, "正在安装…", 16, true, Ink);
            _bar = new ProgressBar();
            _bar.SetBounds(Px(20), Px(80), Px(536), Px(8));
            _bar.Style = ProgressBarStyle.Continuous;
            _pageBusy.Controls.Add(_bar);
            _busyText = new Label();
            _busyText.SetBounds(Px(20), Px(100), Px(536), Px(24));
            _busyText.ForeColor = Sub;
            _busyText.Text = "准备中…";
            _pageBusy.Controls.Add(_busyText);

            // ---------- 完成 ----------
            _pageDone = NewPage();
            AddText(_pageDone, "安装完成", 16, true, Ink);
            _doneText = new Label();
            _doneText.SetBounds(Px(20), Px(44), Px(536), Px(80));
            _doneText.ForeColor = Sub;
            _doneText.Text = "";
            _pageDone.Controls.Add(_doneText);
            _runNow = NewCheck(_pageDone, "立即运行 Anki 助手", true, 140);
        }

        Panel NewPage()
        {
            var p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Bg;
            p.Visible = false;
            _body.Controls.Add(p);
            return p;
        }

        void AddText(Control parent, string text, int x, bool bold, Color color, int y = 0, int h = 0)
        {
            var l = new Label();
            l.Text = text.Replace("**", "");
            if (bold) l.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            l.ForeColor = color;
            l.SetBounds(Px(x), Px(y == 0 ? 20 : y), Px(560), Px(h == 0 ? 30 : h));
            parent.Controls.Add(l);
        }

        CheckBox NewCheck(Control parent, string text, bool on, int y)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Checked = on;
            c.ForeColor = Ink;
            c.SetBounds(Px(20), Px(y), Px(540), Px(26));
            parent.Controls.Add(c);
            return c;
        }

        void BuildFooter()
        {
            var foot = new Panel();
            foot.SetBounds(0, ClientSize.Height - Px(76), ClientSize.Width, Px(76));
            foot.BackColor = Color.White;
            foot.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(Line, 1))
                    e.Graphics.DrawLine(p, 0, 0, foot.Width, 0);
            };
            Controls.Add(foot);

            _step.SetBounds(Px(24), Px(28), Px(300), Px(22));
            _step.ForeColor = Sub;
            foot.Controls.Add(_step);

            _cancel.Text = "取消";
            _cancel.SetBounds(ClientSize.Width - Px(24) - Px(88), Px(22), Px(88), Px(32));
            _cancel.Click += delegate { Close(); };
            foot.Controls.Add(_cancel);

            _next.Text = "下一步";
            _next.SetBounds(ClientSize.Width - Px(24) - Px(88) - Px(8) - Px(112), Px(22), Px(112), Px(32));
            _next.BackColor = Accent;
            _next.ForeColor = Color.White;
            _next.FlatStyle = FlatStyle.Flat;
            _next.FlatAppearance.BorderSize = 0;
            _next.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _next.Click += delegate { NextPage(); };
            foot.Controls.Add(_next);

            _back.Text = "上一步";
            _back.SetBounds(ClientSize.Width - Px(24) - Px(88) - Px(8) - Px(112) - Px(8) - Px(88), Px(22), Px(88), Px(32));
            _back.FlatStyle = FlatStyle.Flat;
            _back.FlatAppearance.BorderColor = Line;
            _back.Click += delegate { ShowPage(_page - 1); };
            foot.Controls.Add(_back);
        }

        // ================================================================ 翻页
        void ShowPage(int n) { ShowPage(n, false); }

        void ShowPage(int n, bool noAction)
        {
            if (n < 0) n = 0;
            _page = n;
            Panel[] pages = { _pageWelcome, _pageDir, _pageOpts, _pageBusy, _pageDone };
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = (i == n);

            _back.Visible = (n == 1 || n == 2);
            _cancel.Visible = (n <= 2);
            _next.Visible = (n <= 2);
            int shown = (_shot && _shotPage >= 0) ? _shotPage : n;
            _step.Text = (shown <= 2) ? ("第 " + (shown + 1) + " / 3 步") : "";
            _next.Text = (n == 2) ? "安装" : "下一步";

            if (n == 3 && !noAction) DoInstall();
            if (n == 4)
            {
                _next.Visible = true;
                _next.Text = "完成";
                _next.Click -= OnFinishClick;
                _next.Click += OnFinishClick;
            }
        }

        void NextPage()
        {
            if (_page == 2) { ShowPage(3); return; }
            if (_page == 4) { OnFinishClick(null, null); return; }
            if (_page == 1)
            {
                string d = _dirBox.Text.Trim();
                if (d.Length == 0) { MessageBox.Show(this, "请填写安装位置。", "提示"); return; }
                try { Directory.CreateDirectory(d); }
                catch (Exception ex) { MessageBox.Show(this, "这个位置不能写入：\n" + ex.Message, "提示"); return; }
                TargetDir = d;
            }
            ShowPage(_page + 1);
        }

        // ================================================================ 干活
        void DoInstall()
        {
            _bar.Value = 0;
            Application.DoEvents();
            try
            {
                var p = new Installer.Progress();
                p.Report = delegate (int pct, string msg)
                {
                    _bar.Value = Math.Max(0, Math.Min(100, pct));
                    _busyText.Text = msg;
                    Application.DoEvents();
                };
                Installer.Install(TargetDir, _desktop.Checked, _autostart.Checked, p);
                _doneText.Text = "已安装到：\n" + TargetDir
                               + "\n\n设置与缓存保存在：" + Installer.UserDataDir;
                LaunchAfter = _runNow.Checked;
                ShowPage(4);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装失败：\n" + ex.Message, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(1);
            }
        }

        void OnFinishClick(object s, EventArgs e)
        {
            LaunchAfter = _runNow.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>卸载向导（由已安装目录里的「卸载 Anki 助手.exe」调用）</summary>
    public static class UninstallFlow
    {
        public static int Run(bool silent)
        {
            bool removeData = false;

            if (!silent)
            {
                string msg = "确定要卸载 Anki 助手 AnkiAssistant 吗？\n\n"
                           + "程序文件和快捷方式会被删除。\n"
                           + "设置与缓存保存在：\n" + Installer.UserDataDir;
                var r = MessageBox.Show(msg, "卸载 Anki 助手",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return 0;

                var r2 = MessageBox.Show(
                    "要一并删除设置和缓存吗？\n\n"
                    + "选「否」的话会保留，以后重新安装还在。",
                    "保留设置和缓存？", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                removeData = (r2 == DialogResult.Yes);
            }

            var p = new Installer.Progress();
            p.Report = delegate (int pct, string msg) { if (!silent) Console.WriteLine(msg); };
            Installer.Uninstall(removeData, p);

            if (!silent)
                MessageBox.Show("卸载完成。", "卸载 Anki 助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }
}
