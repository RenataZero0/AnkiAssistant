using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>
    /// 和 GitHub 打交道的部分：查最新版本、下载安装包、取更新日志。
    ///
    /// 这里只做「只读」的事（仓库是公开的，不需要登录），
    /// AnkiWeb 的登录同步是另一套，见 Sync.cs。
    /// </summary>
    public static class GitHub
    {
        // ===== 常量 =====
        public const string Owner = "RenataZero0";
        public const string Repo = "AnkiAssistant";

        /// <summary>版本号唯一真源：发版时只改这一处。</summary>
        public const string VersionTag = "v1.16.3";

        public const string SetupAsset = "AnkiAssistant-Setup.exe";
        public const string ExeAsset = "AnkiAssistant.exe";
        const string ApiBase = "https://api.github.com";
        const int Timeout = 20000;

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        // 命令行/老 .NET 默认只开 SSL3 + TLS1.0，连 GitHub 会报「未能创建 SSL/TLS 安全通道」。
        // 注意用 = 而不是 |=，把不安全的协议关掉。
        static GitHub() { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }

        /// <summary>
        /// 空方法，但必须显式调一次。
        /// const 会被编译期内联，直接读 VersionTag 不会触发静态构造函数，
        /// 那样第一个网络请求就可能在没开 TLS1.2 的情况下发出去。
        /// </summary>
        public static void Init() { }

        public static string Clean(string v)
        {
            if (v == null) return "";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            return v;
        }

        /// <summary>逐段数值比较，不能用字符串比较（2.1.10 会被判成小于 2.1.9）。</summary>
        public static int CompareVersion(string a, string b)
        {
            string[] pa = Clean(a).Split('.');
            string[] pb = Clean(b).Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = 0, vb = 0;
                if (i < pa.Length) int.TryParse(pa[i], out va);
                if (i < pb.Length) int.TryParse(pb[i], out vb);
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        // ===== 版本信息 =====
        public class Release
        {
            public string Tag = "";
            public string Name = "";
            public string Notes = "";
            public string PageUrl = "";
            public string SetupUrl = "", SetupBrowser = "";
            public long SetupSize;
            public string ExeUrl = "", ExeBrowser = "";
            public long ExeSize;
            public string ApkUrl = "", ApkBrowser = "";
            public long ApkSize;

            public bool Same(string v) { return CompareVersion(Tag, v) == 0; }
        }

        static HttpWebRequest Req(string url, string accept = null)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "AnkiAssistant-Windows";
            r.Timeout = Timeout;
            r.ReadWriteTimeout = Timeout;
            r.AllowAutoRedirect = true;
            r.Accept = accept ?? "application/vnd.github+json";
            return r;
        }

        static string Brief(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }

        static string ReadAll(WebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            using (var sr = new StreamReader(s, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string Get(string url)
        {
            try
            {
                using (WebResponse resp = Req(url).GetResponse()) return ReadAll(resp);
            }
            catch (WebException e)
            {
                if (e.Response is HttpWebResponse)
                {
                    string t = "";
                    try { t = ReadAll(e.Response); } catch { }
                    throw new Exception("HTTP " + (int)((HttpWebResponse)e.Response).StatusCode + "  " + Brief(t));
                }
                throw new Exception("网络错误：" + e.Message);
            }
        }

        /// <summary>取最新 Release；失败抛异常（调用方决定要不要烦用户）。</summary>
        public static Release LatestRelease()
        {
            string body = Get(ApiBase + "/repos/" + Owner + "/" + Repo + "/releases/latest");
            var map = Json.DeserializeObject(body) as Dictionary<string, object>;
            if (map == null) throw new Exception("服务器返回的内容看不懂。");

            var rel = new Release();
            rel.Tag = Str(map, "tag_name");
            rel.Name = Str(map, "name");
            rel.Notes = Str(map, "body");
            rel.PageUrl = Str(map, "html_url");

            object assetsObj;
            if (map.TryGetValue("assets", out assetsObj))
            {
                // 坑：JavaScriptSerializer 把嵌套数组反序列化成 ArrayList，不是 object[]。
                var arr = assetsObj as IEnumerable;
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        if (o is string) continue;
                        var a = o as Dictionary<string, object>;
                        if (a == null) continue;
                        string name = Str(a, "name");
                        string url = Str(a, "browser_download_url");
                        long size = Num(a, "size");
                        if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                        { rel.ApkUrl = Str(a, "url"); rel.ApkBrowser = url; rel.ApkSize = size; }
                        else if (name.Equals(SetupAsset, StringComparison.OrdinalIgnoreCase))
                        { rel.SetupUrl = Str(a, "url"); rel.SetupBrowser = url; rel.SetupSize = size; }
                        else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        { rel.ExeUrl = Str(a, "url"); rel.ExeBrowser = url; rel.ExeSize = size; }
                    }
                }
            }
            return rel;
        }

        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            return m.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        static long Num(Dictionary<string, object> m, string k)
        {
            object v;
            if (!m.TryGetValue(k, out v) || v == null) return 0;
            long n;
            return long.TryParse(Convert.ToString(v), out n) ? n : 0;
        }

        /// <summary>下载到 outPath，onProgress(已下载, 总大小)。</summary>
        public static void Download(string url, string outPath, Action<long, long> onProgress)
        {
            using (WebResponse resp = Req(url).GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                long total = resp.ContentLength;
                var buf = new byte[16384];
                long got = 0;
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n);
                    got += n;
                    if (onProgress != null) onProgress(got, total);
                }
            }
        }

        // ===== 更新日志 =====
        public static class Changelog
        {
            public const string Url =
                "https://raw.githubusercontent.com/RenataZero0/AnkiAssistant/main/CHANGELOG.md";

            /// <summary>① GitHub 抓下来的缓存。</summary>
            public static string CachePath { get { return Path.Combine(Store.Root, "CHANGELOG.md"); } }

            /// <summary>② exe 旁边那份（build.ps1 拷的离线兜底）。</summary>
            public static string BundledPath { get { return Path.Combine(Store.ExeDir, "CHANGELOG.md"); } }

            public static bool HasCache
            {
                get { try { return File.Exists(CachePath); } catch { return false; } }
            }

            public static DateTime? CacheTime
            {
                get
                {
                    try { return File.Exists(CachePath) ? File.GetLastWriteTime(CachePath) : (DateTime?)null; }
                    catch { return null; }
                }
            }

            /// <summary>先缓存后内置，都没有返回空串。</summary>
            public static string Local()
            {
                try
                {
                    if (File.Exists(CachePath))
                    {
                        string t = File.ReadAllText(CachePath, Encoding.UTF8);
                        if (t.Trim().Length > 0) return t;
                    }
                    if (File.Exists(BundledPath))
                    {
                        string t = File.ReadAllText(BundledPath, Encoding.UTF8);
                        if (t.Trim().Length > 0) return t;
                    }
                }
                catch { }
                return "";
            }

            /// <summary>抓最新日志写进缓存。永不抛异常（更新提示不该烦用户）。</summary>
            public static bool Fetch(out string error)
            {
                error = "";
                try
                {
                    var r = Req(Url, "text/plain");
                    r.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                        System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    r.Timeout = r.ReadWriteTimeout = 15000;
                    string text;
                    using (WebResponse resp = r.GetResponse()) text = ReadAll(resp);
                    if (text == null || text.Trim().Length < 50)
                    {
                        error = "服务器返回的内容是空的。";
                        return false;
                    }
                    Store.WriteText(CachePath, text);
                    return true;
                }
                catch (Exception e)
                {
                    error = Friendly(e);
                    return false;
                }
            }

            /// <summary>日志里最新的版本号（`## v1.2.3 —— 标题` 的 1.2.3）。</summary>
            public static string LatestVersionIn(string md)
            {
                if (string.IsNullOrEmpty(md)) return "";
                Match m = Regex.Match(md, @"^##\s*v([0-9][0-9.]*)", RegexOptions.Multiline);
                return m.Success ? m.Groups[1].Value : "";
            }

            /// <summary>取某个版本的正文（从它的标题到下一个标题之间）。</summary>
            public static string SectionOf(string md, string version)
            {
                if (string.IsNullOrEmpty(md) || string.IsNullOrEmpty(version)) return "";
                string v = Clean(version);
                string[] lines = md.Replace("\r\n", "\n").Split('\n');
                int start = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].StartsWith("## ")) continue;
                    if (lines[i].IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) { start = i; break; }
                }
                if (start < 0) return "";
                var sb = new StringBuilder();
                for (int i = start + 1; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("## ")) break;
                    sb.AppendLine(lines[i]);
                }
                return sb.ToString().Trim();
            }
        }

        // ===== 错误翻译 =====
        public static string Friendly(Exception ex)
        {
            string m = ex == null ? "" : ex.Message;
            if (m.Contains("HTTP 404"))
                return "找不到这个仓库或版本。\n可能是仓库改名了，或者 Release 还没发布。";
            if (m.Contains("HTTP 403") && m.ToLower().Contains("rate limit"))
                return "GitHub 的访问次数用完了。\n过一会儿再试，或者直接打开 GitHub 页面看。";
            if (m.Contains("HTTP 403"))
                return "GitHub 拒绝了这次请求（403）。\n如果开了代理，试着关掉再试一次。";
            if (m.Contains("SSL") || m.Contains("TLS"))
                return "和 GitHub 建立安全连接失败。\n多半是网络或代理的问题，换个网络再试。";
            if (m.Contains("超时") || m.Contains("timed out") || m.Contains("Timeout"))
                return "连接 GitHub 超时。\n检查一下网络，或者开代理再试。";
            if (m.Contains("解析") || m.Contains("resolve") || m.Contains("No such host"))
                return "连不上 GitHub。\n检查一下网络和 DNS。";
            return m;
        }
    }
}
