using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;

namespace AnkiAssistant
{
    public enum SyncKind { Unknown, Ok, Error, Busy }

    /// <summary>
    /// 与 Anki 桌面端的连接状态。顶栏角标和底部状态栏都看这里。
    ///
    /// 探活要发 HTTP 请求，慢的时候能卡两三秒，所以永远在后台线程做，
    /// 结果再回到 UI 线程（MainForm.NotifySyncState）。
    /// </summary>
    public static class SyncState
    {
        public static SyncKind Kind = SyncKind.Unknown;
        public static string Detail = "正在检查 Anki…";
        public static int AnkiConnectVersion;
        public static DateTime LastCheck = DateTime.MinValue;

        /// <summary>正在检查时不会重复发起。</summary>
        static bool _busy;

        public static void Refresh()
        {
            if (_busy) return;
            _busy = true;
            Kind = SyncKind.Busy;
            Detail = "正在检查 Anki…";
            if (MainForm.Instance != null) MainForm.Instance.NotifySyncState();

            var t = new Thread(delegate()
            {
                try
                {
                    string err = "";
                    int v = 0;
                    bool ok = false;
                    try
                    {
                        v = AnkiConn.Version();
                        ok = true;
                    }
                    catch (Exception ex) { err = ex.Message; }

                    int due = 0;
                    if (ok)
                    {
                        try
                        {
                            System.Collections.Generic.List<string> decks = AnkiConn.DeckNames();
                            Store.Set("anki.decks", string.Join("\n", decks.ToArray()));
                        }
                        catch { }
                        try { due = AnkiConn.DueCount(""); } catch { }
                    }

                    AnkiConnectVersion = v;
                    LastCheck = DateTime.Now;
                    Kind = ok ? SyncKind.Ok : SyncKind.Error;
                    Detail = ok
                        ? ("已连接 Anki　·　AnkiConnect " + v + (due > 0 ? "　·　" + due + " 张待复习" : ""))
                        : ("未连接 Anki：" + err);

                    // 换个库以后笔记类型也可能变，顺手把列表刷一下
                    if (ok)
                    {
                        try
                        {
                            var models = AnkiConn.ModelNames();
                            Store.Set("anki.models", string.Join("\n", models.ToArray()));
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    _busy = false;
                    if (MainForm.Instance != null) MainForm.Instance.NotifySyncState();
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static string StatusText()
        {
            if (Kind == SyncKind.Ok)
                return (Detail.Length > 0 ? Detail : "已连接 Anki") +
                       (LastCheck != DateTime.MinValue ? "　·　" + Ago(LastCheck) : "");
            if (Kind == SyncKind.Error) return Detail;
            return "正在检查 Anki…";
        }

        static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "刚刚检查";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分钟前检查";
            return (int)d.TotalHours + " 小时前检查";
        }

        /// <summary>需要写库之前调用：没连上就把原因抛出来，别让用户对着按钮干等。</summary>
        public static void RequireConnected()
        {
            if (Kind == SyncKind.Ok) return;
            if (AnkiConn.Ping())
            {
                Kind = SyncKind.Ok;
                return;
            }
            throw new Exception(
                "没有连上 Anki。请确认：\n" +
                "  1. Anki 桌面端已经打开\n" +
                "  2. 装过 AnkiConnect 插件（编号 " + AnkiConn.AddonCode + "）\n" +
                "  3. 插件的端口是 8765（默认）\n" +
                "装好插件后要重启 Anki。");
        }
    }
}
