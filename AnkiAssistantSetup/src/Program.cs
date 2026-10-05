using System;
using System.IO;
using System.Windows.Forms;

namespace AnkiAssistant.Setup
{
    public static class SetupProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = Has(args, "--uninstall");
            bool silent = Has(args, "--silent") || Has(args, "/S") || Has(args, "/silent");

            // 离屏截图：把向导某一页渲染成图片（不弹窗、不安装），用来验证界面
            string shot = Value(args, "--shot");
            if (shot != null)
            {
                int page = 1;
                int.TryParse(Value(args, "--page") ?? "1", out page);
                shot = Path.GetFullPath(shot);
                string dir = Path.GetDirectoryName(shot);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using (var f = new WizardForm())
                {
                    f.PrepareForShot(page);
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new System.Drawing.Point(-6000, -6000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(300);
                    Application.DoEvents();
                    using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    f.Close();
                }
                Console.WriteLine("shot -> " + shot);
                return;
            }

            if (uninstall)
            {
                Environment.ExitCode = UninstallFlow.Run(silent);
                return;
            }

            if (silent)
            {
                // 静默安装：默认位置、建桌面快捷方式、不开机自启
                try
                {
                    var p = new Installer.Progress();
                    Installer.Install(Installer.DefaultDir, true, false, p);
                    Console.WriteLine("OK -> " + Installer.DefaultDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            using (var f = new WizardForm())
            {
                var r = f.ShowDialog();
                if (r == DialogResult.OK && f.LaunchAfter)
                {
                    try
                    {
                        string exe = Path.Combine(f.TargetDir ?? Installer.DefaultDir, Installer.ExeName);
                        if (File.Exists(exe))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                            { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
                    }
                    catch { }
                }
            }
        }

        static bool Has(string[] a, string flag)
        {
            foreach (var s in a)
                if (string.Equals(s, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>取 --xxx 后面那个参数</summary>
        static string Value(string[] a, string flag)
        {
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
    }
}
