using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// `--selftest`：把不需要人工看的部分跑一遍，结果写 exe 旁边的 selftest.txt。
    ///
    /// 分三类：
    ///   PASS  真的跑过并成立
    ///   FAIL  真的跑过但不成立（退出码 1）
    ///   SKIP  需要 Anki / 网络 / 文件，这次没条件跑（不算失败）
    ///
    /// 注意 PASS 数会随环境变（比如本地有没有 CHANGELOG.md），这是故意的：
    /// 自检报告的是「这台机器上现在什么样」，不是一个固定数字。
    /// </summary>
    public static class SelfTest
    {
        // ===== 结果收集 =====
        static readonly List<string> _lines = new List<string>();
        static int _pass, _fail, _skip;

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) { _pass++; _lines.Add("[PASS] " + name); }
            else { _fail++; _lines.Add("[FAIL] " + name + (detail.Length > 0 ? " —— " + detail : "")); }
        }

        static void Skip(string name, string why)
        {
            _skip++;
            _lines.Add("[SKIP] " + name + (why.Length > 0 ? " —— " + why : ""));
        }

        /// <summary>把异常压成一行，免得报告里带上一堆堆栈。</summary>
        static string One(Exception e)
        {
            string m = e == null ? "" : e.Message.Replace("\r", " ").Replace("\n", " ");
            return m.Length > 160 ? m.Substring(0, 160) + "…" : m;
        }

        static string OneLine(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        // ===== 入口 =====
        public static void Run()
        {
            try { Ui.S = 1f; } catch { }   // 不依赖 DPI 探测，自检结果才稳定

            CheckVersion();
            CheckStore();
            CheckSecret();
            CheckConfigs();
            CheckCardFormat();
            CheckUi();
            CheckAvatar();
            CheckMarkdown();
            CheckChangelog();
            CheckNetwork();

            string title = "Anki 助手 自检 " + GitHub.VersionTag;
            string summary = "PASS " + _pass + " / FAIL " + _fail + " / SKIP " + _skip;
            var sb = new StringBuilder();
            sb.Append(title).Append("\r\n").Append(summary).Append("\r\n---\r\n");
            foreach (string l in _lines) sb.Append(l).Append("\r\n");
            sb.Append("---\r\n")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append("  .NET ").Append(Environment.Version)
              .Append("  exe: ").Append(Store.ExeDir)
              .Append("\r\n");

            string path = Path.Combine(Store.ExeDir, "selftest.txt");
            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                // exe 装在 Program Files 之类的只读位置时写不进去，退回用户目录
                path = Path.Combine(Store.Root, "selftest.txt");
                try
                {
                    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                    _lines.Add("[SKIP] exe 旁边写不了 selftest.txt —— " + One(e));
                }
                catch { path = "(写不进去)"; }
            }

            Console.WriteLine(title);
            Console.WriteLine(summary);
            foreach (string l in _lines)
                if (l.StartsWith("[FAIL]")) Console.WriteLine(l);
            Console.WriteLine("SELFTEST -> " + path);

            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ 1 版本
        static void CheckVersion()
        {
            string[] parts = GitHub.VersionTag.Split('.');
            bool shaped = GitHub.VersionTag.Length > 1 && GitHub.VersionTag[0] == 'v' && parts.Length == 3;
            for (int i = 0; shaped && i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i].TrimStart('v'), out n)) shaped = false;
            }
            Check("版本号合法", shaped, "期望 vX.Y.Z，实际 " + GitHub.VersionTag);

            Check("v1.9.0 < v1.10.0", GitHub.CompareVersion("v1.9.0", "v1.10.0") < 0,
                    "实际 " + GitHub.CompareVersion("v1.9.0", "v1.10.0"));
            Check("同版本比较返回 0", GitHub.CompareVersion("v1.10.0", "1.10.0") == 0,
                    "实际 " + GitHub.CompareVersion("v1.10.0", "1.10.0"));
            Check("v1.10.0 > v1.9.9", GitHub.CompareVersion("v1.10.0", "v1.9.9") > 0,
                    "实际 " + GitHub.CompareVersion("v1.10.0", "v1.9.9"));
            Check("Clean 去掉 v", GitHub.Clean("v1.15.8") == "1.15.8",
                    "期望 1.15.8，实际 " + GitHub.Clean("v1.15.8"));
        }

        // ------------------------------------------------------------ 2 Store
        static void CheckStore()
        {
            string key = "selftest.probe";
            string val = "自检-" + DateTime.Now.Ticks.ToString();
            Store.Set(key, val);
            Check("Store 写入后能读回", Store.Get(key) == val, "期望 " + val + "，实际 " + Store.Get(key));

            Store.Set("selftest.int", "42");
            Check("GetInt 读得到数值", Store.GetInt("selftest.int", 7) == 42,
                    "期望 42，实际 " + Store.GetInt("selftest.int", 7));
            Store.Set("selftest.badint", "abc");
            Check("GetInt 遇到垃圾用默认值", Store.GetInt("selftest.badint", 9) == 9,
                    "期望 9，实际 " + Store.GetInt("selftest.badint", 9));
            Check("GetInt 缺键用默认值", Store.GetInt("selftest.missing.int", 5) == 5,
                    "期望 5，实际 " + Store.GetInt("selftest.missing.int", 5));

            Store.SetBool("selftest.t", true);
            Store.SetBool("selftest.f", false);
            Store.Set("selftest.yes", "true");
            Check("GetBool 真值", Store.GetBool("selftest.t", false), "期望 true，实际 false");
            Check("GetBool 假值", !Store.GetBool("selftest.f", true), "期望 false，实际 true");
            Check("GetBool 认 true 字样", Store.GetBool("selftest.yes", false), "期望 true，实际 false");
            Check("GetBool 缺键用默认值", Store.GetBool("selftest.missing.bool", true),
                    "期望默认 true，实际 false");

            Check("Store.Root 存在", Directory.Exists(Store.Root), Store.Root);
            Check("Store.DataDir 存在", Directory.Exists(Store.DataDir), Store.DataDir);
        }

        // ------------------------------------------------------------ 2.5 内置 AI Key
        static void CheckSecret()
        {
            Check("内置 Key 封装能往返（Seal/Unseal）", Secret.VerifyEnvelope(), "同一口令解不回原文");

            bool hasZhipu = Secret.HasBuiltin(AiClient.PZhipu);
            Check("智谱有内置 Key", hasZhipu, "内置密文解不出来（口令或密文对不上）");

            string builtin = Secret.BuiltinKey(AiClient.PZhipu);
            // 只断言长度/前缀形状，不把明文或密钥片段写进自检报告
            Check("内置 Key 长度像样", builtin.Length == 49, "实际长度 " + builtin.Length);

            Check("付费服务商不内置 Key", !Secret.HasBuiltin(AiClient.PDeepSeek), "DeepSeek 不该有内置 Key");

            Check("手填 Key 优先于内置",
                    Secret.Resolve(AiClient.PZhipu, "  my-own-key  ") == "my-own-key",
                    "实际 " + Secret.Resolve(AiClient.PZhipu, "  my-own-key  "));
            Check("留空时回落到内置",
                    Secret.Resolve(AiClient.PZhipu, "") == builtin, "留空没回落到内置 Key");
            Check("预设为空时按默认预设算",
                    Secret.Resolve(null, "") == builtin, "预设为 null 没回落到默认预设");

            // 篡改一个字符必须验不过（MAC 的意义）
            string bad = builtin.Length > 0 ? Tamper(Secret.SealForBuild("abc")) : "";
            Check("密文被改过就解不出来", bad.Length > 0 && !Secret.BlobDecrypts(bad), "改了还解得开");
        }

        /// <summary>把 base64 里一个字符换掉，模拟被篡改的密文。</summary>
        static string Tamper(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            char[] a = b64.ToCharArray();
            int i = a.Length / 2;
            a[i] = a[i] == 'A' ? 'B' : 'A';
            return new string(a);
        }

        // ------------------------------------------------------------ 3 格式 config
        static void CheckConfigs()
        {
            List<CardConfig> all = Configs.All();
            Check("内置格式至少 2 套", all != null && all.Count >= 2,
                    "实际 " + (all == null ? "null" : all.Count.ToString()));

            CardConfig def = Configs.Get(CardConfig.IdVocabDefault);
            CardConfig alevel = Configs.Get(CardConfig.IdAlevel);
            Check("取得到 default", def != null);
            Check("取得到 alevel", alevel != null);
            if (def == null || alevel == null) return;

            // 字段清单：默认 4 个（单词/音标/中文/例句），A Level 7 个
            Check("default 有 4 个字段", def.Fields.Count == 4, "实际 " + def.Fields.Count);
            Check("alevel 有 7 个字段", alevel.Fields.Count == 7, "实际 " + alevel.Fields.Count);
            Check("default 字段名对得上",
                    def.FieldNames()[0] == "单词" && def.FieldNames()[2] == "中文" &&
                    def.FieldNames()[3] == "例句", Join(def.FieldNames()));

            // FieldNames 与 AiKeys 是同一份清单的两个视图（正面字段不产出，所以少 1）
            Check("default 的 AiKeys 比字段少 1",
                    def.AiKeys().Length == def.Fields.Count - 1,
                    "字段 " + def.Fields.Count + "，AI 键 " + def.AiKeys().Length);
            Check("alevel 的 AiKeys 比字段少 1",
                    alevel.AiKeys().Length == alevel.Fields.Count - 1,
                    "字段 " + alevel.Fields.Count + "，AI 键 " + alevel.AiKeys().Length);
            bool keysOk = true;
            for (int i = 0; i < def.AiKeys().Length; i++)
                if (string.IsNullOrEmpty(def.AiKeys()[i])) keysOk = false;
            Check("AI 键都不为空", keysOk, Join(def.AiKeys()));

            // config 没写自己的默认值就沿用应用级默认
            Check("alevel 回落到应用级牌组",
                    alevel.DeckOr(CardConfig.AppDefaultDeck) == CardConfig.AppDefaultDeck,
                    "实际 " + alevel.DeckOr(CardConfig.AppDefaultDeck));
            Check("alevel 回落到应用级标签",
                    alevel.TagsOr(CardConfig.AppDefaultTags) == CardConfig.AppDefaultTags,
                    "实际 " + alevel.TagsOr(CardConfig.AppDefaultTags));
            Check("alevel 回落到应用级学科",
                    alevel.SubjectOr(CardConfig.AppDefaultSubject) == CardConfig.AppDefaultSubject,
                    "实际 " + alevel.SubjectOr(CardConfig.AppDefaultSubject));
            // 相反：自己写了就用自己那份
            Check("default 用自己的牌组", def.DeckOr(CardConfig.AppDefaultDeck) == def.DefaultDeck,
                    "期望 " + def.DefaultDeck + "，实际 " + def.DeckOr(CardConfig.AppDefaultDeck));
            Check("DeckOr 空入参不炸", def.DeckOr(null) != null);

            Check("CardFront 有占位符", def.CardFront().IndexOf("{{") >= 0, OneLine(def.CardFront()));
            Check("CardBack 有占位符", def.CardBack().IndexOf("{{") >= 0, "背面模板没有 {{ }}");
            Check("CardBack 含背面字段", def.CardBack().IndexOf("{{音标}}") >= 0, "背面模板里没有音标占位符");
        }

        static string Join(string[] a)
        {
            return a == null ? "(null)" : string.Join("/", a);
        }

        // ------------------------------------------------------------ 4 解析与组装
        static void CheckCardFormat()
        {
            string p = CardFormat.BuildPrompt("friction", "测试学科");
            Check("BuildPrompt 含词", p != null && p.IndexOf("friction") >= 0, OneLine(p));
            Check("BuildPrompt 含学科", p != null && p.IndexOf("测试学科") >= 0, "提示词里没有学科名");

            // 典型返回：A Level 那套的六个键
            string good = "{\"phonetic\":\"英 /ˈfrɪkʃn/；美 /ˈfrɪkʃn/\",\"pos\":\"n\","
                        + "\"definition\":\"(n) the resistance that one surface meets when moving over another\","
                        + "\"formula\":\"\\\\(F=\\\\mu N\\\\)\","
                        + "\"confusables\":\"fiction /ˈfɪkʃn/ n. 小说\","
                        + "\"chinese\":\"摩擦力\"}";
            Dictionary<string, object> ai = null;
            try { ai = CardFormat.ParseAi(good); } catch (Exception e) { ai = null; Check("ParseAi 不抛异常", false, One(e)); }
            Check("ParseAi 解析得出来", ai != null, "返回 null");
            if (ai != null)
            {
                Check("ParseAi 取到 phonetic", CardConfig.GetString(ai, "phonetic", "").Length > 0);
                Check("ParseAi 取到 pos", CardConfig.GetString(ai, "pos", "") == "n",
                        "实际 " + CardConfig.GetString(ai, "pos", ""));
                Check("ParseAi 取到 chinese", CardConfig.GetString(ai, "chinese", "") == "摩擦力",
                        "实际 " + CardConfig.GetString(ai, "chinese", ""));
            }

            // 围栏 + 尾逗号：两种最常见的写坏方式
            string fenced = "```json\n" + good.Replace("\"chinese\":\"摩擦力\"", "\"chinese\":\"摩擦力\",") + "\n```";
            Dictionary<string, object> fixedAi = null;
            try { fixedAi = CardFormat.ParseAi(CardFormat.RepairJson(fenced)); } catch { }
            Check("RepairJson 修得掉围栏与尾逗号",
                    fixedAi != null && CardConfig.GetString(fixedAi, "chinese", "") == "摩擦力",
                    "修完还是解析不出来");
            Check("RepairJson 不碰值里的中文标点",
                    CardFormat.RepairJson("{\"a\":\"甲：乙\"}").IndexOf("甲：乙") >= 0,
                    OneLine(CardFormat.RepairJson("{\"a\":\"甲：乙\"}")));

            // 垃圾输入：ParseAi 返回 null（调用方走 FallbackFields），不能抛
            bool threw = false;
            Dictionary<string, object> junk = null;
            try { junk = CardFormat.ParseAi("今天天气不错，随便写点什么。"); }
            catch (Exception) { threw = true; }
            Check("ParseAi 对垃圾输入不抛异常", !threw, "抛异常了");
            Check("垃圾输入落到 null 或兜底字段", junk == null || junk.Count > 0, "返回了空对象");
            Dictionary<string, object> fb = CardFormat.FallbackFields("friction", "今天天气不错。");
            Check("FallbackFields 不炸且有释义",
                    fb != null && CardConfig.GetString(fb, "definition", "").Length > 0);

            // 公式归一化：各种输入都不该炸
            string[] formulas = new string[] {
                "\\(a\\)", "$a$", "$$a$$", "b^2-4ac", null, "", "中文说明 \\(x\\)", "力 = 质量 × 加速度" };
            bool formulaOk = true;
            for (int i = 0; i < formulas.Length; i++)
            {
                try { string r = CardFormat.NormalizeFormula(formulas[i]); if (r == null) formulaOk = false; }
                catch (Exception) { formulaOk = false; }
            }
            Check("NormalizeFormula 各种输入都不炸", formulaOk);
            Check("NormalizeFormula 保留已有 \\()",
                    CardFormat.NormalizeFormula("\\(a\\)") == "\\(a\\)",
                    "实际 " + CardFormat.NormalizeFormula("\\(a\\)"));

            // 标签
            string tags = CardFormat.TagsForDeck("A Level Pure Mathematics::力学");
            Check("TagsForDeck 有结果", !string.IsNullOrEmpty(tags), "返回空");
            Check("TagsForDeck 不含空格", tags != null && tags.IndexOf(' ') < 0,
                    "实际 " + tags);
            Check("TagsForDeck 对无关键返回 null", CardFormat.TagsForDeck("随便一个牌组") == null,
                    "实际 " + CardFormat.TagsForDeck("随便一个牌组"));
        }

        // ------------------------------------------------------------ 5 皮肤与绘制基座
        static void CheckUi()
        {
            List<Theme> all = Theme.All();
            Check("皮肤至少 4 套", all != null && all.Count >= 4,
                    "实际 " + (all == null ? "null" : all.Count.ToString()));

            bool applied = true;
            for (int i = 0; all != null && i < all.Count; i++)
            {
                try
                {
                    Theme.Apply(all[i]);
                    if (Ui.BG == Color.Empty) applied = false;
                }
                catch (Exception) { applied = false; }
            }
            Theme.Apply(Theme.Default);   // 收尾：别给后面留下深色
            Check("每套皮肤 Apply 后 Ui.BG 都不是空", applied);
            Check("收尾回到默认皮肤", Theme.Current != null && Theme.Current.Id == Theme.Default.Id,
                    "当前 " + (Theme.Current == null ? "null" : Theme.Current.Id));
            Check("默认皮肤是浅色", !Theme.Default.Dark, "默认皮肤被改成了深色");

            Check("Ui.Px(10) > 0", Ui.Px(10) > 0, "实际 " + Ui.Px(10));

            bool wrapOk = false;
            try
            {
                using (var bmp = new Bitmap(200, 100))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // Ui.F 是全局缓存字体，不能放进 using（Dispose 掉会毒到别处）
                    Font f = Ui.F(9f);
                    string longCn = new string('中', 400);
                    string[] ls = Ui.Wrap(g, longCn, f, 180);
                    wrapOk = ls != null && ls.Length > 1;
                }
            }
            catch (Exception) { wrapOk = false; }
            Check("Ui.Wrap 长中文能折行", wrapOk, "返回 null 或没折行");
        }

        // ------------------------------------------------------------ 5.5 头像
        // 只测不联网的部分（邮箱 → 文件名、裁圆角、编解码）；真实媒体库走
        // AnkiConnect 的那一段在网络自检里一律 SKIP，日常自检必须离线也能过。
        static void CheckAvatar()
        {
            Check("头像缓存路径是 data\\avatar.png",
                  AvatarBadge.AvatarPath == Path.Combine(Store.DataDir, "avatar.png"),
                  AvatarBadge.AvatarPath);

            string a = AvatarStore.Md5("  Kuroneko@Example.COM ");
            string b = AvatarStore.Md5("kuroneko@example.com");
            Check("Md5 先小写去空格再算", a == b && a.Length == 32, a);
            Check("空心邮箱也能算（不炸）", AvatarStore.Md5(null) == AvatarStore.Md5(""),
                  AvatarStore.Md5(null));

            string name = AvatarStore.MediaName("Kuroneko@Example.com");
            Check("媒体文件名跟 Android 一致",
                  name == "ankiassistant-avatar-" + b + ".png", name);
            Check("邮箱空着就没有媒体文件名", AvatarStore.MediaName("") == "",
                  "[" + AvatarStore.MediaName("") + "]");

            bool sizeOk = false, cornerOk = false, centerOk = false, roundOk = false;
            try
            {
                using (var src = new Bitmap(300, 200))
                {
                    using (Graphics g = Graphics.FromImage(src)) g.Clear(Color.Crimson);
                    using (Bitmap r = AvatarStore.Rounded(src))
                    {
                        sizeOk = r.Width == AvatarStore.Size && r.Height == AvatarStore.Size;
                        cornerOk = r.GetPixel(1, 1).A == 0;
                        centerOk = r.GetPixel(AvatarStore.Size / 2, AvatarStore.Size / 2).A == 255;

                        byte[] png = AvatarStore.EncodePng(r);
                        using (Bitmap back = AvatarStore.Decode(png))
                            roundOk = back != null && back.Width == AvatarStore.Size &&
                                      back.GetPixel(1, 1).A == 0;
                    }
                }
            }
            catch (Exception e) { Check("头像裁图/编解码不抛异常", false, One(e)); }

            Check("裁成 256x256", sizeOk);
            Check("四角切圆（透明）", cornerOk);
            Check("中心不透明", centerOk);
            Check("PNG 编解码往返", roundOk);
            Check("坏数据解不出来（返回 null）", AvatarStore.Decode(new byte[] { 1, 2, 3 }) == null);
        }

        // ------------------------------------------------------------ 6 Markdown 解析
        static void CheckMarkdown()
        {
            bool setOk = false, drawOk = false;
            string err = "";
            using (var host = new Form())
            {
                host.StartPosition = FormStartPosition.Manual;
                host.Location = new Point(-4000, -4000);   // 屏幕外，别闪一下
                host.ShowInTaskbar = false;
                host.ClientSize = new Size(600, 400);
                var v = new MarkdownView();
                v.Dock = DockStyle.Fill;
                host.Controls.Add(v);

                try
                {
                    v.Markdown = "# 标题\n- 项一\n**粗体**\n| a | b |\n| --- | --- |\n| 1 | 2 |\n```\ncode\n```\n> 引用\n---";
                    setOk = true;
                }
                catch (Exception e) { err = One(e); }

                try
                {
                    // 解析其实发生在 OnPaint，所以必须真建句柄、真画一次。
                    // 窗体不可见时 CreateControl() 是空操作，句柄永远建不出来 —— 得 Show()。
                    host.Show();
                    for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(20); }
                    drawOk = v.IsHandleCreated;
                    if (!drawOk) err = "MarkdownView 没拿到窗口句柄";
                    using (var bmp = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
                        host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                }
                catch (Exception e) { drawOk = false; err = One(e); }
                try { host.Close(); } catch { }
            }
            Check("MarkdownView 设 Markdown 不抛异常", setOk, err);
            Check("MarkdownView 建句柄并离屏绘制不抛异常", drawOk, err);
        }

        // ------------------------------------------------------------ 7 更新日志
        static void CheckChangelog()
        {
            string md = "";
            try { md = GitHub.Changelog.Local(); } catch { md = ""; }
            if (string.IsNullOrEmpty(md))
            {
                Skip("本地更新日志", "没有 CHANGELOG.md（" + GitHub.Changelog.BundledPath + "）");
                return;
            }

            string latest = "";
            try { latest = GitHub.Changelog.LatestVersionIn(md); } catch { }
            string[] parts = latest.Split('.');
            bool shaped = parts.Length == 3;
            for (int i = 0; shaped && i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n)) shaped = false;
            }
            Check("日志里最新的版本号形如 X.Y.Z", shaped, "实际「" + latest + "」");

            string sec = "";
            try { sec = GitHub.Changelog.SectionOf(md, GitHub.VersionTag); } catch { }
            Check("日志里有当前版本的段落 " + GitHub.VersionTag, !string.IsNullOrEmpty(sec),
                    "没找到 " + GitHub.VersionTag + " 的段落");
        }

        // ------------------------------------------------------------ 8 联网（一律 SKIP，不算失败）
        static void CheckNetwork()
        {
            bool ping = false;
            try { ping = AnkiConn.Ping(); }
            catch (Exception) { ping = false; }
            if (ping) Check("AnkiConnect 探活", true);
            else Skip("需要 Anki：AnkiConn.Ping()", "没连上本机 Anki（正常，不影响制卡以外的部分）");

            // 网络调用给个上限，免得没网时自检卡住
            string ver = "";
            string why = "";
            bool done = RunWithTimeout(delegate()
            {
                try
                {
                    GitHub.Release r = GitHub.LatestRelease();
                    ver = r == null ? "" : r.Tag;
                }
                catch (Exception e) { why = GitHub.Friendly(e); }
            }, 12000);

            if (!done) Skip("需要联网：GitHub 最新版本", "12 秒内没返回，跳过");
            else if (ver.Length == 0) Skip("需要联网：GitHub 最新版本", OneLine(why));
            else Check("GitHub 最新版本可读（" + ver + "）", ver.Length > 0);
        }

        /// <summary>在后台线程跑一段活，超时就放弃。返回 false = 超时（线程留在后台自己结束）。</summary>
        static bool RunWithTimeout(ThreadStart work, int ms)
        {
            using (var done = new ManualResetEvent(false))
            {
                var t = new Thread(delegate() { try { work(); } catch { } finally { done.Set(); } });
                t.IsBackground = true;
                t.Start();
                return done.WaitOne(ms);
            }
        }

        /// <summary>Store 里那份自检键的清理（留给以后的维护者，不自动调用）。</summary>
        public static void CleanProbeKeys()
        {
            Store.Set("selftest.probe", "");
            Store.Set("selftest.int", "");
            Store.Set("selftest.badint", "");
            Store.Set("selftest.t", "");
            Store.Set("selftest.f", "");
            Store.Set("selftest.yes", "");
        }
    }
}
