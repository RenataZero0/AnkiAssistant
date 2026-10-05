using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AnkiAssistant;

// 头像链路探针：本机存图 -> 推进收藏库媒体库 -> 读回 -> 删除，再走一遍弹窗的 UploadLocal。
// 只用来复现「进程容易崩溃」里头像那条路径；所有异常都打全堆栈。
public static class AvaProbe2
{
    static int fails = 0;

    static void Step(string name, Action a)
    {
        try { a(); Console.WriteLine("[ok]   " + name); }
        catch (Exception ex)
        {
            fails++;
            Console.WriteLine("[FAIL] " + name + " -> " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
        {
            fails++;
            Console.WriteLine("[THREAD-EXCEPTION] " + e.Exception.GetType().FullName + ": " + e.Exception.Message);
            Console.WriteLine(e.Exception.StackTrace);
        };

        string mail = "probe-not-real@example.invalid";
        string old = Store.Get("anki.profile", "");
        Console.WriteLine("engine available = " + Engine.Available + "  opened = " + Engine.Opened);

        Step("引擎开库", delegate { AnkiConn.EnsureOpen(); });
        Console.WriteLine("opened = " + AnkiConn.Opened + "  dir = " + AnkiConn.AnkiDir());

        Step("写邮箱", delegate { Store.Set("anki.profile", mail); });

        using (var img = new Bitmap(64, 64))
        using (Graphics g = Graphics.FromImage(img))
        {
            g.Clear(Color.CornflowerBlue);
            Step("AvatarBadge.SaveImage", delegate { AvatarBadge.SaveImage(img); });
        }

        Step("AvatarBadge.LoadImage", delegate
        {
            using (Image got = AvatarBadge.LoadImage())
            {
                Console.WriteLine("       loaded = " + (got == null ? "null" : got.Width + "x" + got.Height));
            }
        });

        Step("AvatarStore.MediaName", delegate { Console.WriteLine("       name = " + AvatarStore.MediaName()); });
        Step("AvatarStore.Push", delegate
        {
            string err = null;
            using (Image img = AvatarBadge.LoadImage())
            {
                bool ok = AvatarStore.Push(img, out err);
                Console.WriteLine("       push = " + ok + " err = " + (err == null ? "-" : err));
            }
        });
        Step("AvatarStore.Pull", delegate
        {
            using (Bitmap b = AvatarStore.Pull()) { Console.WriteLine("       pull = " + (b == null ? "null" : b.Width + "x" + b.Height)); }
        });
        Step("AvatarStore.Resolve", delegate { AvatarStore.Resolve(); });
        Step("AvatarStore.DeleteRemote", delegate { Console.WriteLine("       del = " + AvatarStore.DeleteRemote()); });

        // 弹窗：真的调一次 UploadLocal（内部走 Dlg.Wait + AvatarStore.Push）
        Form dlg = null;
        Step("AvatarDialog.BuildForm", delegate { dlg = AvatarDialog.BuildForm(); });
        if (dlg != null)
        {
            dlg.Show();
            Application.DoEvents();
            Step("AvatarDialog.UploadLocal", delegate
            {
                MethodInfo mi = dlg.GetType().GetMethod("UploadLocal", BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi == null) throw new Exception("找不到 UploadLocal");
                mi.Invoke(dlg, null);
            });
            Application.DoEvents();
            Step("AvatarDialog 关掉", delegate { dlg.Close(); dlg.Dispose(); });
        }

        Step("恢复邮箱", delegate { Store.Set("anki.profile", old); });
        Step("关库", delegate { AnkiConn.Close(); });

        Console.WriteLine(fails == 0 ? "AVA PROBE OK" : "AVA PROBE FAILED (" + fails + ")");
        return fails == 0 ? 0 : 1;
    }
}
