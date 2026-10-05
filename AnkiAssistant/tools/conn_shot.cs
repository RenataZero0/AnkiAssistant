using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 临时探针（不入库）：只把「Anki 连接」弹窗画出来存成 PNG，用来验证弹窗的布局与绘制异常。
    /// 编译（在 src 里额外带上本文件，并用 /main 指定入口，这样 src\Program.cs 还能照常编译）：
    ///   csc /nologo /codepage:65001 /target:exe /main:AnkiAssistant.ConnShot /out:%TEMP%\conn_shot.exe ^
    ///       /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    ///       /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    ///       /r:System.Xml.Linq.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Security.dll ^
    ///       src\*.cs tools\conn_shot.cs
    /// 用法：conn_shot.exe <输出png>
    /// </summary>
    public static class ConnShot
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "conn.png";

            // 绘制里抛异常时别只留个红叉，把堆栈打出来
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Console.WriteLine("PAINT/UI EXCEPTION: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Console.WriteLine("UNHANDLED: " + e.ExceptionObject);
            };

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Ui.S = g.DpiX / 96f; }
            catch { Ui.S = 1f; }
            Theme.Apply(Theme.Get(Store.ThemeId));

            ConnDialog d = null;
            string err = null;
            try { d = new ConnDialog(); }
            catch (Exception ex) { err = "构造失败: " + ex; }

            if (d != null)
            {
                d.StartPosition = FormStartPosition.Manual;
                d.Location = new Point(-4000, -4000);
                d.Show();
                // 等它把后台探活跑完（Shown 里会 Recheck）
                for (int i = 0; i < 70; i++) { Application.DoEvents(); Thread.Sleep(50); }
                try
                {
                    using (var bmp = new Bitmap(d.Width, d.Height))
                    {
                        d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
                        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Console.WriteLine("CONN SHOT -> " + outPath + " (" + d.Width + "x" + d.Height + ")");
                }
                catch (Exception ex) { err = "截图失败: " + ex; }
                try { d.Dispose(); } catch { }
            }

            if (err != null) Console.WriteLine("ERROR: " + err);
            Console.Out.Flush();
            Environment.Exit(err == null ? 0 : 1);
        }
    }
}
