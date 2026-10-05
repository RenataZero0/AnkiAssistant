using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AnkiAssistant
{
    public enum SyncKind { Unknown, Ok, Error, Busy }

    /// <summary>
    /// 内置引擎（rslib）的状态。顶栏角标和底部状态栏都看这里。
    ///
    /// 现在没有 HTTP 探活了：引擎就在本进程里，唯一会慢的是「打开收藏库」
    /// （首次会建库、跑迁移），所以检查仍然放在后台线程做，结果再回到 UI 线程
    /// （MainForm.NotifySyncState）。
    /// </summary>
    public static class SyncState
    {
        public static SyncKind Kind = SyncKind.Unknown;
        public static string Detail = "正在打开收藏库…";
        public static string EngineVersion = "";
        public static DateTime LastCheck = DateTime.MinValue;

        /// <summary>正在检查时不会重复发起。</summary>
        static bool _busy;

        public static void Refresh()
        {
            if (_busy) return;
            _busy = true;
            Kind = SyncKind.Busy;
            Detail = "正在打开收藏库…";
            if (MainForm.Instance != null) MainForm.Instance.NotifySyncState();

            var t = new Thread(delegate()
            {
                try
                {
                    string err = "";
                    bool ok = false;
                    try
                    {
                        EngineVersion = AnkiConn.EngineVersionText();
                        ok = AnkiConn.Ping();
                    }
                    catch (Exception ex) { err = ex.Message; }

                    int due = 0;
                    if (ok)
                    {
                        try
                        {
                            List<string> decks = AnkiConn.DeckNames();
                            Store.Set("anki.decks", string.Join("\n", decks.ToArray()));
                        }
                        catch { }
                        try { due = AnkiConn.DueCount(""); } catch { }
                    }

                    LastCheck = DateTime.Now;
                    Kind = ok ? SyncKind.Ok : SyncKind.Error;
                    Detail = ok
                        ? ("收藏库已就绪　·　引擎 " + (EngineVersion.Length > 0 ? EngineVersion : "rslib") +
                           (due > 0 ? "　·　" + due + " 张待复习" : ""))
                        : ("引擎不可用：" + err);

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
                return (Detail.Length > 0 ? Detail : "收藏库已就绪") +
                       (LastCheck != DateTime.MinValue ? "　·　" + Ago(LastCheck) : "");
            if (Kind == SyncKind.Error) return Detail;
            return "正在打开收藏库…";
        }

        static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "刚刚检查";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分钟前检查";
            return (int)d.TotalHours + " 小时前检查";
        }

        /// <summary>需要写库之前调用：引擎没起来就把原因抛出来，别让用户对着按钮干等。</summary>
        public static void RequireConnected()
        {
            if (Kind == SyncKind.Ok) return;
            if (AnkiConn.Ping())
            {
                Kind = SyncKind.Ok;
                return;
            }
            throw new Exception(
                "内置引擎没起来。常见原因：\n" +
                "  1. 安装目录里缺 rslib_aa.dll（重装一次安装版即可）\n" +
                "  2. 上一个 AnkiAssistant 还在运行，收藏库被它占着 —— 关掉它再试\n" +
                "  3. 收藏库目录没有写入权限：" + AnkiConn.AnkiDir());
        }
    }
}
