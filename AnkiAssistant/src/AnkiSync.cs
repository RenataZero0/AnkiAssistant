using System;

namespace AnkiAssistant
{
    /// <summary>
    /// AnkiWeb 登录与同步 —— 现在由**内置引擎**自己完成，不再依赖 AnkiConnect，
    /// 也不再需要 Anki 桌面端在后台跑（移植自安卓版 AnkiSync.java）。
    ///
    /// 原则：
    ///   · 只存 hkey，永不落盘密码；
    ///   · 服务端可能把我们重定向到别的节点（new_endpoint），一旦返回就要记住并用于后续请求，
    ///     否则下次全量下载会失败（安卓版踩过：400 missing original size）；
    ///   · 全量同步必须先跑一次普通同步，从响应里拿 server_media_usn 当 server_usn。
    /// </summary>
    public static class AnkiSync
    {
        // 服务/方法编号取自 D:\Anki-Android-Backend\anki\out\pylib\anki\_backend_generated.py
        const int SvcSync = 1;
        const int MSyncMedia = 0;               // :65  sync_media(SyncAuth)
        const int MMediaSyncStatus = 2;         // :74  media_sync_status(Empty)
        const int MSyncLogin = 3;               // :79  sync_login(SyncLoginRequest)
        const int MSyncStatus = 4;              // :89  sync_status(SyncAuth)
        const int MSyncCollection = 5;          // :99  sync_collection(SyncCollectionRequest)
        const int MFullUploadOrDownload = 6;    // :109 full_upload_or_download(FullUploadOrDownloadRequest)

        public const string DefEndpoint = "https://sync.ankiweb.net/";

        /// <summary>Anki 需要全量同步。</summary>
        public sealed class FullSyncRequired : Exception
        {
            public readonly bool DownloadOnly;
            public FullSyncRequired(string reason, bool downloadOnly) : base(reason)
            {
                DownloadOnly = downloadOnly;
            }
        }

        /// <summary>全量同步方向：交给界面问用户，或直接指定。</summary>
        public enum FullMode
        {
            Ask = 0,
            Upload = 1,
            Download = 2
        }

        public sealed class Outcome
        {
            public bool DidFullSync;
            public bool Uploaded;      // 全量时是上传（否则是下载）
            public int ServerUsn;
            public string Message = "";
        }

        // ── 登录状态（就存在 Store 里）────────────────────────────────────────

        public static string Hkey() { return Store.Get("anki.web.hkey", "").Trim(); }
        public static string User() { return Store.Get("anki.web.user", "").Trim(); }
        public static string Endpoint() { return Store.Get("anki.web.endpoint", DefEndpoint).Trim(); }
        public static bool LoggedIn() { return Hkey().Length > 0; }

        /// <summary>退出登录：清掉 hkey（密码本来就没存）。</summary>
        public static void Logout()
        {
            Store.Set("anki.web.hkey", "");
        }

        /// <summary>登录并保存 hkey。返回用户名，失败抛异常（消息已中文化）。</summary>
        public static string Login(string user, string pass)
        {
            string u = (user ?? "").Trim();
            if (u.Length == 0) { throw new Exception("请先填 AnkiWeb 邮箱。"); }
            if ((pass ?? "").Length == 0) { throw new Exception("请先填 AnkiWeb 密码。"); }

            Pb.Writer w = new Pb.Writer().Str(1, u).Str(2, pass);
            string ep = Endpoint();
            if (ep.Length > 0) { w.Str(3, ep); }

            byte[] resp = Call(MSyncLogin, w.ToBytes(), "登录");
            string hkey = Str(resp, 1);
            if (hkey.Length == 0) { throw new Exception("登录成功但服务端没有返回凭证，请稍后再试。"); }

            Store.Set("anki.web.hkey", hkey);
            Store.Set("anki.web.user", u);
            string np = Str(resp, 2);
            if (np.Length > 0) { Store.Set("anki.web.endpoint", np); }
            return u;
        }

