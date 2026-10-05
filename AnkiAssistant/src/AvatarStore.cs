using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 头像的取图 / 上传。约定跟 Android 版一模一样，两个平台看的是同一张图：
    ///   媒体文件名 = ankiassistant-avatar-&lt;md5(小写、去首尾空格的邮箱)&gt;.png
    ///
    /// 取图顺序：本机缓存 → Anki 收藏库媒体 → Gravatar → 字母（AvatarBadge 自己画）。
    /// Gravatar 那张不上传：手机上用户自己选的图存在媒体库里，上传会把人家盖掉。
    /// 只有用户在本机主动选的那张才写进媒体库，跟着 AnkiWeb 同步到别的设备。
    /// </summary>
    public static class AvatarStore
    {
        /// <summary>媒体库里那张固定 256x256，跟 Android 版一致。</summary>
        public const int Size = 256;

        const string MediaPrefix = "ankiassistant-avatar-";
        const string GravatarBase = "https://www.gravatar.com/avatar/";

        /// <summary>本机缓存是哪来的：custom / gravatar / ""。</summary>
        const string KeySource = "avatar.source";
        /// <summary>本机缓存是哪封邮箱解析出来的；邮箱换了缓存就作废。</summary>
        const string KeyEmail = "avatar.email";

        static readonly object Gate = new object();

        static AvatarStore()
        {
            // 跟 GitHub.cs 一致：.NET 4.0 默认只开 SSL3/TLS1，走 https 必须显式抬到 TLS1.2
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
        }

        // ================================================================ 邮箱 / 文件名

        /// <summary>AnkiWeb / Gravatar 用的邮箱（就存在 anki.profile 这个键里）。</summary>
        public static string Email
        {
            get { return Store.Get("anki.profile", "").Trim(); }
        }

        /// <summary>小写、去首尾空格之后取 md5 十六进制（Gravatar 的公开算法）。</summary>
        public static string Md5(string s)
        {
            byte[] raw = Encoding.UTF8.GetBytes(s == null ? "" : s.Trim().ToLowerInvariant());
            using (MD5 md5 = MD5.Create())
            {
                byte[] sum = md5.ComputeHash(raw);
                var sb = new StringBuilder(sum.Length * 2);
                for (int i = 0; i < sum.Length; i++) sb.Append(sum[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>收藏库媒体库里的文件名。邮箱空着就取不了（返回 ""）。</summary>
        public static string MediaName(string email)
        {
            string m = email == null ? "" : email.Trim();
            if (m.Length == 0) return "";
            return MediaPrefix + Md5(m) + ".png";
        }

        public static string MediaName() { return MediaName(Email); }

        // ================================================================ 裁图 / 编解码

        /// <summary>居中裁成正方形、缩到 256x256，四角按 30% 切圆（跟角标画法一致）。</summary>
        public static Bitmap Rounded(Image src)
        {
            var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            if (src == null) return bmp;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int side = Math.Min(src.Width, src.Height);
                if (side < 1) side = 1;
                var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                int radius = (int)Math.Round(Size * 0.30);
                using (GraphicsPath clip = Ui.Round(new Rectangle(0, 0, Size, Size), radius))
                {
                    g.SetClip(clip);
                    g.DrawImage(src, new Rectangle(0, 0, Size, Size), crop, GraphicsUnit.Pixel);
                }
            }
            return bmp;
        }

        public static byte[] EncodePng(Image img)
        {
            if (img == null) return null;
            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        public static Bitmap Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (Image tmp = Image.FromStream(ms))
                    return new Bitmap(tmp);
            }
            catch { return null; }
        }

        // ================================================================ 本机缓存

        /// <summary>本机缓存来源：custom / gravatar / ""。</summary>
        public static string Source { get { return Store.Get(KeySource, ""); } }

        /// <summary>把图存成 256x256 圆角 PNG 落到本机缓存，并记住它是哪来的。</summary>
        public static void SaveLocal(Image src, string source)
        {
            try
            {
                string path = AvatarBadge.AvatarPath;
                string tmp = path + ".tmp";
                using (Bitmap bmp = Rounded(src))
                {
                    bmp.Save(tmp, ImageFormat.Png);
                    // 先写临时文件再覆盖：正在重画的线程读不到"写了一半"的 PNG
                    File.Copy(tmp, path, true);
                    try { File.Delete(tmp); } catch { }
                }
                Store.Set(KeySource, source);
                Store.Set(KeyEmail, Email);
            }
            catch { }
        }

        // ================================================================ Gravatar

        /// <summary>按邮箱去 Gravatar 取图；没这个邮箱（404）、断网、超时都返回 null。</summary>
        public static byte[] FetchGravatar(string email)
        {
            if (MediaName(email).Length == 0) return null;
            try
            {
                var r = (HttpWebRequest)WebRequest.Create(GravatarBase + Md5(email) + "?s=" + Size + "&d=404");
                r.UserAgent = "AnkiAssistant-Windows";
                r.Timeout = 8000;
                r.ReadWriteTimeout = 8000;
                using (WebResponse resp = r.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        // ================================================================ 收藏库媒体

        /// <summary>把图写进 Anki 收藏库的媒体库（AnkiWeb 同步会一起带上）。</summary>
        public static bool Push(Image img, out string error)
        {
            error = null;
            string name = MediaName();
            if (name.Length == 0) { error = "没填邮箱，媒体库里没法给头像命名。"; return false; }
            byte[] png = EncodePng(Rounded(img));
            if (png == null) { error = "这张图用不了。"; return false; }
            try
            {
                string got = AnkiConn.StoreMediaFile(name, png);
                if (string.IsNullOrEmpty(got)) { error = "Anki 没确认写入。"; return false; }
                return true;
            }
            catch (Exception ex) { error = AnkiConn.Humanize(ex.Message); return false; }
        }

        /// <summary>从 Anki 媒体库取另一台设备传上去的那张；没有就 null。</summary>
        public static Bitmap Pull()
        {
            string name = MediaName();
            if (name.Length == 0) return null;
            try { return Decode(AnkiConn.RetrieveMediaFile(name)); }
            catch { return null; }
        }

        /// <summary>删掉媒体库里那张（换回 Gravatar 时用）。</summary>
        public static bool DeleteRemote()
        {
            string name = MediaName();
            if (name.Length == 0) return false;
            try { return AnkiConn.DeleteMediaFile(name); }
            catch { return false; }
        }

        // ================================================================ 解析

        /// <summary>
        /// 本机缓存 → Anki 媒体库 → Gravatar → 字母。
        /// 里面有 HTTP 和 AnkiConnect，只能在后台线程调（见 <see cref="ResolveAsync"/>）。
        /// </summary>
        public static void Resolve()
        {
            lock (Gate)
            {
                string email = Email;
                string src = Source;
                string srcEmail = Store.Get(KeyEmail, "");
                bool local = File.Exists(AvatarBadge.AvatarPath);

                // 1) 本机缓存。自己选的图（或来路不明的旧文件）永远优先，换邮箱也不覆盖用户的选择
                if (local && (src == "custom" || src.Length == 0))
                {
                    if (src.Length == 0) Store.Set(KeySource, "custom");
                    // 上次选图时 Anki 没连上，这次补传一次
                    if (email.Length > 0 && srcEmail != email) TryPushLocal();
                    return;
                }
                if (local && src == "gravatar" && srcEmail == email && email.Length > 0) return;

                // 2) 收藏库媒体库（别的设备传上去的）
                Bitmap pulled = Pull();
                if (pulled != null)
                {
                    try { SaveLocal(pulled, "custom"); }
                    finally { pulled.Dispose(); }
                    return;
                }

                // 3) Gravatar
                Bitmap grav = Decode(FetchGravatar(email));
                if (grav != null)
                {
                    try { SaveLocal(grav, "gravatar"); }
                    finally { grav.Dispose(); }
                    return;
                }

                // 4) 都没有：清掉过期缓存，角标退回字母
                if (local && src == "gravatar")
                {
                    AvatarBadge.ClearImage();
                    Store.Set(KeySource, "");
                    Store.Set(KeyEmail, "");
                }
            }
        }

        /// <summary>把本机那张自定义头像补传上去（上次可能没连上 Anki）。</summary>
        static void TryPushLocal()
        {
            try
            {
                using (Image img = AvatarBadge.LoadImage())
                {
                    if (img == null) return;
                    string err = null;
                    if (Push(img, out err)) Store.Set(KeyEmail, Email);
                }
            }
            catch { }
        }

        /// <summary>后台跑一遍 Resolve，跑完回 UI 线程叫 done（控件还没建句柄就算了）。</summary>
        public static void ResolveAsync(Control owner, MethodInvoker done)
        {
            var t = new Thread(delegate()
            {
                try { Resolve(); }
                catch { }
                if (done == null) return;
                try
                {
                    if (owner != null && owner.IsHandleCreated && !owner.IsDisposed)
                        owner.BeginInvoke(done);
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
