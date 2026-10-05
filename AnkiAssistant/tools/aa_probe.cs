using System;
using System.Collections.Generic;
using AnkiAssistant;

/// <summary>
/// 联网自检探针：直接调用应用自己的 AnkiConn，对着**真实运行的 Anki** 走一遍
/// 「建模型 → 写卡 → 读回 → 删卡」，用来验证 AnkiConnect 协议层（尤其是
/// addNote 的 note 包装与 createModel 的参数层级）。产物是临时 exe，不入库。
///   csc /main:Probe ... src\*.cs tools\aa_probe.cs
/// </summary>
public static class Probe
{
    const string Deck = "AnkiAssistant 自检";
    const string Model = "AnkiAssistant 自检卡";

    static int fails;

    static void Check(string label, bool ok, string detail)
    {
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + label + (detail == null ? "" : "  [" + detail + "]"));
    }

    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch { }

        if (string.IsNullOrEmpty(Store.Get("anki.endpoint", "")))
            Store.Set("anki.endpoint", "http://127.0.0.1:8765");
        Console.WriteLine("endpoint = " + AnkiConn.Endpoint);

        // 1) 连通性
        int ver = -1;
        try { ver = AnkiConn.Version(); } catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
        Check("AnkiConn.Version() 读到 AnkiConnect 版本", ver > 0, "version=" + ver);
        Check("AnkiConn.Ping()", AnkiConn.Ping(), null);

        // 2) 读牌组
        List<string> decks = null;
        try { decks = AnkiConn.DeckNames(); } catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
        Check("AnkiConn.DeckNames()", decks != null && decks.Count > 0,
              decks == null ? "null" : decks.Count + " 个牌组");
        if (decks != null)
        {
            for (int i = 0; i < decks.Count && i < 5; i++) Console.WriteLine("      - " + decks[i]);
        }

        // 3) Store 读写
        string probeKey = "selftest.probe";
        string old = Store.Get(probeKey, "");
        Store.Set(probeKey, "自检-" + DateTime.Now.ToString("HHmmss"));
        string back = Store.Get(probeKey, "");
        Check("Store 写入后能读回", back.StartsWith("自检-"), back);
        Store.Set(probeKey, old);

        // 4) 建模型（验证 createModel 参数层级）
        string[] fields = new string[] { "单词", "音标", "中文" };
        bool created = false;
        try { created = AnkiConn.CreateModel(Model, new List<string>(fields), "{{单词}}", "<div>{{单词}}</div><div>{{音标}}</div><div>{{中文}}</div>", ".card{}"); }
        catch (Exception ex) { Console.WriteLine("      createModel: " + ex.Message); }
        Check("AnkiConn.CreateModel() 建笔记类型", created || AnkiConn.NoteTypeExists(Model), "created=" + created);

        List<string> mf = null;
        try { mf = AnkiConn.ModelFieldNames(Model); } catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
        Check("AnkiConn.ModelFieldNames() 字段齐", mf != null && mf.Count == 3,
              mf == null ? "null" : string.Join("/", mf.ToArray()));

        // 5) 写卡（验证 addNote 的 note 包装 + EnsureDeck 自动建牌组）
        var vals = new Dictionary<string, string>();
        vals["单词"] = "probe-word";
        vals["音标"] = "prəʊb";
        vals["中文"] = "自检用中文";
        bool ensured = false;
        try { ensured = AnkiConn.EnsureDeck(Deck); }
        catch (Exception ex) { Console.WriteLine("      ensureDeck: " + ex.Message); }
        Check("AnkiConn.EnsureDeck() 牌组不存在时自动建", ensured && AnkiConn.DeckExists(Deck), "exists=" + AnkiConn.DeckExists(Deck));

        long id = 0;
        try { id = AnkiConn.AddNote(Deck, Model, vals, new List<string>(new string[] { "AnkiAssistant::自检" })); }
        catch (Exception ex) { Console.WriteLine("      addNote: " + ex.Message); Check("AnkiConn.AddNote()", false, ex.Message); }
        if (id != 0) Check("AnkiConn.AddNote() 返回 note id", id > 0, "id=" + id);

        // 6) 读回
        if (id > 0)
        {
            System.Threading.Thread.Sleep(400); // Anki 的搜索索引是异步更新的，刚写完可能还搜不到
            List<long> found = null;
            try { found = AnkiConn.FindNotes("deck:\"" + Deck + "\""); }
            catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
            Check("AnkiConn.FindNotes() 找到刚写的卡", found != null && found.Contains(id),
                  found == null ? "null" : found.Count + " 条");

            System.Collections.IList notes = null;
            try { notes = (System.Collections.IList)AnkiConn.NotesInfo(new List<long>(new long[] { id })); }
            catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
            string got = "?";
            if (notes != null && notes.Count > 0)
            {
                object n = notes[0];
                System.Reflection.FieldInfo fi = n.GetType().GetField("Fields");
                var map = fi == null ? null : (Dictionary<string, string>)fi.GetValue(n);
                if (map != null && map.ContainsKey("中文")) got = map["中文"];
                else if (map != null) got = "字段名对不上: " + string.Join(",", new List<string>(map.Keys).ToArray());
            }
            Check("AnkiConn.NotesInfo() 中文回读一致", got == "自检用中文", got);
        }

        // 7) 清场：删掉自检卡
        if (id > 0)
        {
            bool deleted = false;
            try { AnkiConn.DeleteNotes(new List<long>(new long[] { id })); deleted = true; }
            catch (Exception ex) { Console.WriteLine("      deleteNotes: " + ex.Message); }
            Check("AnkiConn.DeleteNotes() 清场", deleted && AnkiConn.FindNotes("deck:\"" + Deck + "\"").Count == 0, null);
        }

        Console.WriteLine(fails == 0 ? "PROBE OK（全部通过）" : ("PROBE FAILED（" + fails + " 项失败）"));
        return fails == 0 ? 0 : 1;
    }
}
