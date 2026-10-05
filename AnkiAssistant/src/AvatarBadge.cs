using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 顶栏右上角的头像角标：圆角方形（和 Android 版 v1.15 之后一致）。
    ///
    /// 图片来源：%APPDATA%\AnkiAssistant\data\avatar.png（在设置里选的），
    /// 没有就画一个字母。右下角的小圆点是 Anki 连接状态：
    /// 绿=连上了，灰=没检查，红=连不上。
    /// </summary>
    public class AvatarBadge : Control
    {
        public AvatarBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public static string AvatarPath
        {
            get { return Path.Combine(Store.DataDir, "avatar.png"); }
        }

        /// <summary>用户在设置里选的图（可空）。</summary>
        public static Image LoadImage()
        {
            try
            {
                string p = AvatarPath;
                if (!File.Exists(p)) return null;
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                using (var tmp = Image.FromStream(fs))
                    return new Bitmap(tmp);
            }
            catch { return null; }
        }

        /// <summary>把选中的图缩放成 256x256 存下来（顺手压掉体积）。</summary>
        public static void SaveImage(Image src)
        {
            try
            {
                const int size = 256;
                using (var bmp = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        // 居中裁成正方形再缩放，避免被拉变形
                        int side = Math.Min(src.Width, src.Height);
                        var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                        g.DrawImage(src, new Rectangle(0, 0, size, size), crop, GraphicsUnit.Pixel);
                    }
                    bmp.Save(AvatarPath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        public static void ClearImage()
        {
            try { if (File.Exists(AvatarPath)) File.Delete(AvatarPath); } catch { }
        }

        public void Refresh2() { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Parent != null)
            {
                using (var b = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(b, ClientRectangle);
            }

            var box = new Rectangle(0, 0, Width, Height);
            int radius = (int)Math.Round(Math.Min(Width, Height) * 0.30);   // 圆角方形
            Image img = LoadImage();

            using (GraphicsPath clip = Ui.Round(box, radius))
            {
                GraphicsState st = g.Save();
                g.SetClip(clip);
                if (img != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, box);
                }
                else
                {
                    using (var br = new SolidBrush(Ui.ACCENT)) g.FillRectangle(br, box);
                    string letter = Store.Get("anki.profile", "A");
                    if (letter.Length == 0) letter = "A";
                    using (var f = Ui.F(Width * 0.30f, true))
                        Ui.TextC(g, letter.Substring(0, 1).ToUpperInvariant(), f, Ui.WHITE,
                            Width / 2, Height / 2);
                }
                g.Restore(st);
            }

            // 状态点
            int d = Math.Max(6, Width / 4);
            var dot = new Rectangle(Width - d - 1, Height - d - 1, d, d);
            Color c = SyncState.Kind == SyncKind.Ok ? Ui.GREEN
                    : SyncState.Kind == SyncKind.Error ? Ui.RED : Ui.TEXT_DIM;
            using (var b = new SolidBrush(Ui.CARD)) g.FillEllipse(b, dot);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, dot);
        }

        protected override void OnMouseEnter(EventArgs e) { BackColor = Theme.Alpha(Ui.ACCENT_SOFT, 120); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { BackColor = Color.Transparent; Invalidate(); base.OnMouseLeave(e); }
    }
}
