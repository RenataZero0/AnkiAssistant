using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 「Anki 连接」弹窗（点主界面右上角头像打开，见 MainForm.ShowConnDialog）。
    ///
    /// 它是连接问题的唯一处理入口：说清楚现在连没连上、贴出装 AnkiConnect 的步骤、
    /// 允许改插件的监听地址，并给出「打开 Anki / 重新检查 / 同步 AnkiWeb」三个动作。
    ///
    /// 几条约定：
    ///   · 状态的唯一真相是 SyncState（角标和状态栏都看它），本弹窗只负责显示；
    ///   · 跟 Anki 说话全在后台线程，结果用 BeginInvoke 回 UI；
    ///   · 后台干着活时动作按钮禁掉，但「关闭」永远可用 —— 别把人关在弹窗里。
    /// </summary>
    public class ConnDialog : DlgForm
    {
        /// <summary>AnkiConnect 默认监听地址（插件默认端口就是 8765）。</summary>
        const string DefEndpoint = "127.0.0.1:8765";

        Panel _status;                  // 状态区：大字 + 圆点 + 一行灰字
        Input _box;                   // 地址输入框
        Pill _bRecheck, _bOpen, _bSync; // 三个动作按钮（「关闭」由父类 Button 造）

        SyncKind _kind = SyncKind.Unknown;
        int _ver;                       // AnkiConnect 版本号，0 = 还不知道
        bool _busy;                     // 有后台活在跑
        int _ticks;

        // SyncState 的检查在自己的后台线程里跑，没有回调可接，只能过一会儿看它一眼。
        // 注意写全名：System.Threading 和 System.Windows.Forms 里都有 Timer。
        System.Windows.Forms.Timer _poll;

        public ConnDialog() : base("Anki 连接", null, 460)
        {
            // 父类在 sub = null 时标题区只有 56 高、也只画了标题，
            // 这里把标题区撑到 74 并**追加**一行说明（+= 而不是 =，别把父类的标题盖掉）。
            Head.Height = Ui.Px(74);
            Head.Paint += delegate(object s, PaintEventArgs e)
            {
                Ui.Text(e.Graphics, "让本程序连上你电脑上的 Anki 桌面端", Ui.F(9f), Ui.SUB,
                    Ui.Px(Pad), Ui.Px(44));
            };

            Body.AutoScroll = true;     // 屏幕矮的时候正文至少能滚，不至于看不见地址框

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 300;
            _poll.Tick += delegate { PollTick(); };

            KeyPreview = true;          // Esc 关窗
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            BuildBody();
            BuildButtons();

            FitBody(388);
            Reload();
            Shown += delegate { Recheck(); };
        }

        // ===== 正文 =====
        void BuildBody()
        {
            int yDp = 6;

            // ---- 状态区 ----
            _status = new Panel
            {
                Location = new Point(Ui.Px(Pad), Ui.Px(yDp)),
                Size = new Size(BodyWidth, Ui.Px(48)),
                BackColor = Ui.CARD
            };
            _status.Paint += delegate(object s, PaintEventArgs e) { PaintStatus(e.Graphics); };
            Body.Controls.Add(_status);
            yDp += 48 + 8;

            // ---- 安装说明 ----
            // MarkdownView 自己留了 18 的内边距，往左挪回去让文字跟别处对齐。
            string md =
                "### 这个程序怎么把卡片写进 Anki\n" +
                "它通过 **AnkiConnect** 插件跟本机的 Anki 桌面端通信 —— 卡片直接进你的收藏库，" +
                "再交给 Anki 自己同步到 AnkiWeb，不需要登录也不需要密码。\n\n" +
                "### 没连上？按这三步来\n" +
                "1. 打开 Anki 桌面端\n" +
                "2. `工具 → 插件 → 获取插件`，输入编号 **" + AnkiConn.AddonCode + "**，装完重启 Anki\n" +
                "3. 确认端口是 **8765**（插件默认值；改过就在这里填一样的）\n";

            var view = new MarkdownView
            {
                Location = new Point(Ui.Px(Pad - 18), Ui.Px(yDp)),
                Size = new Size(BodyWidth + Ui.Px(36), Ui.Px(240)),
                Markdown = md
            };
            Body.Controls.Add(view);
            yDp += 240 + 8;

            // ---- 地址 ----
            var lab = new Label
            {
                Text = "AnkiConnect 地址",
                Font = Ui.F(9f),
                ForeColor = Ui.SUB,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(Ui.Px(Pad), Ui.Px(yDp))
            };
            Body.Controls.Add(lab);
            yDp += 22;

            _box = new Input();
            _box.Text = ToUi(Store.Get("anki.endpoint", DefEndpoint));
            _box.Font = Ui.F(10f);
            _box.Location = new Point(Ui.Px(Pad), Ui.Px(yDp));
            _box.Size = new Size(BodyWidth, Ui.Px(32));
            _box.TextChanged += delegate
            {
                // 边打边存。空值不写：手一抖清空了，别把还能用的地址冲掉。
                string v = ToStore(_box.Text);
                if (v.Length > 0) Store.Set("anki.endpoint", v);
            };
            _box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Recheck(); }
            };
            Body.Controls.Add(_box);
            _box.Leave += delegate
            {
                if (_box.Text.Trim().Length == 0)
                {
                    _box.Text = DefEndpoint;
                    Store.Set("anki.endpoint", ToStore(DefEndpoint));
                }
            };
            yDp += 32 + 8;

            var hint = new Label
            {
                Text = "改完点『重新检查』",
                Font = Ui.F(8.5f),
                ForeColor = Ui.SUB,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(Ui.Px(Pad), Ui.Px(yDp))
            };
            Body.Controls.Add(hint);
        }

        void BuildButtons()
        {
            // Foot 是 RightToLeft 排的，先加的靠右。
            // 这三个按钮点了不能关窗，所以不能走父类的 Button()（它无条件 Close()），
            // 自己造 Pill，尺寸算法跟父类保持一致。
            _bSync = Action("同步 AnkiWeb", true);
            _bSync.Click += delegate { SyncNow(); };

            _bOpen = Action("打开 Anki", false);
            _bOpen.Click += delegate { OpenAnki(); };

            _bRecheck = Action("重新检查", false);
            _bRecheck.Click += delegate { Recheck(); };

            Button("关闭", false, DialogResult.Cancel);
        }

        /// <summary>造一个只干活不关窗的胶囊按钮（尺寸照抄 DlgForm.Button）。</summary>
        Pill Action(string text, bool primary)
        {
            var b = new Pill { Text = text, Primary = primary, Font = Ui.F(9.5f, primary) };
            b.Size = new Size(Pill.Measure(text, Ui.F(9.5f, primary)) + Ui.Px(16), Ui.Px(34));
            b.Margin = new Padding(Ui.Px(8), 0, 0, 0);
            Foot.Controls.Add(b);
            return b;
        }

        // ===== 状态显示 =====
        /// <summary>状态区重画：大字 + 圆点 + 一行灰字（刷新只重画这里，不重建弹窗）。</summary>
        void PaintStatus(Graphics g)
        {
            string big;
            Color dot;
            if (_kind == SyncKind.Ok)
            {
                big = _ver > 0 ? "已连接 Anki　·　AnkiConnect " + _ver : "已连接 Anki";
                dot = Ui.GREEN;
            }
            else if (_kind == SyncKind.Error)
            {
                big = "没有连上 Anki";
                dot = Ui.RED;
            }
            else
            {
                big = "正在检查…";
                dot = Ui.TEXT_DIM;
            }

            using (var f = Ui.F(11f, true))
            {
                Ui.Text(g, big, f, Ui.INK, 0, Ui.Px(2));
                // 圆点跟在字后面，得自己量一下字宽
                SizeF sz = g.MeasureString(big, f);
                int d = Ui.Px(9);
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, (int)Math.Round(sz.Width) + Ui.Px(4),
                        Ui.Px(2) + (Ui.Px(17) - d) / 2, d, d);
            }
            using (var f = Ui.F(8.5f))
                Ui.Text(g, SyncState.StatusText(), f, Ui.SUB, 0, Ui.Px(29));

            // 一条细线，把状态区和下面的说明隔开
            using (var p = new Pen(Ui.LINE))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        /// <summary>按 SyncState 画一遍（UI 线程）。</summary>
        void Reload()
        {
            _kind = SyncState.Kind;
            _ver = SyncState.AnkiConnectVersion;
            _status.Invalidate();
        }

        /// <summary>后台活跑着的时候禁掉动作按钮，别让人连点。</summary>
        void SetBusy(bool busy)
        {
            _busy = busy;
            _bRecheck.Enabled = !busy;
            _bOpen.Enabled = !busy;
            _bSync.Enabled = !busy;
            _bSync.Text = busy ? "同步中…" : "同步 AnkiWeb";
            _bSync.Invalidate();
        }

        // ===== 动作 =====
        /// <summary>重新探一次：全局状态交给 SyncState，本地再快问一次版本号。</summary>
        void Recheck()
        {
            if (_busy) return;
            SetBusy(true);

            SyncState.Refresh();    // 角标、状态栏、牌组缓存都靠它刷新；它是幂等的
            ProbeVersion();         // 只问版本号，比 SyncState 那一整轮查询快得多

            _ticks = 0;
            _poll.Stop();
            _poll.Start();
        }

        void ProbeVersion()
        {
            _kind = SyncKind.Busy;
            _ver = 0;
            _status.Invalidate();

            var t = new Thread(delegate()
            {
                bool ok = false;
                int v = 0;
                try { v = AnkiConn.Version(); ok = true; }
                catch (Exception) { }       // 连不上不在这里解释，SyncState 那轮会把原因写进 Detail
                try
                {
                    BeginInvoke((MethodInvoker)delegate()
                    {
                        _kind = ok ? SyncKind.Ok : SyncKind.Error;
                        _ver = ok ? v : 0;
                        _status.Invalidate();
                    });
                }
                catch { }                   // 弹窗已经关了
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>等 SyncState 那轮检查落地，然后以它为准收口。</summary>
        void PollTick()
        {
            _ticks++;
            if (SyncState.Kind == SyncKind.Busy && _ticks < 40) return;   // 最多等 12 秒
            _poll.Stop();
            Reload();
            SetBusy(false);
        }

        /// <summary>打开本机的 Anki；找不到就把下载页打开。</summary>
        void OpenAnki()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] tries = new string[]
            {
                "anki.exe",                                             // PATH 里能找到就用它
                Path.Combine(local, @"Programs\Anki\anki.exe"),         // 默认安装位置
                @"C:\Program Files\Anki\anki.exe"
            };
            foreach (string exe in tries)
            {
                try { Process.Start(exe); return; }
                catch (Exception) { }
            }
            try { Process.Start("https://apps.ankiweb.net/"); }
            catch (Exception) { }
        }

        /// <summary>让 Anki 自己同步到 AnkiWeb（等于在 Anki 里点那个同步按钮）。</summary>
        void SyncNow()
        {
            if (_busy) return;
            SetBusy(true);

            var t = new Thread(delegate()
            {
                string err = null;
                try
                {
                    SyncState.RequireConnected();   // 没连上会抛一段中文说明
                    AnkiConn.Sync();
                }
                catch (Exception ex) { err = ex.Message; }

                string msg = err;
                try
                {
                    BeginInvoke((MethodInvoker)delegate()
                    {
                        SetBusy(false);
                        if (msg == null)
                            Dlg.Info(this, "同步 AnkiWeb", null,
                                "已经让 Anki 自己走了一遍同步：本机收藏库传到 AnkiWeb，" +
                                "再把别处的改动拉回来。卡片现在在 Anki 的手机端、网页端都能看到了。", 120);
                        else
                            Dlg.Info(this, "同步 AnkiWeb 没成功", null, msg, 140);
                        Recheck();      // 同步会改待复习数量，顺手再看一眼
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ===== 地址换算 =====
        // 界面上写 host:port（跟插件里的默认值长得一样），存进 Store 的补上 http://。
        // 因为 AnkiConn 直接把这个值丢给 WebRequest.Create，少了 scheme 会当场抛异常。
        static string ToStore(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "http://" + text;
            return text;
        }

        static string ToUi(string stored)
        {
            stored = (stored ?? "").Trim();
            if (stored.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                stored = stored.Substring(7);
            return stored;
        }
    }
}
