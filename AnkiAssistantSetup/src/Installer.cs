using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AnkiAssistant.Setup
{
    /// <summary>
    /// 安装 / 卸载的实际动作（和界面分开，方便静默安装与测试）。
    ///
    /// 采用**每用户安装**：装到 %LOCALAPPDATA%\Programs\，注册表写 HKCU，
    /// 全程不需要管理员权限，也不会弹 UAC。
    /// </summary>
    public static class Installer
    {
        public const string AppId = "AnkiAssistant";
        public const string AppName = "Anki 助手 AnkiAssistant";
        public const string Publisher = "RenataZero0";
        public const string ExeName = "AnkiAssistant.exe";
        public const string ShortcutName = "Anki 助手";
        public const string UninstallName = "卸载 Anki 助手.exe";

        public static string DefaultDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\AnkiAssistant");
            }
        }

        public static string UserDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnkiAssistant");
            }
        }

        static string UninstallKey
        {
            get { return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId; }
        }

        public static bool AlreadyInstalled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                        if (k != null) return true;
                }
                catch { }
                return false;
            }
        }

        public static string InstalledDir
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                        if (k != null) return k.GetValue("InstallLocation", "") as string;
                }
                catch { }
                return null;
            }
        }

        // ================================================================ 安装
        public class Progress
        {
            public Action<int, string> Report = delegate { };
        }

        public static void Install(string dir, bool desktopShortcut, bool startWithWindows, Progress p)
        {
            if (string.IsNullOrEmpty(dir)) dir = DefaultDir;
            Directory.CreateDirectory(dir);

            // --- 1. 主程序 ---
            p.Report(10, "正在写入主程序…");
            string exe = Path.Combine(dir, ExeName);
            // 先杀掉可能在运行的旧版本，否则文件被占用写不进去
            KillRunning(exe);
            Res.Extract("App.AnkiAssistant.exe", exe, true);

            // --- 2. 安装包自己复制成卸载程序 ---
            p.Report(40, "正在写入卸载程序…");
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string un = Path.Combine(dir, UninstallName);
            if (!string.Equals(self, un, StringComparison.OrdinalIgnoreCase)) File.Copy(self, un, true);

            // --- 3. 更新日志（和 exe 放一起，作为离线兜底）---
            // 运行时会优先用从 GitHub 拉下来的那份，放在 %APPDATA%\AnkiAssistant 里。
            // 本应用的数据文件（配置、卡片缓存）由程序自己管理，安装包不预置任何数据。
            p.Report(55, "正在写入更新日志…");
            Res.Extract("App.CHANGELOG.md", Path.Combine(dir, "CHANGELOG.md"), true);

            // 本应用把设置存在 exe 同目录的 settings.ini 与 %APPDATA%\AnkiAssistant 里，
            // 所以这里只把用户目录建出来，不往里写任何东西（绝不覆盖已有配置）。
            try { Directory.CreateDirectory(UserDataDir); } catch { }

            // --- 4. 快捷方式 ---
            p.Report(75, "正在创建快捷方式…");
            string startMenu = Path.Combine(Shortcuts.StartMenuDir, ShortcutName + ".lnk");
            Shortcuts.Create(startMenu, exe, dir, "", exe, AppName);

            string desk = Path.Combine(Shortcuts.DesktopDir, ShortcutName + ".lnk");
            if (desktopShortcut) Shortcuts.Create(desk, exe, dir, "", exe, AppName);
            else Shortcuts.Delete(desk);

            // --- 5. 开机自启 ---
            if (startWithWindows) SetAutoStart(exe, true);

            // --- 6. 注册表（出现在「应用和功能」里）---
            p.Report(90, "正在注册到系统…");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", SetupInfo.Version);
                k.SetValue("Publisher", Publisher);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + un + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + un + "\" --uninstall --silent");
                k.SetValue("NoModify", 1);
                k.SetValue("NoRepair", 1);
                k.SetValue("EstimatedSize", EstimateKb(dir), RegistryValueKind.DWord);
            }

            p.Report(100, "安装完成");
        }

        static int EstimateKb(string dir)
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    total += new FileInfo(f).Length;
                return (int)Math.Max(1, total / 1024);
            }
            catch { return 1; }
        }

        // ================================================================ 卸载
        public static void Uninstall(bool removeUserData, Progress p)
        {
            string dir = InstalledDir;
            if (string.IsNullOrEmpty(dir)) dir = DefaultDir;

            p.Report(10, "正在关闭程序…");
            KillRunning(Path.Combine(dir, ExeName));

            p.Report(30, "正在删除快捷方式…");
            Shortcuts.Delete(Path.Combine(Shortcuts.StartMenuDir, ShortcutName + ".lnk"));
            Shortcuts.Delete(Path.Combine(Shortcuts.DesktopDir, ShortcutName + ".lnk"));
            SetAutoStart(null, false);

            p.Report(50, "正在清理注册表…");
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            p.Report(70, "正在删除程序文件…");
            // 卸载程序自己在占着，删不掉，用批处理延迟删
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (!string.Equals(f, self, StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(f); } catch { }
                foreach (var d in Directory.GetDirectories(dir))
                    try { Directory.Delete(d, true); } catch { }
            }
            catch { }

            if (removeUserData)
            {
                p.Report(85, "正在删除配置与缓存…");
                try { if (Directory.Exists(UserDataDir)) Directory.Delete(UserDataDir, true); } catch { }
            }

            p.Report(95, "正在收尾…");
            try
            {
                string bat = Path.Combine(Path.GetTempPath(), "aa-uninstall.bat");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("ping 127.0.0.1 -n 3 > nul");
                sb.AppendLine(":retry");
                sb.AppendLine("del \"" + self + "\" > nul 2>&1");
                sb.AppendLine("if exist \"" + self + "\" ( ping 127.0.0.1 -n 2 > nul & goto retry )");
                sb.AppendLine("rmdir \"" + dir + "\" > nul 2>&1");
                sb.AppendLine("del \"%~f0\" > nul 2>&1");
                // 必须用系统 ANSI 写：cmd.exe 按 ANSI 解码，UTF-8 会让中文路径变乱码、自删失败
                File.WriteAllText(bat, sb.ToString(), Encoding.Default);
                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
            p.Report(100, "卸载完成");
        }

        // ================================================================ 杂项
        public static void KillRunning(string exePath)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(exePath);
                foreach (var pr in Process.GetProcessesByName(name))
                    try { pr.Kill(); pr.WaitForExit(3000); } catch { }
            }
            catch { }
        }

        public static void SetAutoStart(string exePath, bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    // 应用支持 --tray：开机时只起托盘/后台，不弹主窗口
                    if (on && !string.IsNullOrEmpty(exePath)) k.SetValue(AppId, "\"" + exePath + "\" --tray");
                    else k.DeleteValue(AppId, false);
                }
            }
            catch { }
        }

        public static bool AutoStartOn
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run"))
                        return k != null && k.GetValue(AppId) != null;
                }
                catch { return false; }
            }
        }
    }
}
