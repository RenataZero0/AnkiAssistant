using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace AnkiAssistant
{
    /// <summary>
    /// 程序图标：优先用编进 exe 的那份（build.ps1 的 /resource:），
    /// 取不到就退回 exe 自己的图标，再不行就现画一个。
    /// </summary>
    public static class AppIcon
    {
        static readonly System.Collections.Generic.Dictionary<int, Icon> _cache =
            new System.Collections.Generic.Dictionary<int, Icon>();

        public static Icon Get(int size)
        {
            Icon ic;
            if (_cache.TryGetValue(size, out ic)) return ic;

            // ① 内嵌资源
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("AnkiAssistant.app.ico"))
                {
                    if (s != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            ms.Position = 0;
                            ic = new Icon(ms, new Size(size, size));
                        }
                    }
                }
            }
            catch { ic = null; }

            // ② exe 关联图标
            if (ic == null)
            {
                try { ic = Icon.ExtractAssociatedIcon(Application2.ExePath); } catch { }
            }

            // ③ 自绘兜底
            if (ic == null) ic = DrawFallback(size);

            _cache[size] = ic;
            return ic;
        }

        public static Icon Get() { return Get(32); }

        /// <summary>画一个圆角方形的 A 字图标。</summary>
        static Icon DrawFallback(int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var r = new Rectangle(0, 0, size, size);
                    using (GraphicsPath p = Ui.Round(r, Math.Max(2, size / 4)))
                    using (var br = new SolidBrush(Color.FromArgb(0x35, 0x68, 0xE8)))
                        g.FillPath(br, p);
                    using (var f = new Font("Segoe UI", size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var br = new SolidBrush(Color.White))
                    {
                        SizeF sz = g.MeasureString("A", f);
                        g.DrawString("A", f, br, (size - sz.Width) / 2, (size - sz.Height) / 2);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }

    /// <summary>拿 exe 路径的小助手（Application 在 WinForms 里，单独抽出来避免命名冲突）。</summary>
    public static class Application2
    {
        public static string ExePath
        {
            get
            {
                try { return System.Windows.Forms.Application.ExecutablePath; }
                catch { return System.Reflection.Assembly.GetEntryAssembly().Location; }
            }
        }
    }
}
