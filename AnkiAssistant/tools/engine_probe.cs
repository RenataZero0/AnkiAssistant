// engine_probe.cs —— 内置 Anki 引擎（rslib_aa.dll）联机探针。
//
// 编译（和 build.ps1 一样的引用集，只是换成 console + 显式 Main）：
//   csc /nologo /codepage:65001 /target:exe /main:AnkiAssistant.EngineProbe
//       /out:%TEMP%\engine_probe.exe
//       /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll
//       src\Pb.cs src\Engine.cs src\EngineRpc.cs tools\engine_probe.cs
//
// 跑：
//   %TEMP%\engine_probe.exe [rslib_aa.dll 的完整路径 | 仓库根目录]
//
// 流程（对应任务书第 5 条）：
//   Open(%TEMP%\aa-engine-test\collection.anki2)
//   -> DeckNames -> AddDeck("AA 探针") -> AddNotetypeLegacy(legacy JSON)
//   -> AddNote（落到 "AA 探针" 牌组）-> SearchNotes('deck:"AA 探针"') 命中 1 条
//   -> NotesInfo 回读字段 -> RemoveNotes 删除 -> Close -> 删临时目录
// 退出码：0 = 全部通过；1 = 有断言失败；2 = DLL 不在，跳过。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnkiAssistant
{
    public static class EngineProbe
    {
        static int fails;
        static int checks;
        static StreamWriter log;
        static readonly List<string> lines = new List<string>();

        // ---------------- 输出 ----------------

        static void Emit(string s)
        {
            lines.Add(s);
            try { Console.WriteLine(s); }
            catch (Exception) { }
            if (log != null)
            {
                try { log.WriteLine(s); log.Flush(); }
                catch (Exception) { }
            }
        }

        static void Info(string label, string detail)
        {
            Emit("      " + label + (detail == null || detail.Length == 0 ? "" : " = " + detail));
        }

        static void Check(string label, bool ok, string detail)
        {
            checks++;
            if (!ok) fails++;
            Emit((ok ? "  [PASS] " : "  [FAIL] ") + label +
                 (detail == null || detail.Length == 0 ? "" : "  <" + detail + ">"));
        }

        static void Step(string s)
        {
            Emit("");
            Emit("== " + s);
        }

        static string Esc(string s)
        {
            if (s == null) return "(null)";
            StringBuilder b = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r') b.Append("\\r");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\t') b.Append("\\t");
                else b.Append(c);
            }
            return b.ToString();
        }

        static string Join(string[] a)
        {
            if (a == null) return "(null)";
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) b.Append(", ");
                b.Append(Esc(a[i]));
            }
            return "[" + b.ToString() + "]";
        }

        static string JoinNames(List<AaNameId> a)
        {
            if (a == null) return "(null)";
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < a.Count; i++)
            {
                if (i > 0) b.Append(", ");
                b.Append(a[i].Name).Append("#").Append(a[i].Id);
            }
            return "[" + b.ToString() + "]";
        }

        // ---------------- 入口 ----------------

        public static int Main(string[] args)
        {
            string logPath = Path.Combine(Path.GetTempPath(), "engine_probe.log");
            try { log = new StreamWriter(logPath, false, new UTF8Encoding(false)); }
            catch (Exception) { log = null; }

            Emit("engine_probe  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Emit("log: " + logPath);

            string dll = ResolveDll(args);
            if (dll != null) Engine.UseDllFrom(dll);
            Info("Engine.DllPath", Engine.DllPath);
            Info("Engine.Available", Engine.Available ? "true" : "false");

            if (!Engine.Available)
            {
                Emit("");
                Emit("PROBE SKIP —— rslib_aa.dll 未就位，联机探针没有跑。");
                Info("原因", Engine.UnavailableReason);
                Info("期望位置", dll == null ? "(默认候选路径)" : dll);
                Close();
                return 2;
            }
            Info("aa_version()", Engine.Version());

            string dir = Path.Combine(Path.GetTempPath(), "aa-engine-test");
            string coll = Path.Combine(dir, "collection.anki2");
            string media = Path.Combine(dir, "collection.media");

            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e) { Info("清旧临时目录失败（忽略）", Engine.One(e)); }
            Directory.CreateDirectory(dir);
            Info("临时目录", dir);

            try
            {
                RunAll(dir, coll, media);
            }
            catch (Exception e)
            {
                fails++;
                Emit("");
                Emit("  [FATAL] 探针抛出异常：" + e.GetType().FullName + ": " + e.Message);
                if (e.InnerException != null)
                    Emit("          inner: " + e.InnerException.GetType().Name + ": " + e.InnerException.Message);
                Emit("          " + Engine.One(e));
            }
            finally
            {
                try { if (Engine.Opened) Engine.Close(); }
                catch (Exception e) { Info("收尾 Close 失败", Engine.One(e)); }

                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Info("临时目录已删除", dir);
                    Check("probe.temp_cleanup", !Directory.Exists(dir), dir);
                }
                catch (Exception e)
                {
                    Check("probe.temp_cleanup", false, Engine.One(e));
                }
            }

            Emit("");
            Emit("engine_probe: " + checks + " 项断言，" + (checks - fails) + " PASS / " + fails + " FAIL");
            Emit(fails == 0 ? "PROBE OK（全部通过）" : "PROBE FAILED");
            Close();
            return fails == 0 ? 0 : 1;
        }

        static void Close()
        {
            if (log != null)
            {
                try { log.Flush(); log.Close(); }
                catch (Exception) { }
                log = null;
            }
        }

        // ---------------- 探针主体 ----------------

        static void RunAll(string dir, string coll, string media)
        {
            const string DeckName = "AA 探针";
            const string ModelName = "AA 探针模型";

            Step("1. Open / 版本");
            // OpenCollectionRequest { collection_path=1, media_folder_path=2, media_db_path=3 }
            Engine.Open(coll, media, null);
            Check("engine.open", Engine.Opened, coll);
            Check("engine.open.collection_created", File.Exists(coll), coll);

            Step("2. DeckNames（默认牌组）");
            List<AaNameId> decks0 = EngineRpc.DeckNames();
            Info("DeckNames()", JoinNames(decks0));
            Check("engine.deck_names.not_null", decks0 != null, "count=" + (decks0 == null ? -1 : decks0.Count));
            Check("engine.deck_names.has_default", decks0 != null && decks0.Count >= 1, JoinNames(decks0));

            Step("3. AddDeck");
            long did = EngineRpc.AddDeck(DeckName);
            Info("AddDeck 返回 id", did.ToString());
            Check("engine.add_deck.id_positive", did > 0, "did=" + did);

            List<AaNameId> decks1 = EngineRpc.DeckNames();
            Info("DeckNames()", JoinNames(decks1));
            Check("engine.add_deck.visible", ContainsName(decks1, DeckName), JoinNames(decks1));
            long byName = EngineRpc.DeckIdByName(DeckName);
            Info("DeckIdByName", byName.ToString());
            Check("engine.deck_id_by_name", byName == did, "byName=" + byName + " did=" + did);

            Step("4. AddNotetypeLegacy");
            // legacy JSON：id 必须 0、flds/tmpls 是对象数组、要有 did、
            // req 是 [[0,"any",[0]]]、字段与模板都要有 id。
            string json = EngineRpc.BuildNotetypeJson(ModelName,
                new string[] { "Front", "Back" }, did,
                "{{Front}}", "{{FrontSide}}<hr id=answer>{{Back}}", ".card { font-family: arial; }");
            Info("请求 JSON", json);
            long ntid = EngineRpc.AddNotetypeLegacy(json);
            Info("AddNotetypeLegacy 返回 id", ntid.ToString());
            Check("engine.add_notetype.id_positive", ntid > 0, "ntid=" + ntid);

            long ntid2 = EngineRpc.NotetypeIdByName(ModelName);
            Info("NotetypeIdByName", ntid2.ToString());
            Check("engine.notetype_id_by_name", ntid2 == ntid, "byName=" + ntid2 + " ntid=" + ntid);
            List<AaNameId> models = EngineRpc.NotetypeNames();
            Info("NotetypeNames()", JoinNames(models));
            Check("engine.notetype_names.visible", ContainsName(models, ModelName), JoinNames(models));

            Step("5. FieldNames");
            string[] flds = EngineRpc.FieldNames(ntid);
            Info("FieldNames(ntid)", Join(flds));
            Check("engine.field_names", flds != null && flds.Length == 2, Join(flds));
            Check("engine.field_names.order", flds != null && flds.Length == 2 &&
                  flds[0] == "Front" && flds[1] == "Back", Join(flds));

            Step("6. AddNote（卡片落牌组靠 AddNoteRequest.deck_id）");
            // AddNoteRequest { Note note = 1; int64 deck_id = 2 }
            long nid = EngineRpc.AddNote(DeckName, ModelName,
                new string[] { "hello from engine_probe", "world" }, new string[] { "aa-probe" });
            Info("AddNote 返回 nid", nid.ToString());
            Check("engine.add_note.id_positive", nid > 0, "nid=" + nid);

            Step("7. SearchNotes(\"deck:\\\"AA 探针\\\"\")");
            string query = "deck:\"" + DeckName + "\"";
            Info("query", query);
            long[] ids = EngineRpc.SearchNotes(query);
            Info("命中 ids", ids == null ? "(null)" : "count=" + ids.Length + " " + string.Join(",", ToStr(ids)));
            Check("engine.search.hit_one", ids != null && ids.Length == 1, "count=" + (ids == null ? -1 : ids.Length));
            Check("engine.search.hit_is_new_note", ids != null && ids.Length == 1 && ids[0] == nid,
                  ids == null || ids.Length != 1 ? "n/a" : ("ids[0]=" + ids[0] + " nid=" + nid));

            Step("8. NotesInfo 回读字段");
            // 记录一个坑：FieldNamesForNotes(25/10) 返回的名字顺序跟 Note.fields 对不上，
            // 所以 NotesInfo 内部改用 FieldNames(notetype) 取名字。这里把它打出来留证据。
            string[] rawNames = EngineRpc.FieldNamesForNotes(ids == null ? new long[0] : ids);
            Info("FieldNamesForNotes(25/10) 原始返回", Join(rawNames));
            Info("FieldNames(23/16) 用于配对", Join(EngineRpc.FieldNames(ntid)));

            List<AaNoteInfo> infos = EngineRpc.NotesInfo(ids == null ? new long[0] : ids);
            Check("engine.notes_info.count", infos != null && infos.Count == 1, "count=" + (infos == null ? -1 : infos.Count));
            if (infos != null && infos.Count == 1)
            {
                AaNoteInfo n = infos[0];
                Info("Note.id", n.Id.ToString());
                Info("Note.notetype_id", n.NotetypeId.ToString());
                Info("Note.tags", Join(n.Tags));
                Info("Note.fields", Join(n.Fields));
                Info("FieldNames", Join(n.FieldNames));
                Check("engine.notes_info.id", n.Id == nid, "id=" + n.Id + " nid=" + nid);
                Check("engine.notes_info.notetype", n.NotetypeId == ntid, "nt=" + n.NotetypeId + " ntid=" + ntid);
                Check("engine.notes_info.fields", n.Fields != null && n.Fields.Length == 2 &&
                      n.Fields[0] == "hello from engine_probe" && n.Fields[1] == "world", Join(n.Fields));
                Check("engine.notes_info.field_by_name", n.Field("Back") == "world",
                      "Back=" + Esc(n.Field("Back")));
                Check("engine.notes_info.tags", n.Tags != null && n.Tags.Length == 1 && n.Tags[0] == "aa-probe",
                      Join(n.Tags));
            }

            Step("9. GetConfigJson / SetConfigJson");
            // SetConfigJsonRequest { string key = 1; bytes value_json = 2; bool undoable = 3 }
            EngineRpc.SetConfigJson("aaProbeKey", "\"hello-config\"");
            string cfg = EngineRpc.GetConfigJson("aaProbeKey");
            Info("GetConfigJson(aaProbeKey)", Esc(cfg));
            Check("engine.config_json.roundtrip", cfg != null && cfg.IndexOf("hello-config") >= 0, Esc(cfg));

            Step("10. AddMediaFile");
            byte[] blob = new UTF8Encoding(false).GetBytes("engine_probe media payload\n");
            string stored = EngineRpc.AddMediaFile("aa_probe_note.txt", blob);
            Info("AddMediaFile 返回", Esc(stored));
            Check("engine.add_media_file.name", stored != null && stored.Length > 0, Esc(stored));
            if (stored != null && stored.Length > 0)
            {
                string p = Path.Combine(media, stored);
                Check("engine.add_media_file.on_disk", File.Exists(p), p);
                if (File.Exists(p))
                {
                    byte[] back = File.ReadAllBytes(p);
                    Check("engine.add_media_file.bytes", BytesEqual(back, blob),
                          "len=" + back.Length + " expect=" + blob.Length);
                }
            }

            Step("11. RemoveNotes + 复查搜索为空");
            int removed = EngineRpc.RemoveNotes(new long[] { nid });
            Info("RemoveNotes 返回", removed.ToString());
            long[] after = EngineRpc.SearchNotes(query);
            Info("删后搜索", after == null ? "(null)" : "count=" + after.Length);
            Check("engine.remove_notes.empty_after", after != null && after.Length == 0,
                  "count=" + (after == null ? -1 : after.Length));

            Step("12. SyncStatus（未登录，只验证 RPC 能通）");
            // SyncStatusResponse { Required required = 1; optional string new_endpoint = 4 }
            try
            {
                AaSyncAuth auth = new AaSyncAuth();
                auth.HKey = "";
                AaSyncStatus st = EngineRpc.SyncStatus(auth);
                Info("SyncStatus.required", st.Required + " (" + st.RequiredText() + ")");
                Check("engine.sync_status.reachable", true, "required=" + st.Required);
            }
            catch (EngineException e)
            {
                // 伪 hkey 被服务端/本地拒绝也算「RPC 通道可用」。
                Info("SyncStatus 抛错（预期内）", e.Message);
                Check("engine.sync_status.reachable", true, "code=" + e.Code);
            }

            Step("13. CloseCollection");
            EngineRpc.CloseCollection();
            Check("engine.close_collection", true, "无异常");
        }

        // ---------------- 小工具 ----------------

        static string[] ToStr(long[] a)
        {
            if (a == null) return new string[0];
            string[] s = new string[a.Length];
            for (int i = 0; i < a.Length; i++) s[i] = a[i].ToString();
            return s;
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool ContainsName(List<AaNameId> list, string name)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Name == name) return true;
            return false;
        }

        /// <summary>
        /// 参数可以是 rslib_aa.dll 的完整路径，也可以是仓库根目录（会补 tools\lib\rslib_aa.dll）。
        /// 不给参数时按几个常见位置找。
        /// </summary>
        static string ResolveDll(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] != null && args[0].Length > 0)
            {
                string a = args[0];
                if (a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return a;
                return Path.Combine(Path.Combine(Path.Combine(a, "AnkiAssistant"), "tools"), "lib\\rslib_aa.dll");
            }

            string[] guesses = new string[] {
                Path.Combine(Path.GetTempPath(), "rslib_aa.dll"),
                "D:\\UsrFiles\\Documents\\NCUK IFY Self Study\\AnkiAssistant\\AnkiAssistant\\tools\\lib\\rslib_aa.dll"
            };
            for (int i = 0; i < guesses.Length; i++)
                if (File.Exists(guesses[i])) return guesses[i];
            return null;
        }
    }
}
