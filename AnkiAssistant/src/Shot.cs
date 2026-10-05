using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// `--shot &lt;png&gt; [--page create|browse|settings]`：离屏截图，给 README / 更新说明配图用。
    ///
    /// 三个要点：
    ///   1. 窗口挪到 (-4000, -4000)，用户看不到它闪出来
    ///   2. 自己跑 DoEvents 循环等布局和后台探活，**不调 Application.Run**
    ///      （一旦 Run 起来就得等窗口关掉，命令行截图会永远不返回）
    ///   3. 画完 Environment.Exit(0)：后台探活线程还挂着，正常退出会被它拖住
    /// </summary>
    public static class Shot
    {
        public static void Run(string path, string page)
        {
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Store.ExeDir, "shot.png");
            else path = Path.GetFullPath(path);
            if (string.IsNullOrEmpty(page)) page = "create";

            // ==== 临时（验证自绘弹窗用，验完删） ====
            if (page.StartsWith("dlg:", StringComparison.OrdinalIgnoreCase))
            {
                ShotDialog(path, page.Substring(4).ToLowerInvariant());
                return;
            }

            // ==== 临时：dark: 前缀用来出一张深色皮肤的图（验完删） ====
            bool darkSkin = false;
            if (page.StartsWith("dark:", StringComparison.OrdinalIgnoreCase))
            {
                darkSkin = true;
                page = page.Substring(5);
            }

            // 先建窗再套皮肤，理由和 MainForm 里一致：控件用构造时的 Ui.XXX 取色
            Theme.Apply(darkSkin ? Theme.Get("dark") : Theme.Get(Store.ThemeId));

            MainForm f = null;
            try
            {
                f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);   // 屏幕外，别让人看见
                f.Show();

                // 等布局跑完；Shown 里发起的 SyncState.Refresh 在后台线程，这里正好等它回来
                Pump(40, 50);

                int p = 0;
                if (string.Equals(page, "browse", StringComparison.OrdinalIgnoreCase)) p = 1;
                else if (string.Equals(page, "settings", StringComparison.OrdinalIgnoreCase)) p = 2;
                f.ShowPage(p);
                Pump(12, 50);

                // 非客户区（标题栏、边框）不计入 ClientSize，整窗抓图要按外框尺寸
                int w = f.Width, h = f.Height;
                using (var bmp = new Bitmap(w, h))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    bmp.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("SHOT -> " + path + " (" + w + "x" + h + ")");
            }
            catch (Exception e)
            {
                Console.WriteLine("SHOT FAILED: " + e.Message);
            }
            finally
            {
                try { if (f != null) f.Dispose(); } catch { }
            }

            // 后台探活线程还开着，Application.Exit 不一定收得干净
            Environment.Exit(0);
        }

        /// <summary>跑 <paramref name="rounds"/> 轮消息泵，每轮歇 <paramref name="ms"/> 毫秒。</summary>
        static void Pump(int rounds, int ms)
        {
            for (int i = 0; i < rounds; i++)
            {
                Application.DoEvents();
                Thread.Sleep(ms);
            }
        }

        // ==== 临时：抓自绘弹窗（验完删） ====
        static void ShotDialog(string path, string which)
        {
            Theme.Apply(Theme.Get(Store.ThemeId));
            var t = new System.Windows.Forms.Timer();
            t.Interval = 1200;
            t.Tick += delegate(object s, EventArgs e)
            {
                t.Stop();
                Form act = null;
                foreach (Form f in Application.OpenForms)
                {
                    if (f != null && f.Visible) act = f;
                }
                if (act == null)
                {
                    Console.WriteLine("SHOT FAILED: no active form");
                    Environment.Exit(1);
                }
                using (var bmp = new Bitmap(act.Width, act.Height))
                {
                    act.DrawToBitmap(bmp, new Rectangle(0, 0, act.Width, act.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("SHOT -> " + path + " (" + act.Width + "x" + act.Height + ")");
                act.Close();
            };
            t.Start();

            if (which == "choose")
            {
                string[] items = new string[14];
                for (int i = 0; i < items.Length; i++) items[i] = "NCUK::Maths::Deck::" + (i + 1);
                Dlg.Choose(null, "选择牌组", "卡片会放进选中的牌组下面。", items, null, 3, null);
            }
            else if (which == "confirm")
            {
                Dlg.Confirm(null, "删除卡片", "删掉之后就找不回来了。",
                    "真的要把这张卡片从 Anki 里删掉吗？本机卡片和 AnkiWeb 都会少一张。",
                    "删除", "取消", true);
            }
            else if (which == "avatar")
            {
                AvatarDialog.Show(null);
            }
            else if (which == "web")
            {
                WebDialog.Show(null);
            }
            else if (which == "form")
            {
                Dlg.Form(null, "新建输出格式", "照着你的学科改就行。",
                    new string[] { "名称", "笔记类型", "默认牌组" },
                    new string[] { "英语词汇", "AnkiAssistant", "英语::词汇" });
            }
            else if (which == "config")
            {
                var d = new ConfigEditor(null, CardConfig.VocabConfig());
                d.ShowDialog(null);
            }
            else if (which == "configdark")
            {
                Theme.Apply(Theme.Get("dark"));
                var d2 = new ConfigEditor(null, CardConfig.VocabConfig());
                d2.ShowDialog(null);
            }
            else if (which == "choosedark")
            {
                Theme.Apply(Theme.Get("dark"));
                string[] its = new string[14];
                for (int i = 0; i < its.Length; i++) its[i] = "NCUK::Maths::Deck::" + (i + 1);
                Dlg.Choose(null, "选择牌组", "卡片会放进选中的牌组下面。", its, null, 3, null);
            }
            Environment.Exit(0);
        }
    }
}
