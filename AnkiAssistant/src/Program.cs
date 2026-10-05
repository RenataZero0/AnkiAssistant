using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 程序入口。
    ///
    /// 启动顺序有讲究：
    ///   DPI 感知 → TLS（GitHub.Init）→ 算缩放系数 → 应用皮肤 → 命令行分支 → 单实例 → 主窗口
    /// 命令行（一般用不到）：
    ///   --tray            只起托盘（开机自启用；本版没有托盘，等价于正常启动）
    ///   --selftest        跑一遍自检写 selftest.txt
    ///   --shot <png>      离屏截图（写文档用，见 README）
    /// </summary>
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        /// <summary>主窗口（字段名不能叫 Main —— 会和入口方法重名）。</summary>
        public static MainForm Form;

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            try { GitHub.Init(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // DPI 缩放：进程 DPI 感知之后，96 DPI 就是 1.0
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Ui.S = g.DpiX / 96f;
            }
            catch { Ui.S = 1f; }

            Theme.Apply(Theme.Get(Store.ThemeId));

            if (Has(args, "--selftest")) { SelfTest.Run(); return; }

            if (Has(args, "--shot"))
            {
                Shot.Run(Value(args, "--shot"), Value(args, "--page"));
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "AnkiAssistant_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Anki 助手已经在运行了。", "Anki 助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Form = new MainForm();
                Application.Run(Form);
            }
        }

        public static bool Has(string[] a, string flag)
        {
            foreach (string s in a) if (string.Equals(s, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Value(string[] a, string flag)
        {
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return "";
        }
    }

    /// <summary>侧栏图标：全部用代码画，不依赖图片资源。</summary>
    public static class Icons
    {
        public static void Draw(Graphics g, string kind, Rectangle r, Color c, float w = 1.8f)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, w))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                if (kind == "create")   // 方框 + 加号
                {
                    var box = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
                    Ui.StrokeRound(g, box, 4, c, w);
                    g.DrawLine(p, r.X + r.Width / 2, r.Y + 8, r.X + r.Width / 2, r.Bottom - 8);
                    g.DrawLine(p, r.X + 8, r.Y + r.Height / 2, r.Right - 8, r.Y + r.Height / 2);
                }
                else if (kind == "browse")   // 卡片列表
                {
                    var box = new Rectangle(r.X + 3, r.Y + 4, r.Width - 6, r.Height - 8);
                    Ui.StrokeRound(g, box, 4, c, w);
                    g.DrawLine(p, r.X + 7, r.Y + r.Height / 2 - 3, r.Right - 7, r.Y + r.Height / 2 - 3);
                    g.DrawLine(p, r.X + 7, r.Y + r.Height / 2 + 3, r.Right - 7, r.Y + r.Height / 2 + 3);
                }
                else if (kind == "settings")  // 三条滑杆
                {
                    int[] ys = { r.Y + 7, r.Y + r.Height / 2, r.Bottom - 7 };
                    int[] xs = { r.X + r.Width - 8, r.X + 8, r.X + r.Width - 8 };
                    for (int i = 0; i < 3; i++)
                    {
                        g.DrawLine(p, r.X + 4, ys[i], r.Right - 4, ys[i]);
                        using (var b = new SolidBrush(c)) g.FillEllipse(b, xs[i] - 3, ys[i] - 3, 6, 6);
                    }
                }
                else if (kind == "sync")   // 转圈箭头
                {
                    var box = new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);
                    g.DrawArc(p, box, 40, 260);
                    g.DrawLine(p, r.X + 4, r.Y + 7, r.X + 4, r.Y + 13);
                    g.DrawLine(p, r.X + 4, r.Y + 7, r.X + 10, r.Y + 7);
                }
            }
            g.SmoothingMode = old;
        }
    }

    /// <summary>侧栏上的一个按钮（图标 + 文字，选中时浅色胶囊底）。</summary>
    public class RailButton : Control
    {
        public string Kind = "create";
        public string Label = "";
        public bool Selected;
        bool _hover;

        public RailButton(string kind, string label)
        {
            Kind = kind; Label = label;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Ui.F(8.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.CARD))
                g.FillRectangle(b, ClientRectangle);

            if (Selected) Ui.FillRound(g, new Rectangle(0, 0, Width, Height), Ui.Px(12), Ui.ACCENT_SOFT);
            else if (_hover) Ui.FillRound(g, new Rectangle(0, 0, Width, Height), Ui.Px(12), Theme.Alpha(Ui.ACCENT, 18));

            Color c = Selected ? Ui.ACCENT : Ui.SUB;
            int iconSize = Ui.Px(20);
            Icons.Draw(g, Kind, new Rectangle((Width - iconSize) / 2, Ui.Px(12), iconSize, iconSize), c);
            Ui.TextC(g, Label, Font, c, Width / 2, Height - Ui.Px(24));
        }
    }
}
