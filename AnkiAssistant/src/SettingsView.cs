using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 设置页：左栏栏目索引 + 右侧可滚动卡片区。
    ///
    /// 左栏固定 Ui.Px(168) 宽（安卓版窄屏时索引横过来，桌面窗口够宽，竖排更好点）；
    /// 右侧八张卡片宽度跟着窗口走，卡片内部所有控件先按 1 份坐标系登记、等宽度定下来
    /// 再整体换算成像素（<see cref="CardBuilder.Layout"/>），所以不会出现横向滚动条。
    /// </summary>
    public class SettingsView : Panel
    {
        // 栏目顺序固定，索引、卡片、_cardY 三者一一对应
        static readonly string[] Sections = new string[] {
            "AI 自动填充", "输出格式", "同步", "制卡习惯", "皮肤", "头像", "关于", "更新内容"
        };

        // 每张卡片在内容区里的 Y（像素），点击索引时用它精确滚过去
        readonly int[] _cardY = new int[Sections.Length];

        ScrollHost _host;
        IndexItem[] _index;
        int _sel = -1;
        bool _built;        // 卡片是否已经建出来过（首次显示前 _host 里一张卡都没有）
        int _builtW = -1;   // 上次建卡片时内容区的宽度，用来判断"宽度变了要重排"
        bool _loading;      // 灌值期间挡住 TextChanged，免得把读出来的值又写回去
        Timer _spy, _syncPoll;
        long _syncWait;

        // AI 卡片
        Pill _providerBtn;
        Input _keyBox, _modelBox, _urlBox;
        Label _keyHint;
        CheckBox _thinking;

        // 输出格式卡片
        Label _cfgLabel;
        Pill _cfgDelete, _cfgReset;

        // 同步卡片
        Input _endpointBox;
        AnkiLamp _lamp;
        CheckBox _autoSync, _autoCheck;

        // 制卡习惯
        CheckBox _clear, _autoPreview;

        // 皮肤
        SkinDot _skinDot;
        Label _skinName;

        // 头像
        Input _mailBox;
        Label _avatarMsg;
        Timer _mailWait;

        // 关于 / 更新
        Label _aboutVer, _updateVer, _updateMsg;
        Pill _checkBtn;

        public SettingsView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.BG;
            Font = Ui.F(9f);

            BuildSkeleton();

            // 滚动位置 → 索引高亮只能轮询：Panel 没有暴露滚动事件，
            // 自己拦 WM_VSCROLL 又要多一个子类，不值当。
            _spy = new Timer();
            _spy.Interval = 90;
            _spy.Tick += delegate { Spy(); };
            _spy.Start();
        }

        // ================================================================ 骨架

        void BuildSkeleton()
        {
            var idx = new Panel();
            idx.Dock = DockStyle.Left;
            idx.Width = Ui.Px(168);
            idx.BackColor = Ui.CARD;
            idx.Paint += delegate(object s, PaintEventArgs e)
            {
                // 靠内容侧画一道线：左边是 Ui.CARD、右边是 Ui.BG，不给线两块会糊在一起
                using (var p = new Pen(Ui.LINE))
                    e.Graphics.DrawLine(p, idx.Width - 1, 0, idx.Width - 1, idx.Height);
            };
            Controls.Add(idx);

            _index = new IndexItem[Sections.Length];
            for (int i = 0; i < Sections.Length; i++)
            {
                var it = new IndexItem(Sections[i]);
                it.Left = Ui.Px(10);
                it.Width = Ui.Px(148);
                it.Height = Ui.Px(40);
                it.Top = Ui.Px(14) + i * Ui.Px(42);
                int n = i;
                it.Click += delegate { ScrollTo(n, true); };
                idx.Controls.Add(it);
                _index[i] = it;
            }

            _host = new ScrollHost();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Ui.BG;
            _host.BodyPadding = new Padding(Ui.Px(16), Ui.Px(14), Ui.Px(16), Ui.Px(16));
            _host.MaxContentWidth = Ui.Px(860);   // 大屏限宽，一行字太长反而难读
            _host.ScrollChanged += delegate { Spy(); };
            Controls.Add(_host);
            _host.BringToFront();
        }

        // ================================================================ 整页重建

        /// <summary>按当前宽度重建整页（第一次显示、窗口变宽都会走这里）。</summary>
        void Rebuild()
        {
            if (_loading || _host == null) return;
            _loading = true;
            try
            {
                int keep = _host.ScrollY;
                _host.ClearContent();
                for (int i = 0; i < Sections.Length; i++)
                {
                    _cardY[i] = _host.ContentHeight;
                    BuildSection(i);
                }
                _host.Recalc();
                _host.ScrollTo(keep);
                _built = true;
                _builtW = ContentW();
            }
            finally { _loading = false; }
            LoadValues();   // 卡片是刚建出来的空控件，把存下来的值灌回去
            Spy();
        }

        void BuildSection(int i)
        {
            switch (i)
            {
                case 0: BuildAi(); break;
                case 1: BuildFormat(); break;
                case 2: BuildSync(); break;
                case 3: BuildHabit(); break;
                case 4: BuildSkin(); break;
                case 5: BuildAvatar(); break;
                case 6: BuildAbout(); break;
                default: BuildUpdates(); break;
            }
        }

        // ---------------------------------------------------------------- 1. AI 自动填充

        void BuildAi()
        {
            var c = new CardBuilder(_host, Sections[0]);
            c.Tip("选一个服务商，填好 Key（免密钥的不用填），点「测试」。");

            c.Label("服务商");
            _providerBtn = c.Button("选择服务商", false, delegate { PickProvider(); });

            c.Label("API Key");
            _keyBox = c.Input(true);
            _keyBox.TextChanged += delegate
            {
                if (_loading) return;
                Store.Set("ai.key", _keyBox.Text);
                RefreshKeyHint();
            };
            _keyHint = c.Tip("");

            c.Label("模型");
            _modelBox = c.Input(false);
            _modelBox.TextChanged += delegate
            {
                if (_loading) return;
                Store.Set("ai.model", _modelBox.Text.Trim());
            };

            c.Label("接口地址（一般不用改）");
            _urlBox = c.Input(false);
            _urlBox.TextChanged += delegate
            {
                if (_loading) return;
                Store.Set("ai.url", _urlBox.Text.Trim());
            };

            c.Gap(12);
            c.Button("测试 AI", true, delegate { TestAi(); });

            c.Gap(8);
            _thinking = c.Check("让思考型模型先思考", false);
            _thinking.CheckedChanged += delegate { Store.SetBool("ai.thinking", _thinking.Checked); };
            c.Hint("默认关闭。开启后更慢，且长推理容易把 JSON 输出挤断（需要重试）。");

            c.Done();
        }

        void RefreshProviderBtn()
        {
            if (_providerBtn == null) return;
            string id = Store.Get("ai.provider", AiClient.DefaultPreset);
            _providerBtn.Text = "当前：" + AiClient.PresetLabel(id) + " ▾";
            _providerBtn.Invalidate();
            RefreshKeyHint();
        }

        /// <summary>Key 框下面那行小字：说清"留空就是内置 Key、手填优先"。</summary>
        void RefreshKeyHint()
        {
            if (_keyHint == null) return;
            string id = Store.Get("ai.provider", AiClient.DefaultPreset);
            bool typed = _keyBox != null && _keyBox.Text.Trim().Length > 0;
            bool builtin = Secret.HasBuiltin(id);

            string t;
            if (typed) t = "正在用你填的 Key（它优先于内置 Key）。";
            else if (builtin) t = "留空就用内置的 " + AiClient.PresetLabel(id) + " Key，不用自己填。";
            else if (AiClient.NeedsKey(id)) t = "这个服务商需要自己填 Key。";
            else t = "这个服务商免密钥，不用填。";
            _keyHint.Text = t;
            _keyHint.Invalidate();
        }

        void PickProvider()
        {
            string[] ids = new string[] {
                AiClient.PDeepSeek, AiClient.PDoubao, AiClient.PZhipu,
                AiClient.PSilicon, AiClient.PCustom
            };
            string cur = Store.Get("ai.provider", AiClient.DefaultPreset);
            string[] names = new string[ids.Length];
            string[] notes = new string[ids.Length];
            int sel = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                names[i] = AiClient.PresetLabel(ids[i]);
                if (Secret.HasBuiltin(ids[i])) notes[i] = "已内置免费 Key";
                else notes[i] = AiClient.NeedsKey(ids[i]) ? "需要 API Key" : "免密钥";
                if (ids[i] == cur) sel = i;
            }

            int pick = Dlg.Choose(FindForm(), "AI 服务商",
                "当前：" + AiClient.PresetLabel(cur), names, notes, sel);
            if (pick < 0) return;

            string now = ids[pick];
            Store.Set("ai.provider", now);

            // 只有用户没手动改过模型/地址时才用预设覆盖；改过的尊重用户
            string model = Store.Get("ai.model", "");
            string url = Store.Get("ai.url", "");
            if (model.Length == 0 && url.Length == 0)
            {
                model = AiClient.PresetModel(now);
                url = AiClient.PresetBaseUrl(now);
                Store.Set("ai.model", model);
                Store.Set("ai.url", url);
            }

            _loading = true;
            _modelBox.Text = model;
            _urlBox.Text = url;
            _loading = false;

            RefreshProviderBtn();
            MainForm.Instance.SetStatus("已切到 " + AiClient.PresetLabel(now) + "，点「测试 AI」验证");
        }

        void TestAi()
        {
            // 点测试前先把输入框落盘：用户常常是刚改完就点
            Store.Set("ai.key", _keyBox.Text);
            Store.Set("ai.model", _modelBox.Text.Trim());
            Store.Set("ai.url", _urlBox.Text.Trim());

            string preset = Store.Get("ai.provider", AiClient.DefaultPreset);
            string url = Store.Get("ai.url", AiClient.PresetBaseUrl(preset));
            string key = Secret.Resolve(preset, Store.Get("ai.key", ""));
            string model = Store.Get("ai.model", AiClient.PresetModel(preset));
            bool think = Store.GetBool("ai.thinking", false);

            _providerBtn.Enabled = false;
            MainForm.Instance.SetStatus("正在测试…");

            // 必须有后台线程：免费档要 30～60 秒，卡在 UI 线程上窗口会白掉
            Dlg.Wait(FindForm(), "测试 AI", "正在请求模型，免费档可能要 30～60 秒…", delegate
            {
                AiReply r = null;
                string err = null;
                try
                {
                    r = AiClient.ChatDetailed(url, key, model, "你是一个助手。", "只回复两个字：测试", think);
                }
                catch (Exception ex) { err = ex.Message; }

                BeginInvokeOnUi(delegate
                {
                    _providerBtn.Enabled = true;
                    if (err != null)
                    {
                        MainForm.Instance.SetStatus("AI 测试失败", true);
                        Dlg.Info(FindForm(), "测试失败", "", err);
                        return;
                    }
                    string sec = r.Seconds.ToString("0.0");
                    MainForm.Instance.SetStatus("AI 连接正常 · " + sec + " 秒");
                    Dlg.Info(FindForm(), "连接正常", "",
                        "模型回复：" + r.Content + "\n耗时 " + sec + " 秒");
                });
            });
        }

        // ---------------------------------------------------------------- 2. 输出格式

        void BuildFormat()
        {
            var c = new CardBuilder(_host, Sections[1]);
            c.Tip("控制 AI 的输出格式与卡片字段，并连带决定该格式使用的默认牌组、标签与学科背景。" +
                  "内置一套通用英语词汇格式；可按学科或考试另建格式，随时切换。");

            _cfgLabel = c.Tip("");
            _cfgLabel.ForeColor = Ui.TEXT_BODY;

            c.Gap(10);
            c.Button("切换格式", true, delegate { PickConfig(); });

            c.Gap(8);
            c.Row(32, 3);
            c.InRow(c.MakeButton("新建", false, delegate { NewConfig(); }), 8);
            c.InRow(c.MakeButton("编辑", false, delegate { EditConfig(Configs.Active); }), 8);
            _cfgDelete = c.MakeButton("删除", false, delegate { DeleteConfig(); });
            c.InRow(_cfgDelete, 0);

            c.Gap(8);
            _cfgReset = c.Button("恢复默认", false, delegate { ResetConfig(); });
            _cfgReset.Visible = false;

            c.Done();
        }

        void RefreshFormatCard()
        {
            if (_cfgLabel == null) return;
            CardConfig cfg = Configs.Active;

            var sb = new System.Text.StringBuilder();
            sb.Append("当前：").Append(cfg.Name);
            if (Configs.IsBuiltin(cfg.Id))
                sb.Append(Configs.IsOverridden(cfg.Id) ? "（内置 · 已覆盖）" : "（内置）");
            else sb.Append("（自定义）");

            string[] fields = cfg.FieldNames();
            sb.Append("\n字段 ").Append(fields.Length).Append(" 个：");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(fields[i]);
            }
            sb.Append("\n笔记类型：").Append(cfg.NoteType);
            if (CardConfig.UsesAppDefaults(cfg.Id))
                sb.Append("\n牌组 / 标签 / 学科：跟随应用级默认值");
            _cfgLabel.Text = sb.ToString();

            _cfgDelete.Enabled = !Configs.IsBuiltin(cfg.Id);
            _cfgDelete.Invalidate();

            // 恢复默认按钮只在"内置且被改过"时出现。它的格子在建卡片时就已经登记
            // （Button 总会占 32dp），所以这里只切 Visible 就够，不需要整页重排 ——
            // 重排会把 _cfgLabel 换成新控件，刚灌好的文字反而丢了。
            bool show = Configs.IsBuiltin(cfg.Id) && Configs.IsOverridden(cfg.Id);
            _cfgReset.Visible = show;
            _cfgReset.Invalidate();
        }

        void PickConfig()
        {
            List<CardConfig> all = Configs.All();
            string active = Configs.Active.Id;
            string[] names = new string[all.Count];
            string[] notes = new string[all.Count];
            int sel = 0;
            for (int i = 0; i < all.Count; i++)
            {
                CardConfig x = all[i];
                names[i] = x.Name;
                notes[i] = x.FieldNames().Length + " 字段 · " + x.NoteType +
                           (Configs.IsBuiltin(x.Id) ? " · 内置" : " · 自定义");
                if (x.Id == active) sel = i;
            }

            int pick = Dlg.Choose(FindForm(), "切换输出格式", "选中的格式立刻用于后续制卡", names, notes, sel);
            if (pick < 0) return;

            Configs.Active = all[pick];      // setter 自己写 Store.ActiveConfigId
            Store.Set("cfg.active", all[pick].Id);
            RefreshFormatCard();
            MainForm.Instance.SetStatus("已切到格式「" + all[pick].Name + "」");
        }

        void NewConfig()
        {
            string[] v = Dlg.Form(FindForm(), "新建输出格式", "",
                new string[] { "名称" }, new string[] { "" }, null, "创建", "取消", 420,
                "例如：雅思词汇、A Level 物理");
            if (v == null) return;

            string name = v[0] == null ? "" : v[0].Trim();
            if (name.Length == 0) name = "新格式";

            CardConfig cfg = Configs.NewBlank(name);
            if (!ConfigEditor.Edit(FindForm(), cfg)) return;

            Configs.Save(cfg);
            Configs.Active = cfg;
            Store.Set("cfg.active", cfg.Id);
            RefreshFormatCard();
            Rebuild();
            MainForm.Instance.SetStatus("已新建格式「" + cfg.Name + "」");
        }

        void EditConfig(CardConfig cfg)
        {
            if (cfg == null) return;
            // 内置格式也先拷一份：ConfigEditor 里点保存才回写，取消不能留痕
            CardConfig copy = ConfigEditor.CopyOf(cfg);
            if (!ConfigEditor.Edit(FindForm(), copy)) return;

            Configs.Save(copy);
            Configs.Active = copy;
            RefreshFormatCard();
            Rebuild();
            MainForm.Instance.SetStatus("已保存格式「" + copy.Name + "」");
        }

        void DeleteConfig()
        {
            CardConfig cfg = Configs.Active;
            if (Configs.IsBuiltin(cfg.Id))
            {
                Dlg.Info(FindForm(), "内置格式", "",
                    "内置格式不允许删除。改坏了可以用「恢复默认」还原。");
                return;
            }
            bool ok = Dlg.Confirm(FindForm(), "删除输出格式",
                "确定删除「" + cfg.Name + "」吗？已经用它做过的卡片不受影响。", "删除", "取消", true);
            if (!ok) return;

            Configs.Delete(cfg.Id);
            RefreshFormatCard();
            Rebuild();
            MainForm.Instance.SetStatus("已删除格式「" + cfg.Name + "」");
        }

        void ResetConfig()
        {
            CardConfig cfg = Configs.Active;
            if (!Configs.IsBuiltin(cfg.Id)) return;

            bool ok = Dlg.Confirm(FindForm(), "恢复默认",
                "把「" + cfg.Name + "」还原成出厂设置？你对它的改动会丢掉。", "恢复", "取消", false);
            if (!ok) return;

            Configs.Reset(cfg.Id);
            RefreshFormatCard();
            Rebuild();
            MainForm.Instance.SetStatus("已恢复「" + cfg.Name + "」的默认设置");
        }

        // ---------------------------------------------------------------- 3. 同步

        void BuildSync()
        {
            var c = new CardBuilder(_host, Sections[2]);
            c.Tip("卡片写进本机 Anki 桌面端，再由 Anki 自己同步到 AnkiWeb。" +
                  "这里设置用哪个 AnkiConnect 端口，以及要不要顺手帮 Anki 同步一次。");

            c.Label("AnkiConnect 地址");
            _endpointBox = c.Input(false);
            _endpointBox.TextChanged += delegate
            {
                if (_loading) return;
                string t = _endpointBox.Text.Trim();
                if (t.Length > 0) AnkiConn.Endpoint = t;   // 空值不写：逐字删除时别把地址清成空
            };
            c.Hint("必须先在 Anki 里装 AnkiConnect 插件（编号 " + AnkiConn.AddonCode +
                   "），装完重启 Anki。");

            c.Gap(10);
            c.Button("立即连接检查", false, delegate { CheckNow(); });

            c.Gap(10);
            _lamp = c.Lamp();

            c.Gap(6);
            _autoSync = c.Check("保存卡片后自动让 Anki 同步一次", false);
            _autoSync.CheckedChanged += delegate { Store.SetBool("anki.autosync", _autoSync.Checked); };
            _autoCheck = c.Check("启动时自动检查连接", true);
            _autoCheck.CheckedChanged += delegate { Store.SetBool("anki.autocheck", _autoCheck.Checked); };

            c.Done();
        }

        void CheckNow()
        {
            SyncState.Refresh();   // 内部自己开线程查，查完会回调 MainForm.NotifySyncState
            MainForm.Instance.SetStatus("正在检查 Anki 连接…");
            RefreshSyncCard();

            // 查完的结果没人通知我，这里短轮询一会儿把状态栏文字刷新过来
            _syncWait = Environment.TickCount + 12000;
            if (_syncPoll == null)
            {
                _syncPoll = new Timer();
                _syncPoll.Interval = 400;
                _syncPoll.Tick += delegate
                {
                    RefreshSyncCard();
                    if (SyncState.Kind != SyncKind.Busy || Environment.TickCount > _syncWait)
                    {
                        _syncPoll.Stop();
                        MainForm.Instance.SetStatus(SyncState.StatusText(),
                            SyncState.Kind == SyncKind.Error);
                    }
                };
            }
            _syncPoll.Start();
        }

        void RefreshSyncCard()
        {
            if (_lamp == null) return;
            if (SyncState.Kind == SyncKind.Busy) _lamp.State = 0;
            else if (SyncState.Kind == SyncKind.Ok) _lamp.State = 1;
            else _lamp.State = 2;

            _lamp.Text = SyncState.StatusText();
            _lamp.Invalidate();
        }

        // ---------------------------------------------------------------- 4. 制卡习惯

        void BuildHabit()
        {
            var c = new CardBuilder(_host, Sections[3]);
            c.Tip("连着做很多张时能省几次点击。");

            _clear = c.Check("保存成功后清空输入，方便接着做下一张", true);
            _clear.CheckedChanged += delegate { Store.SetBool("create.clear", _clear.Checked); };
            _autoPreview = c.Check("AI 填充完成后自动切到预览", false);
            _autoPreview.CheckedChanged += delegate { Store.SetBool("create.autopreview", _autoPreview.Checked); };

            c.Done();
        }

        // ---------------------------------------------------------------- 5. 皮肤

        void BuildSkin()
        {
            var c = new CardBuilder(_host, Sections[4]);
            c.Tip("换一套配色。深色皮肤会把整个界面切成暗色。");

            c.Gap(6);
            _skinName = c.DotRow("");
            _skinDot = c.LastDot;

            c.Gap(12);
            c.Button("选择皮肤", false, delegate { PickSkin(); });

            c.Done();
        }

        void RefreshSkin()
        {
            if (_skinDot == null) return;
            Theme t = Theme.Current;
            if (t == null) t = Theme.Get(Store.ThemeId);
            _skinDot.DotColor = t.Accent;
            _skinDot.Invalidate();
            _skinName.Text = t.Name + (t.Dark ? "（深色）" : "（浅色）");
            _skinName.Invalidate();
        }

        /// <summary>进头像卡片时先说清楚现在这张是哪来的。</summary>
        void RefreshAvatarMsg()
        {
            if (_avatarMsg == null) return;
            string src = Store.Get("avatar.source", "");
            if (src == "custom") AvatarMsg("现在用的是自己选的那张（在 Anki 媒体库里，会跟着同步）。", Ui.SUB);
            else if (src == "gravatar") AvatarMsg("现在用的是 Gravatar 上那张。", Ui.SUB);
            else if (AvatarStore.Email.Length == 0) AvatarMsg("还没填邮箱 —— 填了才能用 Gravatar。", Ui.AMBER);
            else AvatarMsg("Gravatar 上没有的话，角标就显示邮箱首字母。", Ui.SUB);
        }

        void PickSkin()
        {
            List<Theme> all = Theme.All();
            string cur = Store.ThemeId;
            string[] names = new string[all.Count];
            string[] notes = new string[all.Count];
            Color[] dots = new Color[all.Count];
            int sel = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Theme t = all[i];
                names[i] = t.Name;
                notes[i] = t.Dark ? "深色" : "浅色";
                dots[i] = t.Accent;
                if (t.Id == cur) sel = i;
            }

            int pick = Dlg.Choose(FindForm(), "皮肤", "换皮肤会保存并重建主窗口，没保存的输入会丢。",
                names, notes, sel, dots);
            if (pick < 0 || all[pick].Id == cur) return;

            ThemeSwap.Apply(all[pick].Id);   // 内部会写 Store.ThemeId 并重建 MainForm
        }

        // ---------------------------------------------------------------- 6. 头像

        void BuildAvatar()
        {
            var c = new CardBuilder(_host, Sections[5]);
            c.Tip("默认按邮箱从 Gravatar 取头像；也可以自己选一张，" +
                  "选完那张会写进 Anki 的媒体库，跟着 AnkiWeb 同步到手机 / 别的电脑。");

            c.Label("Gravatar / AnkiWeb 邮箱");
            _mailBox = c.Input(false);
            _mailBox.TextChanged += delegate
            {
                if (_loading) return;
                Store.Set("anki.profile", _mailBox.Text.Trim());
                Store.Set("avatar.email", "");   // 邮箱换了，旧缓存（Gravatar 那张）作废
                WaitMail();
            };
            c.Hint("只用来算头像（Gravatar 的公开算法），本程序不会把邮箱发到别处，" +
                   "也不需要 AnkiWeb 密码。");

            c.Gap(12);
            c.Row(32, 2);
            c.InRow(c.MakeButton("选择图片…", true, delegate { PickAvatarImage(); }), 8);
            c.InRow(c.MakeButton("换回 Gravatar", false, delegate { UseGravatar(); }), 0);

            c.Gap(6);
            _avatarMsg = c.Tip("");

            c.Done();
        }

        /// <summary>输入邮箱时别每敲一个字就去打一次 Gravatar，停手一会儿再解析。</summary>
        void WaitMail()
        {
            if (_mailWait == null)
            {
                _mailWait = new Timer();
                _mailWait.Interval = 800;
                _mailWait.Tick += delegate
                {
                    _mailWait.Stop();
                    RefreshAvatar();
                };
            }
            _mailWait.Stop();
            _mailWait.Start();
        }

        /// <summary>让顶栏角标按新的邮箱 / 缓存重解析一次。</summary>
        void RefreshAvatar()
        {
            if (MainForm.Instance != null) MainForm.Instance.RefreshAvatar();
        }

        /// <summary>头像卡片里那行反馈：成功绿、只存在本机琥珀、失败红。</summary>
        void AvatarMsg(string text, Color color)
        {
            if (_avatarMsg == null) return;
            _avatarMsg.Text = text;
            _avatarMsg.ForeColor = color;
            _avatarMsg.Invalidate();
        }

        void PickAvatarImage()
        {
            string path;
            using (var d = new OpenFileDialog())
            {
                d.Title = "选择头像图片";
                d.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" +
                           "|所有文件 (*.*)|*.*";
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = d.FileName;
            }

            try
            {
                // 拷一份再存：File 系列会让文件一直被占着，用户在原程序里可能还要用它
                using (Image raw = Image.FromFile(path))
                using (var copy = new Bitmap(raw))
                    AvatarBadge.SaveImage(copy);
            }
            catch (Exception ex)
            {
                AvatarMsg("这张图读不进来：" + ex.Message, Ui.RED);
                return;
            }

            RefreshAvatar();
            AvatarMsg("头像已换好，正在往 Anki 媒体库里写…", Ui.SUB);

            // 上传放后台：Anki 没开也不能卡住设置页，失败只在那行小字里说一声
            string why = null;
            Dlg.LastError = null;   // 静态字段，上一次的错不能算到这一次头上
            Dlg.Wait(FindForm(), "头像", "正在把头像写进 Anki 媒体库…", delegate
            {
                using (Image img = AvatarBadge.LoadImage())
                {
                    if (img == null) why = "本机那张读不回来了。";
                    else AvatarStore.Push(img, out why);
                }
            });
            if (why == null && Dlg.LastError != null) why = Dlg.LastError.Message;

            if (why == null) AvatarMsg("已存进 Anki 媒体库，会跟着 AnkiWeb 同步。", Ui.GREEN);
            else AvatarMsg("头像只存在本机：" + why, Ui.AMBER);
        }

        void UseGravatar()
        {
            bool ok = Dlg.Confirm(FindForm(), "换回默认头像",
                "本机这张会被删掉，Anki 媒体库里那张也一起删；" +
                "别的设备同步完也会回到 Gravatar（按上面的邮箱取）。",
                "换回默认", "取消", false);
            if (!ok) return;

            AvatarBadge.ClearImage();
            Store.Set("avatar.source", "");
            Store.Set("avatar.email", "");

            Dlg.Wait(FindForm(), "头像", "正在清媒体库里的旧头像…", delegate
            {
                AvatarStore.DeleteRemote();   // 删不掉也不影响本机换回默认
            });

            RefreshAvatar();
            MainForm.Instance.SetStatus("已换回 Gravatar 默认头像");
            AvatarMsg("已换回默认：有 Gravatar 就显示它，没有就显示字母。", Ui.GREEN);
        }

        // ---------------------------------------------------------------- 7. 关于

        void BuildAbout()
        {
            var c = new CardBuilder(_host, Sections[6]);
            _aboutVer = c.Text("Anki 助手 " + GitHub.Clean(GitHub.VersionTag), 11.5f, true);

            c.Tip("卡片通过 AnkiConnect 插件写进本机 Anki 桌面端；" +
                  "本程序不联网同步卡片，也不保存你的 Anki 密码。");

            c.Gap(10);
            c.Button("打开项目主页", false, delegate
            {
                try
                {
                    System.Diagnostics.Process.Start("https://github.com/" + GitHub.Owner + "/" + GitHub.Repo);
                }
                catch (Exception ex) { Dlg.Info(FindForm(), "打不开浏览器", "", ex.Message); }
            });

            c.Gap(8);
            c.Hint("第三方组件：AnkiConnect（Anki 插件，MIT 许可，编号 " + AnkiConn.AddonCode +
                   "）；本程序只通过它的本机 HTTP 接口读写卡片。");

            c.Done();
        }

        // ---------------------------------------------------------------- 8. 更新内容

        void BuildUpdates()
        {
            var c = new CardBuilder(_host, Sections[7]);
            _updateVer = c.Text("当前版本 " + GitHub.VersionTag, 9f, false);

            c.Gap(10);
            c.Row(32, 2);
            c.InRow(c.MakeButton("查看更新内容", true, delegate { ShowChangelog(); }), 8);
            _checkBtn = c.MakeButton("检查更新", false, delegate { CheckUpdate(); });
            c.InRow(_checkBtn, 0);

            c.Gap(10);
            _updateMsg = c.Text("点「检查更新」去 GitHub 看看有没有新版本。", 9f, false);

            c.Done();
        }

        void UpdateMsg(string text, Color color)
        {
            if (_updateMsg == null) return;
            _updateMsg.Text = text;
            _updateMsg.ForeColor = color;
            _updateMsg.Invalidate();
        }

        void ShowChangelog()
        {
            string local = GitHub.Changelog.Local();
            if (string.IsNullOrEmpty(local))
            {
                Dlg.Info(FindForm(), "更新内容", "", "本地没有更新日志。可以点「检查更新」从 GitHub 拉取。");
                return;
            }
            string section = GitHub.Changelog.SectionOf(local, GitHub.VersionTag);
            if (string.IsNullOrEmpty(section)) section = local;
            Dlg.Markdown(FindForm(), "更新内容", "当前版本 " + GitHub.VersionTag, section);
        }

        void CheckUpdate()
        {
            _checkBtn.Enabled = false;
            UpdateMsg("正在检查更新…", Ui.SUB);
            MainForm.Instance.SetStatus("正在检查更新…");

            Dlg.Wait(FindForm(), "检查更新", "正在查询 GitHub 上的最新版本…", delegate
            {
                GitHub.Release rel = null;
                string err = null;
                try { rel = GitHub.LatestRelease(); }
                catch (Exception ex) { err = GitHub.Friendly(ex); }

                BeginInvokeOnUi(delegate
                {
                    _checkBtn.Enabled = true;
                    if (err != null)
                    {
                        UpdateMsg("检查更新失败：" + err, Ui.RED);
                        MainForm.Instance.SetStatus("检查更新失败", true);
                        return;
                    }
                    if (rel.Same(GitHub.VersionTag))
                    {
                        UpdateMsg("已经是最新版本 " + GitHub.VersionTag, Ui.GREEN);
                        MainForm.Instance.SetStatus("已是最新版本");
                        Dlg.Info(FindForm(), "检查更新", "", "已经是最新版本");
                        return;
                    }
                    string extra = string.IsNullOrEmpty(rel.Name) ? "" : " · " + rel.Name;
                    UpdateMsg("发现新版本 " + rel.Tag + extra, Ui.ACCENT);
                    MainForm.Instance.SetStatus("发现新版本 " + rel.Tag);

                    bool go = Dlg.Confirm(FindForm(), "发现新版本 " + rel.Tag, rel.Name,
                        "要现在下载安装吗？", "下载", "稍后", false);
                    if (!go) return;
                    if (string.IsNullOrEmpty(rel.SetupUrl)) OpenPage(rel.PageUrl);
                    else DownloadSetup(rel);
                });
            });
        }

        void OpenPage(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Dlg.Info(FindForm(), "打不开浏览器", "", ex.Message); }
        }

        void DownloadSetup(GitHub.Release rel)
        {
            string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AnkiAssistant-Setup.exe");
            _checkBtn.Enabled = false;
            UpdateMsg("正在下载 " + rel.Tag + "…", Ui.ACCENT);
            var busy = new BusyWindow(FindForm(), "下载 " + rel.Tag);

            Dlg.Wait(FindForm(), "下载更新", "正在下载 " + rel.Tag + "…", delegate
            {
                string err = null;
                try
                {
                    GitHub.Download(rel.SetupUrl, outPath, delegate(long done, long total)
                    {
                        busy.Report(done, total);
                    });
                }
                catch (Exception ex) { err = GitHub.Friendly(ex); }

                BeginInvokeOnUi(delegate
                {
                    busy.Close();
                    _checkBtn.Enabled = true;
                    if (err != null)
                    {
                        UpdateMsg("下载失败：" + err, Ui.RED);
                        MainForm.Instance.SetStatus("下载更新失败", true);
                        Dlg.Info(FindForm(), "下载失败", "", err);
                        return;
                    }
                    UpdateMsg("下载完成，正在打开安装程序…", Ui.GREEN);
                    MainForm.Instance.SetStatus("下载完成，正在打开安装程序…");
                    try { System.Diagnostics.Process.Start(outPath); }
                    catch (Exception ex)
                    {
                        Dlg.Info(FindForm(), "打不开安装程序", "", outPath + "\n" + ex.Message);
                    }
                });
            });
        }

        // ================================================================ 辅助

        /// <summary>后台线程回 UI 线程的统一入口（本视图还没建句柄时直接忽略）。</summary>
        void BeginInvokeOnUi(MethodInvoker act)
        {
            try
            {
                if (IsHandleCreated) BeginInvoke(act);
                else act();
            }
            catch { }
        }

        // ================================================================ 索引联动

        void ScrollTo(int i, bool byClick)
        {
            if (i < 0 || i >= Sections.Length) return;
            _host.ScrollTo(_cardY[i] - Ui.Px(2));
            if (byClick) Select(i);
        }

        void Spy()
        {
            if (_host == null || _cardY == null) return;
            // 照抄安卓版：取滚动位置往下 40dp 的那条线，落在哪张卡片里就是哪一栏
            int line = _host.ScrollY + Ui.Px(40);
            int best = 0;
            for (int i = 0; i < Sections.Length; i++)
                if (_cardY[i] <= line) best = i;
            Select(best);
        }

        void Select(int i)
        {
            if (i == _sel) return;
            _sel = i;
            for (int k = 0; k < _index.Length; k++) _index[k].Selected = (k == i);
        }

        // ================================================================ 对外

        /// <summary>切到这一页时刷新：版本号、格式信息、连接状态、皮肤、各输入框。</summary>
        public void Activate()
        {
            // 第一次显示时右侧八张卡片一张都还没建（Rebuild 过去只在用户改格式时才被调用），
            // 不先建出来的话 _keyBox / _thinking / _endpointBox… 全是 null，
            // 灌值那段会被 NullReferenceException 打断、又被 catch 吞掉，内容区就一直是空白。
            if (!_built) Rebuild();
            else LoadValues();
        }

        /// <summary>把 Store / 运行时的值灌回各控件，并刷新格式、皮肤、连接状态。</summary>
        void LoadValues()
        {
            try
            {
                if (_aboutVer != null) _aboutVer.Text = "Anki 助手 " + GitHub.Clean(GitHub.VersionTag);
                if (_updateVer != null) _updateVer.Text = "当前版本 " + GitHub.VersionTag;

                string preset = Store.Get("ai.provider", AiClient.DefaultPreset);
                _loading = true;
                _keyBox.Text = Store.Get("ai.key", "");
                _modelBox.Text = Store.Get("ai.model", AiClient.PresetModel(preset));
                _urlBox.Text = Store.Get("ai.url", AiClient.PresetBaseUrl(preset));
                _thinking.Checked = Store.GetBool("ai.thinking", false);
                _endpointBox.Text = AnkiConn.Endpoint;
                _autoSync.Checked = Store.GetBool("anki.autosync", false);
                _autoCheck.Checked = Store.GetBool("anki.autocheck", true);
                _clear.Checked = Store.GetBool("create.clear", true);
                if (_autoPreview != null) _autoPreview.Checked = Store.GetBool("create.autopreview", false);
                if (_mailBox != null) _mailBox.Text = Store.Get("anki.profile", "");
                _loading = false;

                RefreshProviderBtn();
                RefreshFormatCard();
                RefreshSyncCard();
                RefreshSkin();
                RefreshAvatarMsg();
                Spy();
            }
            catch { }
            finally { _loading = false; }
        }

        /// <summary>内容区宽度变了：卡片里的控件都是绝对坐标，必须整页重排。</summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!_built || _host == null || _loading) return;
            // 只比「内容宽度」：大屏再多出来的是留白（MaxContentWidth 限宽 + 居中），
            // 而且拖动窗口时每一像素都重建一次整页会让界面卡死。
            if (ContentW() != _builtW) Rebuild();
        }

        int ContentW()
        {
            ScrollHost h = _host;                     // _host 是属性，先落到局部变量再取字段（否则 CS1690）
            if (h == null) return 0;
            Padding pad = h.BodyPadding;
            int w = h.ClientSize.Width - pad.Left - pad.Right;
            if (h.MaxContentWidth > 0 && w > h.MaxContentWidth) w = h.MaxContentWidth;
            return w;
        }

        protected override void Dispose(bool disposing)
        {
            // Timer 不停掉的话，换肤重建窗口后会有一堆死掉的 tick 继续跑
            if (disposing)
            {
                if (_spy != null) { _spy.Stop(); _spy.Dispose(); _spy = null; }
                if (_syncPoll != null) { _syncPoll.Stop(); _syncPoll.Dispose(); _syncPoll = null; }
                if (_mailWait != null) { _mailWait.Stop(); _mailWait.Dispose(); _mailWait = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ==================================================================== 左栏索引项

    /// <summary>左栏栏目名：选中时主色淡底 + 1px 主色描边（对齐安卓版 styleIndex）。</summary>
    public class IndexItem : Control
    {
        bool _sel;
        public bool Selected
        {
            get { return _sel; }
            set { if (_sel != value) { _sel = value; Invalidate(); } }
        }
        bool _hover;

        public IndexItem(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.CARD;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.CARD))
                g.FillRectangle(b, ClientRectangle);

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Selected)
            {
                Ui.FillRound(g, box, Ui.Px(10), Ui.ACCENT_SOFT);
                Ui.StrokeRound(g, box, Ui.Px(10), Ui.ACCENT, 1f);
            }
            else if (_hover)
            {
                Ui.FillRound(g, box, Ui.Px(10), Theme.Alpha(Ui.ACCENT, 18));
            }

            Font f = Ui.F(Selected ? 10f : 9.5f, Selected);
            Ui.TextVC(g, Text, f, Selected ? Ui.ACCENT : Ui.SUB,
                new Rectangle(Ui.Px(11), 0, Width - Ui.Px(16), Height));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // ==================================================================== 自绘小件

    /// <summary>白底圆角卡片。</summary>
    public class CardBox : Card
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Card 的 Fill/Border 是构造时快照的，这里每次重绘都重新取 Ui.*，
            // 换肤后旧卡片才不会挂着上一套颜色。
            Fill = Ui.CARD;
            Border = Ui.LINE;
            base.OnPaintBackground(e);
        }
    }

    /// <summary>一个色点，用来表示当前皮肤的主色。</summary>
    public class SkinDot : Control
    {
        public Color DotColor = Color.Gray;

        public SkinDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.CARD;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.CARD))
                g.FillRectangle(b, ClientRectangle);
            int d = Math.Min(Width, Height) - Ui.Px(2);
            if (d < 2) return;
            using (var b = new SolidBrush(DotColor))
                g.FillEllipse(b, (Width - d) / 2, (Height - d) / 2, d, d);
        }
    }

    /// <summary>连接状态小圆点 + 一行说明。绿=已连接，红=没连上，灰=正在检查。</summary>
    public class AnkiLamp : Control
    {
        public int State;   // 0 检查中 / 1 已连接 / 2 未连接

        public AnkiLamp()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.CARD;
        }

        Color DotColor()
        {
            if (State == 1) return Ui.GREEN;
            if (State == 2) return Ui.RED;
            return Ui.SUB;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.CARD))
                g.FillRectangle(b, ClientRectangle);

            int d = Math.Max(Ui.Px(8), Math.Min(Height - Ui.Px(6), Ui.Px(10)));
            int cy = Height / 2;
            using (var b = new SolidBrush(DotColor()))
                g.FillEllipse(b, 0, cy - d / 2, d, d);

            int left = d + Ui.Px(8);
            Ui.TextVC(g, Text, Ui.F(9f), Ui.TEXT_BODY,
                new Rectangle(left, 0, Math.Max(1, Width - left), Height));
        }
    }

    // ==================================================================== 可滚动内容区

    /// <summary>
    /// 只竖滚的内容容器：卡片宽度跟着面板走，永远不出横向滚动条。
    ///
    /// 不用 Panel.AutoScroll 的原因：它按子控件的 Anchor/Dock 算总高，
    /// 卡片一改宽就容易冒出横向条，滚动位置也不好像素级控制。
    /// </summary>
    public class ScrollHost : Control
    {
        readonly List<Control> _cards = new List<Control>();
        readonly Panel _body;
        int _scroll, _contentH, _thumbH, _thumbTop, _dragOffset;
        bool _dragging, _trackHot;

        public int Gap = 14;                        // 卡片间距（dp）
        public Padding BodyPadding = new Padding(16, 14, 16, 16);
        public int MaxContentWidth;                 // >0 时限宽 + 居中

        public event EventHandler ScrollChanged;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.BG;
            // 自己管滚动位置：不用 Control.AutoScroll（它是只读属性，也按 Anchor/Dock
            // 算总高，卡片一改宽就容易冒出横向条）。

            _body = new Panel();
            _body.BackColor = Ui.BG;
            Controls.Add(_body);

            MouseDown += OnTrackDown;
            MouseMove += OnTrackMove;
            MouseUp += delegate { _dragging = false; };
        }

        public int ContentHeight { get { return _contentH; } }
        public int ScrollY { get { return _scroll; } }
        public int MaxScroll { get { return Math.Max(0, _contentH - ClientSize.Height); } }

        public void Add(Control c)
        {
            if (c == null || _cards.Contains(c)) return;
            if (c.Parent != _body) _body.Controls.Add(c);
            _cards.Add(c);
        }

        public void ClearContent()
        {
            _cards.Clear();
            _body.Controls.Clear();
            _contentH = 0;
            _scroll = 0;
            _body.Top = 0;
            _trackH = 0;
        }

        /// <summary>按当前宽度摆好所有卡片，算出内容总高和滚动条。</summary>
        public void Recalc()
        {
            int x = BodyPadding.Left;
            int w = ClientSize.Width - BodyPadding.Left - BodyPadding.Right;
            if (MaxContentWidth > 0 && w > MaxContentWidth)
            {
                w = MaxContentWidth;
                x = (ClientSize.Width - w) / 2;   // 大屏居中，别缩在左边一条
            }
            if (w <= 0) return;

            Ui.Native.Freeze(_body);
            try
            {
                int y = BodyPadding.Top;
                for (int i = 0; i < _cards.Count; i++)
                {
                    Control c = _cards[i];
                    c.SetBounds(x, y, w, Math.Max(1, c.Height));
                    y += c.Height + Ui.Px(Gap);
                }
                _contentH = _cards.Count > 0
                    ? y - Ui.Px(Gap) + BodyPadding.Bottom
                    : BodyPadding.Bottom;
                _body.SetBounds(0, 0, ClientSize.Width, Math.Max(_contentH, ClientSize.Height));
            }
            finally { Ui.Native.Unfreeze(_body); }

            if (_scroll > MaxScroll) _scroll = MaxScroll;
            _body.Top = -_scroll;
            ComputeThumb();
            Invalidate();
        }

        public void ScrollTo(int y)
        {
            int max = MaxScroll;
            if (y < 0) y = 0;
            if (y > max) y = max;
            if (y != _scroll)
            {
                _scroll = y;
                _body.Top = -_scroll;
                ComputeThumb();
                Invalidate();
                if (ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaxScroll > 0)
                ScrollTo(_scroll + (e.Delta > 0 ? -Ui.Px(70) : Ui.Px(70)));
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalc();
        }

        // -------- 自绘滚动条 --------

        int _trackH;

        void ComputeThumb()
        {
            int trackH = ClientSize.Height - Ui.Px(12);
            if (MaxScroll <= 0 || trackH < Ui.Px(40)) { _trackH = 0; return; }
            _trackH = Math.Max(Ui.Px(30), (int)((long)trackH * ClientSize.Height / Math.Max(1, _contentH)));
            _thumbH = _trackH;
            _thumbTop = Ui.Px(6) + (trackH - _trackH) * _scroll / Math.Max(1, MaxScroll);
        }

        int TrackX() { return ClientSize.Width - Ui.Px(10); }

        void OnTrackDown(object s, MouseEventArgs e)
        {
            if (_trackH <= 0 || e.X < TrackX()) return;
            if (e.Y >= _thumbTop && e.Y <= _thumbTop + _trackH)
            {
                _dragging = true;
                _dragOffset = e.Y - _thumbTop;
            }
            else
            {
                ScrollTo(_scroll + (e.Y < _thumbTop ? -1 : 1) * ClientSize.Height * 4 / 5);
            }
        }

        void OnTrackMove(object s, MouseEventArgs e)
        {
            if (!_dragging || _trackH <= 0) return;
            int room = ClientSize.Height - Ui.Px(12) - _trackH;
            int max = MaxScroll;
            if (room <= 0 || max <= 0) return;
            int y = e.Y - _dragOffset - Ui.Px(6);
            if (y < 0) y = 0;
            if (y > room) y = room;
            ScrollTo(y * max / room);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_trackH <= 0) return;
            // 底槽用极淡的一层，滑块的对比度才够
            Ui.FillRound(e.Graphics,
                new Rectangle(TrackX() + Ui.Px(3), Ui.Px(6), Ui.Px(4), ClientSize.Height - Ui.Px(12)),
                Ui.Px(2), _trackHot ? Ui.LINE : Ui.BG);
            Ui.FillRound(e.Graphics,
                new Rectangle(TrackX() + Ui.Px(3), _thumbTop, Ui.Px(4), _trackH),
                Ui.Px(2), Ui.SUB);
        }

        protected override void OnMouseEnter(EventArgs e) { _trackHot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _trackHot = false; _dragging = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // ==================================================================== 卡片排版器

    /// <summary>
    /// 往一张卡片里顺序摆控件的小工具。
    ///
    /// 登记时用"1 份宽度"当坐标系（宽度 1.0 = 卡片内容宽度），宽度定下来后
    /// <see cref="Layout"/> 一次性换算成像素；宽度再变就重算一遍。
    /// 全部绝对坐标：卡片宽度是 ScrollHost 每帧算出来的，用 Dock/FlowLayout
    /// 反而要跟它们的布局时机打架。
    /// </summary>
    public class CardBuilder
    {
        const double Fill = -2;

        class Entry
        {
            public Control C;
            public double X, Y, W, H;
            public int Align;      // 0=左 1=居中
            public int Slot, Slots;   // Slots>0 表示这是一组并排元素
            public double RowGap;
        }

        readonly ScrollHost _host;
        readonly CardBox _card;
        readonly List<Entry> _list = new List<Entry>();
        readonly int _pad;
        int _innerGuess;           // 内容宽度估值（像素）：算折行高度要用，宽度定下来后会更新
        double _y;

        int _rowTopY, _rowH;       // 当前行的 Y / 高（1 份坐标系）
        int _rowSlot, _rowSlots;
        bool _inRow;

        public SkinDot LastDot;

        public CardBuilder(ScrollHost host, string title)
        {
            _host = host;
            _card = new CardBox();
            _card.Radius = 14;
            _card.Fill = Ui.CARD;
            _card.Border = Ui.LINE;
            _card.ShowBorder = true;
            _card.BackColor = Ui.CARD;
            _pad = Ui.Px(16);
            _card.Height = Ui.Px(80);
            _host.Add(_card);

            _innerGuess = Ui.Px(560) - _pad * 2;

            var t = new Label();
            t.Text = title;
            t.Font = Ui.F(11.5f, true);
            t.ForeColor = Ui.INK;
            t.TextAlign = ContentAlignment.MiddleLeft;
            _card.Controls.Add(t);
            var e = new Entry();
            e.C = t; e.X = 0; e.W = 1.0; e.H = Ui.Px(26); e.Y = _y; e.Align = 0;
            _list.Add(e);
            _y += Ui.Px(26) + Ui.Px(4);
        }

        int InnerGuess { get { return _innerGuess > 40 ? _innerGuess : 40; } }

        Entry Reg(Control c, double x, double w, int hPx, int align)
        {
            var e = new Entry();
            e.C = c; e.X = x; e.W = w; e.H = hPx; e.Y = _y; e.Align = align;
            _list.Add(e);
            _card.Controls.Add(c);
            return e;
        }

        // -------- 组件 --------

        /// <summary>说明文字（灰色小字，自动折行）。</summary>
        public Label Tip(string text)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(9f);
            l.ForeColor = Ui.SUB;
            l.TextAlign = ContentAlignment.TopLeft;
            Entry e = Reg(l, 0, 1.0, 0, 0);
            _y += MeasureWrap(text, l.Font, InnerGuess) + Ui.Px(2);
            return l;
        }

        /// <summary>小灰字提示，比 Tip 再小一号。</summary>
        public Label Hint(string text)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(8.5f);
            l.ForeColor = Ui.TEXT_DIM;
            l.TextAlign = ContentAlignment.TopLeft;
            Entry e = Reg(l, 0, 1.0, 0, 0);
            _y += MeasureWrap(text, l.Font, InnerGuess) + Ui.Px(2);
            return l;
        }

        /// <summary>一行文本（版本号、皮肤名之类）。bold 决定字重。</summary>
        public Label Text(string text, float pt, bool bold)
        {
            _y += Ui.Px(5);
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(pt, bold);
            l.ForeColor = Ui.INK;
            l.TextAlign = ContentAlignment.MiddleLeft;
            Reg(l, 0, 1.0, Ui.Px(24), 0);
            _y += Ui.Px(24);
            return l;
        }

        /// <summary>字段标签。</summary>
        public Label Label(string text)
        {
            _y += Ui.Px(11);
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(9f);
            l.ForeColor = Ui.SUB;
            l.TextAlign = ContentAlignment.MiddleLeft;
            Reg(l, 0, 1.0, Ui.Px(20), 0);
            _y += Ui.Px(20) + Ui.Px(2);
            return l;
        }

        public void Gap(int dp) { _y += Ui.Px(dp); }

        public Input Input(bool secret)
        {
            var t = new Input();
            t.Font = Ui.F(9.5f);
            t.Secret = secret;
            Reg(t, 0, 1.0, Ui.Px(34), 0);
            _y += Ui.Px(34) + Ui.Px(6);
            return t;
        }

        /// <summary>勾选框。高度按文字实际行数算，长说明才不会被切掉。</summary>
        public CheckBox Check(string text, bool on)
        {
            _y += Ui.Px(7);
            var cb = new CheckBox();
            cb.Text = text;
            cb.Font = Ui.F(9f);
            cb.ForeColor = Ui.TEXT_BODY;
            cb.BackColor = Ui.CARD;
            cb.FlatStyle = FlatStyle.System;
            cb.Checked = on;
            Entry e = Reg(cb, 0, 1.0, 0, 0);
            _y += Math.Max(Ui.Px(24), MeasureWrap(text, cb.Font, InnerGuess - Ui.Px(26)));
            return cb;
        }

        /// <summary>整行宽的按钮。</summary>
        public Pill Button(string text, bool primary, EventHandler onClick)
        {
            var p = MakeButton(text, primary, onClick);
            Reg(p, 0, 1.0, Ui.Px(32), 0);
            _y += Ui.Px(32);
            return p;
        }

        /// <summary>只造不摆：留给 Row / InRow 并排用。</summary>
        public Pill MakeButton(string text, bool primary, EventHandler onClick)
        {
            var p = new Pill();
            p.Text = text;
            p.Primary = primary;
            p.Font = Ui.F(9f);
            p.Cursor = Cursors.Hand;
            p.Click += onClick;
            return p;
        }

        /// <summary>起一行并排元素（高度按 dp，格子在 InRow 里分配）。</summary>
        public void Row(int heightDp, int slots)
        {
            if (slots < 1) slots = 1;
            _rowTopY = (int)Math.Round(_y);
            _rowH = Ui.Px(heightDp);
            _rowSlot = 0;
            _rowSlots = slots;
            _inRow = true;
            _y += _rowH;
        }

        /// <summary>把按钮放进当前行的下一个格子，gap 是格间缝（dp）。</summary>
        public void InRow(Pill p, int gapDp)
        {
            if (!_inRow) { Button(p.Text, p.Primary, null); return; }
            AddInRow(p, Ui.Px(30), gapDp, false);
        }

        void AddInRow(Control c, int hPx, int gapDp, bool fixedSize)
        {
            var e = new Entry();
            e.C = c;
            e.Y = _rowTopY;
            e.H = fixedSize ? Ui.Px(16) : hPx;
            e.Slot = _rowSlot;
            e.Slots = _rowSlots;
            e.RowGap = Ui.Px(gapDp);
            e.W = Fill;
            _list.Add(e);
            _card.Controls.Add(c);
            _rowSlot++;
        }

        /// <summary>色点 + 一行文字，用于皮肤卡片。</summary>
        public Label DotRow(string text)
        {
            var dot = new SkinDot();
            dot.DotColor = Ui.ACCENT;
            AddInRow(dot, Ui.Px(16), 8, true);
            LastDot = dot;

            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(10.5f);
            l.ForeColor = Ui.INK;
            l.TextAlign = ContentAlignment.MiddleLeft;
            AddInRow(l, Ui.Px(24), 0, false);
            return l;
        }

        /// <summary>连接状态圆点 + 一行说明。</summary>
        public AnkiLamp Lamp()
        {
            var l = new AnkiLamp();
            l.Text = "";
            Reg(l, 0, 1.0, Ui.Px(24), 0);
            _y += Ui.Px(24);
            return l;
        }

        /// <summary>收尾：算出卡片总高、把尺寸摊到每个控件上，再让 ScrollHost 重排。</summary>
        public void Done()
        {
            // 卡片刚 new 出来还是 Panel 的默认宽度（200），拿它算 inner 会把整张卡的内容
            // 排成左侧一条窄柱。先让 ScrollHost 按当前宽度把卡片摆一次（顺带限宽 + 居中），
            // 拿到真实宽度再排版。
            _host.Recalc();
            int inner = _card.Width - _pad * 2 - Ui.Px(2);
            if (inner < 40) inner = InnerGuess;
            Layout(inner);
            _host.Recalc();   // 高度算完了，卡片的纵向位置和内容总高得再摆一次
        }

        // -------- 摆位 --------

        int ColWidth(int slot, int slots, double gapDp)
        {
            int gap = (int)Math.Round(gapDp);
            int usable = _innerGuess - gap * (slots - 1);
            if (usable < slots) usable = slots;
            return usable / slots;
        }

        int ColLeft(int slot, int slots, double gapDp)
        {
            int gap = (int)Math.Round(gapDp);
            return _pad + slot * (ColWidth(slot, slots, gapDp) + gap);
        }

        void Layout(int inner)
        {
            _innerGuess = inner;
            int innerTotalH = 0;

            for (int i = 0; i < _list.Count; i++)
            {
                Entry e = _list[i];
                int w = e.Slots > 0
                    ? ColWidth(e.Slot, e.Slots, e.RowGap)
                    : (e.W == Fill ? inner : (int)Math.Round(e.W * inner));
                if (w < 1) w = 1;

                int h;
                if (e.H > 0) h = (int)Math.Round(e.H);
                else
                {
                    // 高度跟着宽度走：文字换行数变了，高度自然要变
                    int aw = w;
                    if (e.C is CheckBox) aw = w - Ui.Px(26);
                    h = MeasureWrap(e.C.Text, e.C.Font, aw);
                }
                if (h < 1) h = 1;

                int x;
                if (e.Slots > 0)
                {
                    x = ColLeft(e.Slot, e.Slots, e.RowGap);
                    // 最后一个格子吃掉余数，避免并排按钮之间差 1px
                    if (e.Slot == e.Slots - 1)
                    {
                        int right = _pad + inner - Ui.Px(2);
                        w = Math.Max(1, right - x);
                    }
                }
                else
                {
                    x = _pad + (int)Math.Round(e.X * inner);
                    if (e.Align == 1) x = _pad + (inner - w) / 2;
                }

                int y = (int)Math.Round(e.Y);
                int boxTop = y;
                if (e.Slots > 0) boxTop = y + Math.Max(0, (_rowH - h) / 2);

                e.C.SetBounds(x, boxTop, w, h);
                int bottom = boxTop + h;
                if (e.Slots == 0 && bottom > innerTotalH) innerTotalH = bottom;
                if (e.Slots > 0 && y + _rowH > innerTotalH) innerTotalH = y + _rowH;
            }

            _card.Height = innerTotalH + Ui.Px(16);
            _card.Invalidate();
        }

        /// <summary>按控件字体量一段文字折行后要多少像素高。</summary>
        static int MeasureWrap(string text, Font f, int widthPx)
        {
            if (string.IsNullOrEmpty(text)) return Ui.Px(18);
            if (widthPx < 20) widthPx = 20;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                string[] lines = Ui.Wrap(g, text, f, widthPx);
                if (lines == null || lines.Length == 0) return Ui.Px(18);
                int lh = (int)Math.Ceiling(g.MeasureString("国Ag", f).Height);
                if (lh < Ui.Px(14)) lh = Ui.Px(14);
                return lh * lines.Length + Ui.Px(2);
            }
        }
    }

    // ==================================================================== 下载进度窗

    /// <summary>
    /// 下载进度小窗。Dlg.Wait 的 job 跑在后台线程、动不了 UI，
    /// 所以进度条单独开一个非模态窗口，靠 BeginInvoke 回 UI 线程刷。
    /// </summary>
    public class BusyWindow
    {
        readonly Form _form;
        readonly ProgressBar _bar;
        readonly Label _sub;
        readonly IWin32Window _owner;

        public BusyWindow(IWin32Window owner, string title)
        {
            _owner = owner;
            _form = new Form();
            _form.Text = "Anki 助手";
            _form.FormBorderStyle = FormBorderStyle.None;
            _form.StartPosition = FormStartPosition.CenterParent;
            _form.BackColor = Ui.CARD;
            _form.ShowInTaskbar = false;
            _form.Font = Ui.F(9f);
            _form.ClientSize = new Size(Ui.Px(360), Ui.Px(116));
            _form.Paint += delegate(object s, PaintEventArgs e)
            {
                Ui.StrokeRound(e.Graphics,
                    new Rectangle(0, 0, _form.ClientSize.Width - 1, _form.ClientSize.Height - 1),
                    Ui.Px(10), Ui.LINE);
            };

            var t = new Label();
            t.Text = title;
            t.Font = Ui.F(10.5f, true);
            t.ForeColor = Ui.INK;
            t.BackColor = Color.Transparent;
            t.SetBounds(Ui.Px(20), Ui.Px(16), Ui.Px(320), Ui.Px(24));

            _sub = new Label();
            _sub.Text = "准备下载…";
            _sub.ForeColor = Ui.SUB;
            _sub.BackColor = Color.Transparent;
            _sub.SetBounds(Ui.Px(20), Ui.Px(42), Ui.Px(320), Ui.Px(22));

            _bar = new ProgressBar();
            _bar.Maximum = 100;
            _bar.SetBounds(Ui.Px(20), Ui.Px(76), Ui.Px(320), Ui.Px(14));

            _form.Controls.Add(t);
            _form.Controls.Add(_sub);
            _form.Controls.Add(_bar);
            _form.Show(owner);
        }

        /// <summary>从后台线程调，自己转回 UI 线程。</summary>
        public void Report(long done, long total)
        {
            if (_form == null || _form.IsDisposed) return;
            try
            {
                _form.BeginInvoke((MethodInvoker)delegate
                {
                    if (_form.IsDisposed) return;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    _bar.Value = pct;
                    _sub.Text = total > 0
                        ? "已下载 " + (done / 1024) + " / " + (total / 1024) + " KB（" + pct + "%）"
                        : "已下载 " + (done / 1024) + " KB";
                    if (MainForm.Instance != null)
                        MainForm.Instance.SetStatus("正在下载更新… " + pct + "%");
                });
            }
            catch { }
        }

        public void Close()
        {
            try { _form.Close(); _form.Dispose(); }
            catch { }
        }
    }
}
