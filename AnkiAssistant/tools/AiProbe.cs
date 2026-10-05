// tools\AiProbe.cs -- network probe, NOT part of the shipped app.
//
// Exercises the sealed built-in AI key end to end: resolve -> real HTTP -> print reply.
// Build (mirrors build.ps1, but with an explicit entry point and this file added):
//
//   $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
//   & $csc /nologo /codepage:65001 /target:exe /out:"$env:TEMP\ai_probe.exe" /main:AnkiAssistant.AiProbe `
//       /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
//       /r:System.Core.dll /r:System.Security.dll (Get-ChildItem "src\*.cs" | ForEach-Object FullName) "tools\AiProbe.cs"
//   & "$env:TEMP\ai_probe.exe"
using System;
using System.Collections.Generic;
using System.Text;

namespace AnkiAssistant
{
    public static class AiProbe
    {
        public static void Main(string[] args)
        {
            int fails = 0;

            string preset = Store.Get("ai.preset", AiClient.DefaultPreset);
            string provider = Store.Get("ai.provider", preset);
            string url = Store.Get("ai.url", AiClient.PresetBaseUrl(preset));
            string model = Store.Get("ai.model", AiClient.PresetModel(preset));
            string userKey = Store.Get("ai.key", "");
            string key = Secret.Resolve(provider, userKey);

            Console.WriteLine("preset   = " + preset);
            Console.WriteLine("provider = " + provider);
            Console.WriteLine("model    = " + model);
            Console.WriteLine("url      = " + url);
            Console.WriteLine("user key = " + (userKey.Length > 0 ? "set (" + userKey.Length + ")" : "empty"));
            Console.WriteLine("key used = " + (key.Length > 0 ? "len " + key.Length + " sha-shape " + key.Substring(0, 4) + "..." : "NONE"));
            if (key.Length == 0) { Console.WriteLine("FAIL: no key resolved"); Environment.Exit(1); }

            try
            {
                AiReply r = AiClient.ChatDetailed(url, key, model, "你是一个助手。", "只回复两个字：测试", false);
                string t = r == null ? "" : r.Content;
                Console.WriteLine("reply    = [" + t + "]");
                if (t.Trim().Length == 0) { Console.WriteLine("FAIL: empty reply"); fails++; }
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL: " + e.Message);
                fails++;
            }

            // 第二段：完整走一遍制卡页的 AI 流程（提示词 -> 模型 -> 解析 -> 映射到字段），
            // 确认真机"装完就能用内置 Key 制卡"，而不只是能连上。
            CardConfig cfg = Configs.Active;
            string word = "friction";
            string subject = cfg.SubjectOr(CardConfig.AppDefaultSubject);
            Console.WriteLine();
            Console.WriteLine("---- 制卡流程：" + cfg.Name + " / " + word + " ----");
            try
            {
                AiReply r = AiClient.ChatDetailed(url, key, model, cfg.SystemPrompt,
                    cfg.BuildPromptStrict(word, subject), false);
                Dictionary<string, object> parsed = null;
                string used = "";
                try { parsed = CardFormat.ParseAi(r.Content); used = "严格"; }
                catch
                {
                    AiReply r2 = AiClient.ChatDetailed(url, key, model, cfg.SystemPrompt,
                        cfg.BuildPrompt(word, subject), false);
                    try { parsed = CardFormat.ParseAi(r2.Content); used = "宽松"; }
                    catch { parsed = CardFormat.FallbackFields(word, r2.Content); used = "兜底"; }
                }
                Console.WriteLine("parse    = " + used);
                if (parsed == null) { Console.WriteLine("FAIL: parsed null"); fails++; }
                else
                {
                    int filled = 0;
                    for (int i = 1; i < cfg.Fields.Count; i++)
                    {
                        Field f = cfg.Fields[i];
                        string v = Pick(parsed, f.Name);
                        if (v.Length == 0) v = Pick(parsed, f.Key);
                        if (v.Trim().Length > 0) filled++;
                        string one = v.Replace("\r", " ").Replace("\n", " ");
                        if (one.Length > 70) one = one.Substring(0, 70) + "...";
                        Console.WriteLine("  " + f.Name + " = " + one);
                    }
                    Console.WriteLine("filled   = " + filled + " / " + (cfg.Fields.Count - 1));
                    if (filled < cfg.Fields.Count - 1)
                    {
                        Console.WriteLine("FAIL: 有字段没填上");
                        fails++;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL: " + e.Message);
                fails++;
            }

            Console.WriteLine(fails == 0 ? "AI PROBE OK" : "AI PROBE FAILED (" + fails + ")");
            Environment.Exit(fails == 0 ? 0 : 1);
        }

        static string Pick(Dictionary<string, object> d, string key)
        {
            if (d == null || string.IsNullOrEmpty(key)) return "";
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return "";
            return o.ToString().Trim();
        }
    }
}
