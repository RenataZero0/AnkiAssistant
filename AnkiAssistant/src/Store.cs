using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnkiAssistant
{
    /// <summary>
    /// 设置与本地数据的存放约定（和 Android 版一个思路，只是换成 Windows 的目录）。
    ///
    /// %APPDATA%\AnkiAssistant\            设置、令牌、日志缓存
    /// %APPDATA%\AnkiAssistant\data\       卡片配置、草稿、头像
    ///
    /// 之所以不放在 exe 旁边：安装版装在 %LOCALAPPDATA%\Programs 下，
    /// 但用户以后也可能手动挪 exe，写自己的目录迟早出问题。
    /// 早期版本确实把 data 放在 exe 旁边，所以启动时跑一次 MigrateOnce（只补缺、不覆盖）。
    /// </summary>
    public static class Store
    {
        public const string AppName = "AnkiAssistant";

        public static string Root
        {
            get
            {
                string p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                Directory.CreateDirectory(p);
                Migrate();
                return p;
            }
        }

        public static string DataDir
        {
            get
            {
                string p = Path.Combine(Root, "data");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string SettingsPath { get { return Path.Combine(Root, "settings.ini"); } }
        public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        // ===== 老版本迁移 =====
        static bool _migrated;

        /// <summary>把 exe 旁边 data\ 里的文件搬到 %APPDATA%（只补缺，绝不覆盖）。</summary>
        static void Migrate()
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                string oldRoot = Path.Combine(ExeDir, "data");
                string newRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "data");
                if (!Directory.Exists(oldRoot)) return;
                if (string.Equals(Path.GetFullPath(oldRoot).TrimEnd('\\'),
                                  Path.GetFullPath(newRoot).TrimEnd('\\'),
                                  StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(newRoot);
                foreach (string f in Directory.GetFiles(oldRoot))
                {
                    string target = Path.Combine(newRoot, Path.GetFileName(f));
                    if (!File.Exists(target)) { try { File.Copy(f, target); } catch { } }
                }
            }
            catch { }
        }

        // ===== 设置（key=value 纯文本，UTF-8 无 BOM）=====
        static List<string> _lines;
        static List<string> Lines
        {
            get
            {
                if (_lines != null) return _lines;
                _lines = new List<string>();
                try
                {
                    if (File.Exists(SettingsPath))
                        _lines.AddRange(File.ReadAllLines(SettingsPath, Encoding.UTF8));
                }
                catch { }
                return _lines;
            }
        }

        public static string Get(string key, string def = "")
        {
            string prefix = key + "=";
            foreach (string line in Lines)
            {
                if (line == null) continue;
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length);
            }
            return def;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key), out v) ? v : def;
        }

        public static bool GetBool(string key, bool def)
        {
            string v = Get(key);
            if (v.Length == 0) return def;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void Set(string key, string val)
        {
            val = val ?? "";
            List<string> ls = Lines;
            string prefix = key + "=";
            for (int i = 0; i < ls.Count; i++)
            {
                if (ls[i] != null && ls[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    ls[i] = prefix + val;
                    Save();
                    return;
                }
            }
            ls.Add(prefix + val);
            Save();
        }

        public static void SetBool(string key, bool v) { Set(key, v ? "1" : "0"); }

        static void Save()
        {
            try
            {
                File.WriteAllText(SettingsPath, string.Join("\r\n", Lines.ToArray()) + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        // ===== 常用设置项的强类型包装 =====
        /// <summary>皮肤 id（blue / forest / sunset / violet / dark）。</summary>
        public static string ThemeId
        {
            get { return Get("theme", "blue"); }
            set { Set("theme", value); }
        }

        /// <summary>当前使用的输出格式（卡片配置）id。</summary>
        public static string ActiveConfigId
        {
            get { return Get("config.active", ""); }
            set { Set("config.active", value); }
        }

        /// <summary>卡片配置的 JSON 全文（由 CardConfig 负责读写）。</summary>
        public static string ConfigsJson
        {
            get { return Get("configs.json", ""); }
            set { Set("configs.json", value); }
        }

        public static bool SyncAfterSave
        {
            get { return GetBool("sync.afterSave", false); }
            set { SetBool("sync.afterSave", value); }
        }

        public static bool SyncWifiOnly
        {
            get { return GetBool("sync.wifiOnly", true); }
            set { SetBool("sync.wifiOnly", value); }
        }

        public static bool SyncMedia
        {
            get { return GetBool("sync.media", false); }
            set { SetBool("sync.media", value); }
        }

        public static bool ClearAfterSave
        {
            get { return GetBool("habit.clearAfterSave", true); }
            set { SetBool("habit.clearAfterSave", value); }
        }

        public static bool PreviewAfterFill
        {
            get { return GetBool("habit.previewAfterFill", false); }
            set { SetBool("habit.previewAfterFill", value); }
        }

        public static bool CloseToTray
        {
            get { return GetBool("tray.closeToTray", true); }
            set { SetBool("tray.closeToTray", value); }
        }

        // ===== 开机自启（HKCU\...\Run）=====
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool AutoStart
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(AppName) != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                    {
                        if (k == null) return;
                        if (value)
                        {
                            string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                            k.SetValue(AppName, "\"" + exe + "\" --tray");
                        }
                        else k.DeleteValue(AppName, false);
                    }
                }
                catch { }
            }
        }

        // ===== 小工具 =====
        public static void WriteText(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string ReadText(string path, string def = "")
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : def; }
            catch { return def; }
        }
    }
}