        /// <summary>
        /// 与 AnkiWeb 同步。fullMode = Ask 时遇到全量同步会抛 FullSyncRequired 让界面问用户。
        /// </summary>
        public static Outcome Sync(FullMode fullMode, bool withMedia)
        {
            Outcome o = new Outcome();
            if (!LoggedIn()) { throw new Exception("还没登录 AnkiWeb —— 先在上面填邮箱和密码点「登录并同步」。"); }

            // 先问一次：Anki 是否要求全量同步（顺手可能带回新的节点地址）
            byte[] st = Call(MSyncStatus, SyncAuth(), "检查同步状态");
            int need = Int(st, 1);
            Remember(Str(st, 4));

            if (need == 2 && fullMode == FullMode.Ask)
            {
                throw new FullSyncRequired("这台电脑的收藏库和 AnkiWeb 上的差别太大，需要做一次全量同步。", false);
            }

            // 普通同步（顺便拿全量同步必需的 server_usn）
            byte[] req = new Pb.Writer().Msg(1, SyncAuth()).Bool(2, withMedia).ToBytes();
            byte[] resp = Call(MSyncCollection, req, "同步");
            int changes = Int(resp, 3);
            int serverUsn = Int(resp, 5);
            string serverMsg = Str(resp, 2);
            Remember(Str(resp, 4));
            o.ServerUsn = serverUsn;
            if (serverMsg.Length > 0) { o.Message = serverMsg; }

            if (need == 2 || changes == 2 || changes == 3 || changes == 4)
            {
                if (fullMode == FullMode.Ask)
                {
                    throw new FullSyncRequired("这台电脑的收藏库和 AnkiWeb 上的差别太大，需要做一次全量同步。", changes == 3);
                }
                bool upload = fullMode == FullMode.Upload;
                if (changes == 3 && upload) { throw new Exception("AnkiWeb 上的收藏库是空的，只能用「用 AnkiWeb 覆盖本机」。"); }
                if (changes == 4 && !upload) { throw new Exception("这台电脑的收藏库是空的，只能用「用本机覆盖 AnkiWeb」。"); }

                Pb.Writer fw = new Pb.Writer().Msg(1, SyncAuth()).Bool(2, upload);
                if (serverUsn > 0) { fw.Int32(3, serverUsn); }
                Call(MFullUploadOrDownload, fw.ToBytes(), upload ? "上传收藏库" : "下载收藏库");
                o.DidFullSync = true;
                o.Uploaded = upload;
                if (withMedia) { Media(); }
                return o;
            }

            if (withMedia && changes != 0) { Media(); }
            return o;
        }

        /// <summary>只推一次媒体（头像等）。失败不致命，交给调用方决定要不要提示。</summary>
        public static void Media()
        {
            Call(MSyncMedia, SyncAuth(), "同步媒体");
        }

        /// <summary>媒体同步是否还在跑（内置引擎的媒体同步是异步的）。</summary>
        public static bool MediaBusy()
        {
            try
            {
                return Int(Call(MMediaSyncStatus, new byte[0], "查询媒体同步状态"), 1) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>界面上显示的一句话状态。</summary>
        public static string Describe()
        {
            if (!LoggedIn()) { return "未登录 AnkiWeb"; }
            string u = User();
            return u.Length > 0 ? ("已登录 " + u) : "已登录 AnkiWeb";
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        static byte[] SyncAuth()
        {
            Pb.Writer w = new Pb.Writer().Str(1, Hkey());
            string ep = Endpoint();
            if (ep.Length > 0) { w.Str(2, ep); }
            return w.ToBytes();
        }

        static void Remember(string newEndpoint)
        {
            if (newEndpoint != null && newEndpoint.Length > 0 && newEndpoint != Endpoint())
            {
                Store.Set("anki.web.endpoint", newEndpoint);
            }
        }

        static byte[] Call(int method, byte[] req, string what)
        {
            try
            {
                return Engine.Call(SvcSync, method, req);
            }
            catch (EngineException e)
            {
                throw new Exception(Humanize(what, e.Message));
            }
        }

        /// <summary>把引擎的原始英文错误换成能看懂的中文提示。</summary>
        public static string Humanize(string what, string raw)
        {
            string s = raw ?? "";
            string low = s.ToLowerInvariant();
            if (low.IndexOf("invalid username or password") >= 0) { return "邮箱或密码不对。"; }
            // AnkiWeb 真实返回的是这句："Email or password was incorrect; please try again."
            if (low.IndexOf("email or password") >= 0 || low.IndexOf("password was incorrect") >= 0)
            {
                return "邮箱或密码不对 —— 确认这是你在 ankiweb.net 注册时用的邮箱；" +
                       "密码记不清就去 ankiweb.net 点「忘记密码」重置（本程序不保存密码）。";
            }
            if (low.IndexOf("no such account") >= 0) { return "AnkiWeb 上没有这个账号 —— 确认邮箱拼写，或先去 ankiweb.net 注册。"; }
            if (low.IndexOf("too many") >= 0 || low.IndexOf("rate") >= 0)
            {
                return "AnkiWeb 让你慢一点：它限制了同步频率，等一会儿再试。";
            }
            if (low.IndexOf("connection") >= 0 || low.IndexOf("timed out") >= 0 || low.IndexOf("timedout") >= 0)
            {
                return what + "失败：连不上 AnkiWeb（检查网络或代理）。";
            }
            if (low.IndexOf("full sync") >= 0 || low.IndexOf("fullsync") >= 0)
            {
                return "需要做一次全量同步。";
            }
            return what + "失败：" + s;
        }

        static string Str(byte[] msg, int field)
        {
            if (msg == null) { return ""; }
            Pb.Reader r = new Pb.Reader(msg);
            int f, w;
            while (r.Next(out f, out w))
            {
                if (f == field && w == Pb.WireLength) { return r.Str(); }
                r.Skip();
            }
            return "";
        }

        static int Int(byte[] msg, int field)
        {
            if (msg == null) { return 0; }
            Pb.Reader r = new Pb.Reader(msg);
            int f, w;
            while (r.Next(out f, out w))
            {
                if (f == field && w == Pb.WireVarint) { return r.Int32(); }
                r.Skip();
            }
            return 0;
        }
    }
}
