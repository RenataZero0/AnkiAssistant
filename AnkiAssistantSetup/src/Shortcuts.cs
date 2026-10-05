using System;
using System.IO;
using System.Reflection;

namespace AnkiAssistant.Setup
{
    /// <summary>
    /// 创建 Windows 快捷方式（.lnk）。
    ///
    /// 用 WScript.Shell 的 COM 接口 + 反射调用，比手写 IShellLink 互操作代码短得多，
    /// 而且 WScript.Shell 在任何 Windows 上都有，不需要额外引用。
    /// </summary>
    public static class Shortcuts
    {
        public static bool Create(string lnkPath, string target, string workingDir,
                                  string args, string iconPath, string description)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                object shell = Activator.CreateInstance(shellType);

                object lnk = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });

                Type lt = lnk.GetType();
                Action<string, object> set = delegate (string name, object val)
                {
                    lt.InvokeMember(name, BindingFlags.SetProperty, null, lnk, new object[] { val });
                };

                set("TargetPath", target);
                if (!string.IsNullOrEmpty(workingDir)) set("WorkingDirectory", workingDir);
                if (!string.IsNullOrEmpty(args)) set("Arguments", args);
                if (!string.IsNullOrEmpty(iconPath)) set("IconLocation", iconPath);
                if (!string.IsNullOrEmpty(description)) set("Description", description);
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                return true;
            }
            catch { return false; }
        }

        public static string StartMenuDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs");
            }
        }

        public static string DesktopDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        }

        public static void Delete(string lnkPath)
        {
            try { if (File.Exists(lnkPath)) File.Delete(lnkPath); } catch { }
        }
    }

    /// <summary>
    /// 内嵌资源的读取（安装包把主程序、图标、更新日志打进了自己身体里）。
    ///
    /// 资源名和 build-setup.ps1 里 /resource: 的逗号后半段**逐字对应**：
    /// App.AnkiAssistant.exe / App.app.ico / App.CHANGELOG.md
    /// （应用自己体内的 AnkiAssistant.app.ico 是应用侧的资源名，跟这里无关。）
    /// </summary>
    public static class Res
    {
        public static byte[] Read(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var buf = new byte[s.Length];
                int off = 0;
                while (off < buf.Length)
                {
                    int n = s.Read(buf, off, buf.Length - off);
                    if (n <= 0) break;
                    off += n;
                }
                return buf;
            }
        }

        public static void Extract(string name, string target, bool overwrite)
        {
            if (!overwrite && File.Exists(target)) return;
            byte[] b = Read(name);
            if (b == null) throw new FileNotFoundException("安装包缺少资源：" + name);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, b);
        }

        public static long SizeOf(string name)
        {
            byte[] b = Read(name);
            return b == null ? 0 : b.Length;
        }
    }
}
