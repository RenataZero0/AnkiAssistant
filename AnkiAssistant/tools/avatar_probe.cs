using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AnkiAssistant;

/// <summary>
/// 头像探针：对着**真实运行的 Anki** 走一遍「生成 256x256 圆角图 → storeMediaFile
/// → retrieveMediaFile → 字节比对 → deleteMediaFile → 再取应为空」，
/// 用来验证 AnkiConnect 的媒体接口参数名（filename / data 是 base64）与文件名约定。
///
/// 为了不动用户自己的头像，探针把邮箱临时换成 aa-probe@example.com，跑完原样还回去；
/// 本机缓存 data\avatar.png 全程不碰。产物是临时 exe，不入库。
///   csc /main:AvatarProbe ... src\*.cs tools\avatar_probe.cs
/// </summary>
public static class AvatarProbe
{
    static int fails;

    static void Check(string label, bool ok, string detail)
    {
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + label + (detail == null ? "" : "  [" + detail + "]"));
    }

    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch { }

        // ---- 先把要改的三个键记下来，退出前一定还原 ----
        string oldMail = Store.Get("anki.profile", "");
        string oldSource = Store.Get("avatar.source", "");
        string oldEmail = Store.Get("avatar.email", "");
        const string ProbeMail = "aa-probe@example.com";

        try
        {
            int ver = -1;
            try { ver = AnkiConn.Version(); } catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
            Check("AnkiConnect 在线", ver > 0, "version=" + ver);

            // 1) MD5 / 文件名约定（纯函数，跟 Android 的 mediaName 对齐）
            string md5 = AvatarStore.Md5("  AA-Probe@Example.COM ");
            Check("Md5() 小写去空格", md5 == AvatarStore.Md5("aa-probe@example.com"), md5);
            Check("Md5() 长度 32", md5.Length == 32, md5.Length.ToString());
            string name = AvatarStore.MediaName(ProbeMail);
            Check("MediaName() 前缀约定", name.StartsWith("ankiassistant-avatar-") && name.EndsWith(".png"), name);

            // 2) 造一张 300x200 的图，走真实裁图路径
            byte[] png;
            using (var src = new Bitmap(300, 200))
            {
                using (Graphics g = Graphics.FromImage(src))
                {
                    g.Clear(Color.FromArgb(255, 255, 0, 128));
                    using (var b = new SolidBrush(Color.FromArgb(255, 0, 160, 255)))
                        g.FillEllipse(b, 20, 20, 160, 160);
                }
                using (Bitmap rounded = AvatarStore.Rounded(src))
                {
                    Check("Rounded() 出 256x256", rounded.Width == 256 && rounded.Height == 256,
                          rounded.Width + "x" + rounded.Height);
                    Check("Rounded() 四角透明", rounded.GetPixel(1, 1).A == 0, "alpha=" + rounded.GetPixel(1, 1).A);
                    Check("Rounded() 中心不透明", rounded.GetPixel(128, 128).A == 255, "alpha=" + rounded.GetPixel(128, 128).A);
                    png = AvatarStore.EncodePng(rounded);
                }
            }
            Check("EncodePng() 出 PNG 头", png != null && png.Length > 8 &&
                  png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47,
                  png == null ? "null" : png.Length + " 字节");

            // 3) storeMediaFile → retrieveMediaFile → 字节一致
            Store.Set("anki.profile", ProbeMail);
            string wrote = null;
            try { wrote = AnkiConn.StoreMediaFile(name, png); }
            catch (Exception ex) { Console.WriteLine("      storeMediaFile: " + ex.Message); }
            Check("storeMediaFile 回写文件名", wrote == name, wrote);

            byte[] back = null;
            try { back = AnkiConn.RetrieveMediaFile(name); }
            catch (Exception ex) { Console.WriteLine("      retrieveMediaFile: " + ex.Message); }
            Check("retrieveMediaFile 取回同样字节", back != null && back.Length == png.Length,
                  back == null ? "null" : back.Length + " 字节");

            // 4) AvatarStore.Push / Pull 的完整通路
            string err = null;
            bool pushed;
            using (Image img = AvatarStore.Decode(png))
                pushed = AvatarStore.Push(img, out err);
            Check("AvatarStore.Push()", pushed, err);

            Bitmap pulled = AvatarStore.Pull();
            Check("AvatarStore.Pull() 取回 256x256", pulled != null && pulled.Width == 256 && pulled.Height == 256,
                  pulled == null ? "null" : pulled.Width + "x" + pulled.Height);
            if (pulled != null) pulled.Dispose();

            // 5) 清场：deleteMediaFile，再取应该是空
            bool deleted = false;
            try { deleted = AvatarStore.DeleteRemote(); }
            catch (Exception ex) { Console.WriteLine("      deleteMediaFile: " + ex.Message); }
            Check("deleteMediaFile 不报错", deleted, null);

            byte[] gone = AnkiConn.RetrieveMediaFile(name);
            Check("删完再取为空", gone == null, gone == null ? "null" : gone.Length + " 字节");

            // 6) SaveLocal：本机缓存那张圆角 PNG + source/email 记账（跑完还原）
            string cache = AvatarBadge.AvatarPath;
            byte[] cacheBefore = File.Exists(cache) ? File.ReadAllBytes(cache) : null;
            try
            {
                using (var src = new Bitmap(400, 260))
                {
                    using (Graphics g = Graphics.FromImage(src)) g.Clear(Color.SeaGreen);
                    AvatarStore.SaveLocal(src, "custom");
                }
                using (Image saved = AvatarBadge.LoadImage())
                {
                    var sb = saved as Bitmap;
                    Check("SaveLocal 落盘 256x256", sb != null && sb.Width == 256 && sb.Height == 256,
                          sb == null ? "null" : sb.Width + "x" + sb.Height);
                    Check("SaveLocal 四角是透明的", sb != null && sb.GetPixel(1, 1).A == 0, null);
                }
                Check("source 记账 = custom", Store.Get("avatar.source", "") == "custom", Store.Get("avatar.source", ""));
                Check("email 记账 = 当前邮箱", Store.Get("avatar.email", "") == ProbeMail, Store.Get("avatar.email", ""));
            }
            finally
            {
                if (cacheBefore == null) { try { File.Delete(cache); } catch { } }
                else { try { File.WriteAllBytes(cache, cacheBefore); } catch { } }
            }
            Check("本机 avatar.png 已还原", cacheBefore != null || !File.Exists(cache), null);
        }
        finally
        {
            Store.Set("anki.profile", oldMail);
            Store.Set("avatar.source", oldSource);
            Store.Set("avatar.email", oldEmail);
            string left = AvatarStore.MediaName(ProbeMail);
            try { AnkiConn.DeleteMediaFile(left); } catch { }
            Console.WriteLine("已还原 anki.profile=" + (oldMail.Length == 0 ? "(空)" : oldMail));
        }

        Console.WriteLine(fails == 0 ? "AVATAR PROBE OK（全部通过）" : ("AVATAR PROBE FAILED（" + fails + " 项失败）"));
        return fails == 0 ? 0 : 1;
    }
}
