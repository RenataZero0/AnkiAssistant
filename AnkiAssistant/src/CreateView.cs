using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>
    /// 制卡页：输入一个词 → AI 按当前「输出格式」填字段 → 存进 Anki。
    ///
    /// 布局自上而下：输入卡（单词 + 清空/AI 填充）、编辑/预览切换、
    /// 字段区（编辑）或卡片预览（预览）、底部牌组/标签/保存。
    /// </summary>
    public class CreateView : Panel
    {
        // 数据
        CardConfig _cfg;
        List<Input> _boxes = new List<Input>();     // 与 _cfg.Fields 一一对应（0 号是单词框）
        Dictionary<string, object> _lastAi;            // 最近一次 AI 解析结果（重填时用）

        // 控件
        Card _topCard, _bottomCard;
        Panel _center;
        ScrollHost _editPane;
        List<Card> _fieldCards = new List<Card>();  // 字段卡片（重建时统一 Dispose）
        MarkdownView _preview;
        Input _word, _deck, _tags;
        Label _labWord, _hintWord;      // 顶部「单词」块的字段名 / 提示（跟着当前格式走）
        Pill _tabEdit, _tabPreview, _bFill, _bClear, _bSave, _bDraft, _bDeck;
        Label _aiLine;
        int _tab;             // 0 编辑 1 预览
        bool _busy;

        public CreateView()
        {
            BackColor = Ui.BG;
            Ui.EnableDoubleBuffer(this);

            _topCard = new Card { Dock = DockStyle.Top, Height = Ui.Px(110) };
            _center = new Panel { Dock = DockStyle.Fill, BackColor = Ui.BG };
            _bottomCard = new Card { Dock = DockStyle.Bottom, Height = Ui.Px(122) };

            BuildTopCard();
            BuildCenter();
            BuildBottomCard();

            Controls.Add(_center);
            Controls.Add(_bottomCard);
            Controls.Add(_topCard);
        }

        // ===== 顶部输入卡 =====
        // 和下面「音标 / 中文 / 例句」保持同构：整块卡片 + 字段名写在块内左上角，
        // 值在名字右侧/下方。这里因为「单词」是卡片正面，输入框给得比别的字段更大更醒目。
        void BuildTopCard()
        {
            _labWord = new Label
            {
                Text = "单词",
                AutoSize = true,
                Font = Ui.F(9f),
                ForeColor = Ui.SUB,
                BackColor = Color.Transparent,
                Location = new Point(Ui.Px(14), Ui.Px(10))
            };
            _topCard.Controls.Add(_labWord);

            _hintWord = new Label
            {
                Text = "（你输入的词就是卡片正面）",
                AutoSize = true,
                Font = Ui.F(8.5f),
                ForeColor = Ui.TEXT_DIM,
                BackColor = Color.Transparent,
                Location = new Point(Ui.Px(14), Ui.Px(12))
            };
            _topCard.Controls.Add(_hintWord);

            _word = new Input(true, false);
            _word.Font = Ui.F(12f, true);
            _word.WordWrap = false;
            _word.Scrollbars = ScrollBars.None;   // 单词框不需要滚动条（多行 + 不折行会画出个挤扁的箭头）
            _word.PadX = 9;
            _word.PadY = 5;
            _word.Location = new Point(Ui.Px(18), Ui.Px(30));
            _word.Size = new Size(Ui.Px(560), Ui.Px(34));
            _word.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; FillWithAi(); }
                if (e.KeyCode == Keys.S && e.Control) Save();
            };
            _topCard.Controls.Add(_word);

            _bClear = new Pill { Text = "清空", Font = Ui.F(9.5f) };
            _bClear.Size = new Size(Pill.Measure("清空", _bClear.Font) + Ui.Px(18), Ui.Px(34));
            _bClear.Click += delegate { ClearAll(); };
            _topCard.Controls.Add(_bClear);

            _bFill = new Pill { Text = "AI 填充", Primary = true, Font = Ui.F(9.5f, true) };
            _bFill.Size = new Size(Pill.Measure("AI 填充", _bFill.Font) + Ui.Px(20), Ui.Px(34));
            _bFill.Click += delegate { FillWithAi(); };
            _topCard.Controls.Add(_bFill);

            _aiLine = new Label
            {
                AutoSize = false,
                Font = Ui.F(9f),
                ForeColor = Ui.ACCENT,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(Ui.Px(14), Ui.Px(76)),
                Size = new Size(Ui.Px(420), Ui.Px(22))
            };
            _aiLine.Click += delegate
            {
                if (MainForm.Instance != null) MainForm.Instance.ShowPage(2);   // 去设置里换 AI
            };
            _topCard.Controls.Add(_aiLine);

            _topCard.Resize += delegate { LayoutTopCard(); };
        }

        void LayoutTopCard()
        {
            int w = _topCard.Width;
            int right = w - Ui.Px(20);
            int row = Ui.Px(30);                        // 输入框和「清空 / AI 填充」同排
            _bFill.Location = new Point(right - _bFill.Width, row);
            _bClear.Location = new Point(_bFill.Left - Ui.Px(10) - _bClear.Width, row);
            int wordW = Math.Max(Ui.Px(180), _bClear.Left - Ui.Px(10) - Ui.Px(18));   // 18 = 输入框左边距
            _word.Size = new Size(wordW, Ui.Px(34));
            _hintWord.Location = new Point(_labWord.Right + Ui.Px(8), Ui.Px(12));   // 提示紧跟字段名
        }

        // ===== 中间：编辑 / 预览 =====
        void BuildCenter()
        {
            var tabs = new Panel { Dock = DockStyle.Top, Height = Ui.Px(46), BackColor = Ui.BG };
            // 选中态必须跟当前皮肤的主色走：Pill.Primary → Ui.ACCENT（默认蓝 / 深色皮肤另一套色），
            // 未选中态走 Pill 的中性浅底。不用 Pill.Toggle 的选中样式，免得再回到写死的颜色上。
            _tabEdit = new Pill { Text = "编辑", Primary = true, Font = Ui.F(9.5f) };
            _tabPreview = new Pill { Text = "预览", Font = Ui.F(9.5f) };
            _tabEdit.Size = new Size(Ui.Px(120), Ui.Px(32));
            _tabPreview.Size = new Size(Ui.Px(120), Ui.Px(32));
            _tabEdit.Location = new Point(0, Ui.Px(6));
            _tabPreview.Location = new Point(Ui.Px(128), Ui.Px(6));
            _tabEdit.Click += delegate { SetTab(0); };
            _tabPreview.Click += delegate { SetTab(1); };
            tabs.Controls.Add(_tabEdit);
            tabs.Controls.Add(_tabPreview);

            // 原来是 Panel.AutoScroll —— 那会露出系统那条带灰色箭头的经典滚动条。
            // 换成自绘的 ScrollHost：同样的细条，跟设置页共用一套。
            _editPane = new ScrollHost();
            _editPane.Dock = DockStyle.Fill;
            _editPane.BackColor = Ui.BG;
            _editPane.Gap = 8;
            _editPane.BodyPadding = new Padding(0, 0, Ui.Px(12), Ui.Px(4));

            _preview = new MarkdownView { Dock = DockStyle.Fill, Visible = false, BackColor = Ui.CARD };
            var prevCard = new Card { Dock = DockStyle.Fill };
            prevCard.Padding = new Padding(Ui.Px(18), Ui.Px(16), Ui.Px(18), Ui.Px(16));
            prevCard.Controls.Add(_preview);
            prevCard.Visible = false;
            _previewHost = prevCard;

            _center.Controls.Add(_editPane);
            _center.Controls.Add(prevCard);
            _center.Controls.Add(tabs);
        }

        Card _previewHost;

        void SetTab(int i)
        {
            _tab = i;
            _tabEdit.Primary = i == 0;
            _tabPreview.Primary = i == 1;
            _tabEdit.Invalidate();
            _tabPreview.Invalidate();
            _editPane.Visible = i == 0;
            _previewHost.Visible = i == 1;
            if (i == 1) RefreshPreview();
        }

        // ===== 底部：牌组 / 标签 / 保存 =====
        void BuildBottomCard()
        {
            var labDeck = new Label { Text = "牌组", AutoSize = true, Font = Ui.F(9f), ForeColor = Ui.SUB, BackColor = Color.Transparent, Location = new Point(Ui.Px(20), Ui.Px(12)) };
            var labTags = new Label { Text = "标签（随牌组自动填充）", AutoSize = true, Font = Ui.F(9f), ForeColor = Ui.SUB, BackColor = Color.Transparent, Location = new Point(Ui.Px(20), Ui.Px(12)) };
            _bottomCard.Controls.Add(labDeck);
            _bottomCard.Controls.Add(labTags);
            _labDeck = labDeck;
            _labTags = labTags;

            _deck = new Input();
            _deck.Font = Ui.F(10f);
            _deck.PadX = 10;
            _deck.Location = new Point(Ui.Px(20), Ui.Px(36));
            _deck.Size = new Size(Ui.Px(300), Ui.Px(32));
            _bottomCard.Controls.Add(_deck);

            _bDeck = new Pill { Text = "选择牌组", Font = Ui.F(9.5f) };
            _bDeck.Size = new Size(Pill.Measure("选择牌组", _bDeck.Font) + Ui.Px(16), Ui.Px(30));
            _bDeck.Click += delegate { ChooseDeck(); };
            _bottomCard.Controls.Add(_bDeck);

            _tags = new Input();
            _tags.Font = Ui.F(10f);
            _tags.PadX = 10;
            _tags.Location = new Point(Ui.Px(20), Ui.Px(36));
            _tags.Size = new Size(Ui.Px(300), Ui.Px(32));
            _bottomCard.Controls.Add(_tags);

            _bSave = new Pill { Text = "保存到 Anki", Primary = true, Font = Ui.F(10f, true) };
            _bSave.Size = new Size(Ui.Px(200), Ui.Px(38));
            _bSave.Click += delegate { Save(); };
            _bottomCard.Controls.Add(_bSave);

            _bDraft = new Pill { Text = "存草稿", Font = Ui.F(10f) };
            _bDraft.Size = new Size(Ui.Px(130), Ui.Px(38));
            _bDraft.Click += delegate { SaveDraft(); };
            _bottomCard.Controls.Add(_bDraft);

            _bottomCard.Resize += delegate { LayoutBottomCard(); };
        }

        Label _labDeck, _labTags;

        void LayoutBottomCard()
        {
            int w = _bottomCard.Width;
            int half = (w - Ui.Px(40) - Ui.Px(14)) / 2;
            int tagsX = Ui.Px(20) + half + Ui.Px(14);
            _labTags.Location = new Point(tagsX, Ui.Px(12));
            _deck.Size = new Size(Math.Max(Ui.Px(140), half - _bDeck.Width - Ui.Px(8)), Ui.Px(32));
            _bDeck.Location = new Point(Ui.Px(20) + half - _bDeck.Width, Ui.Px(38));
            _tags.Location = new Point(tagsX, Ui.Px(36));
            _tags.Size = new Size(half, Ui.Px(32));

            int y = Ui.Px(76);
            _bSave.Location = new Point(Ui.Px(20), y);
            _bSave.Size = new Size(Math.Max(Ui.Px(120), (w - Ui.Px(40)) / 2 - Ui.Px(6)), Ui.Px(36));
            _bDraft.Location = new Point(_bSave.Right + Ui.Px(12), y);
            _bDraft.Size = new Size(Math.Max(Ui.Px(90), (w - Ui.Px(40)) / 2 - Ui.Px(6)), Ui.Px(36));
        }

        // ===== 生命周期 =====
        /// <summary>切到这一页时调用：如果输出格式变了就重建字段区。</summary>
        public void Activate()
        {
            CardConfig cfg = Configs.Active;
            if (_cfg == null || _cfg.Id != cfg.Id || _boxes.Count != cfg.Fields.Count)
            {
                _cfg = cfg;
                BuildFields();
                _deck.Text = cfg.DeckOr(CardConfig.AppDefaultDeck);
                _tags.Text = cfg.TagsOr(CardConfig.AppDefaultTags);
            }
            RefreshAiLine();
            RefreshPreview();
        }

        void RefreshAiLine()
        {
            string id = AiClient.ActivePreset();
            string model = Store.Get("ai.model", AiClient.PresetModel(id));
            _aiLine.Text = "AI：" + AiClient.PresetLabel(id) +
                (string.IsNullOrEmpty(model) ? "" : "（" + model + "）") + "　·　点此更换";
        }

        /// <summary>按当前格式重建字段编辑区。</summary>
        void BuildFields()
        {
            _editPane.SuspendLayout();
            Ui.Native.Freeze(_editPane);
            foreach (Card old in _fieldCards)
            {
                if (old == null || old.IsDisposed) continue;
                old.Dispose();          // Dispose 会自己从父容器摘掉
            }
            _fieldCards.Clear();
            _editPane.ClearContent();
            _boxes.Clear();

            // 0 号字段就是顶部那个输入块（单词），这里只把字段名同步到顶部卡片的左上角，
            // 不再像以前那样另起一个标签吊在输入框底下。
            _labWord.Text = _cfg.Fields.Count > 0 ? _cfg.Fields[0].Name : "单词";
            _hintWord.Location = new Point(_labWord.Right + Ui.Px(8), Ui.Px(12));
            _boxes.Add(_word);

            for (int i = 1; i < _cfg.Fields.Count; i++)
            {
                Field f = _cfg.Fields[i];
                var c = new Card { Height = Ui.Px(84), ShowBorder = true };
                c.Paint += delegate(object s, PaintEventArgs e)
                {
                    Ui.Text(e.Graphics, f.Name, Ui.F(9f), Ui.SUB, Ui.Px(14), Ui.Px(8));
                    if (!string.IsNullOrEmpty(f.Hint))
                        Ui.Text(e.Graphics, f.Hint, Ui.F(8.5f), Ui.TEXT_DIM, Ui.Px(96), Ui.Px(9));
                };
                var tb = new Input(true, false);
                tb.Font = Ui.F(10f);
                tb.BoxFill = Ui.PANEL;
                tb.PadX = 10;
                tb.PadY = 5;
                // 字段框自己不带滚动条了：内容多了就让卡片长高，滚动交给整页。
                tb.Scrollbars = ScrollBars.None;
                tb.WordWrap = true;
                tb.Location = new Point(Ui.Px(14), Ui.Px(30));
                tb.Size = new Size(Ui.Px(400), Ui.Px(46));
                c.Controls.Add(tb);

                Card host = c;
                Input box = tb;
                box.TextChanged += delegate { GrowField(host, box); };
                host.Resize += delegate
                {
                    box.Size = new Size(Math.Max(Ui.Px(80), host.Width - Ui.Px(28)),
                                        Math.Max(Ui.Px(46), host.Height - Ui.Px(38)));
                };

                _editPane.Add(c);
                _fieldCards.Add(c);
                _boxes.Add(tb);
            }

            Ui.Native.Unfreeze(_editPane);
            _editPane.ResumeLayout();
            _editPane.Recalc();
        }

        /// <summary>字段框没滚动条了，文字换行变多就把它所在的卡片撑高。</summary>
        void GrowField(Card host, Input box)
        {
            if (host == null || box == null || box.IsDisposed || host.IsDisposed) return;
            int need = box.ContentHeight() + Ui.Px(42);
            if (need < Ui.Px(84)) need = Ui.Px(84);
            if (host.Height == need) return;
            host.Height = need;
            if (_editPane != null) _editPane.Recalc();
        }

        // ===== 预览 =====

        void RefreshPreview()
        {
            if (_cfg == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("### ").Append(Text0()).Append("\n\n");
            for (int i = 1; i < _cfg.Fields.Count && i < _boxes.Count; i++)
            {
                string v = _boxes[i].Text.Trim();
                if (v.Length == 0) continue;
                sb.Append("**").Append(_cfg.Fields[i].Name).Append("**　").Append(v).Append("\n\n");
            }
            if (sb.Length < 20) sb.Append("_（还没有内容，点「AI 填充」或自己写）_");
            _preview.Markdown = sb.ToString();
        }

        string Text0()
        {
            if (_cfg == null || _cfg.Fields.Count == 0) return "卡片";
            string t = _word.Text.Trim();
            return t.Length > 0 ? t : _cfg.Fields[0].Name;
        }

        // ===== 操作 =====
        void ClearAll()
        {
            for (int i = 1; i < _boxes.Count; i++) _boxes[i].Text = "";
            _word.Text = "";
            _word.Focus();
            RefreshPreview();
        }

        void ChooseDeck()
        {
            Dlg.Wait(this, "牌组", "正在读 Anki 里的牌组…", delegate
            {
                List<string> decks;
                try { decks = AnkiConn.DeckNames(); }
                catch { decks = new List<string>(); }
                if (decks.Count == 0) decks.Add(_deck.Text);
                BeginInvoke((MethodInvoker)delegate
                {
                    int idx = Dlg.Choose(this, "选择牌组", null, decks.ToArray(), null,
                        decks.IndexOf(_deck.Text));
                    if (idx >= 0)
                    {
                        _deck.Text = decks[idx];
                        if (string.IsNullOrEmpty(_tags.Text) || _tags.Text == _cfg.TagsOr(CardConfig.AppDefaultTags))
                            _tags.Text = CardFormat.TagsForDeck(decks[idx]);
                    }
                });
            });
        }

        /// <summary>AI 填充：严格提示词 → 宽松提示词 → 兜底解析。</summary>
        void FillWithAi()
        {
            if (_busy || _cfg == null) return;
            string word = _word.Text.Trim();
            if (word.Length == 0)
            {
                Dlg.Info(this, "还没有输入", null, "先在顶上那个框里写一个词吧。", 90);
                _word.Focus();
                return;
            }

            string id = AiClient.ActivePreset();
            string url = Store.Get("ai.url", AiClient.PresetBaseUrl(id));
            string key = Secret.Resolve(id, Store.Get("ai.key", ""));
            string model = Store.Get("ai.model", AiClient.PresetModel(id));
            bool thinking = Store.GetBool("ai.thinking", false);
            string subject = _cfg.SubjectOr(CardConfig.AppDefaultSubject);

            Dictionary<string, object> parsed = null;
            string used = "";
            string err = null;

            _busy = true;
            _bFill.Enabled = false;
            SetStatus("AI 正在填充…");

            Dlg.Wait(this, "AI 正在填充", "正在为「" + word + "」生成卡片内容，请稍候…", delegate
            {
                try
                {
                    AiReply r = AiClient.ChatDetailed(url, key, model, _cfg.SystemPrompt,
                        _cfg.BuildPromptStrict(word, subject), thinking);
                    try { parsed = CardFormat.ParseAi(r.Content); used = "严格"; }
                    catch
                    {
                        AiReply r2 = AiClient.ChatDetailed(url, key, model, _cfg.SystemPrompt,
                            _cfg.BuildPrompt(word, subject), thinking);
                        try { parsed = CardFormat.ParseAi(r2.Content); used = "宽松"; }
                        catch { parsed = CardFormat.FallbackFields(word, r2.Content); used = "兜底"; }
                    }
                }
                catch (Exception ex) { err = AnkiConn.Humanize(ex.Message); }
            });
            _busy = false;
            _bFill.Enabled = true;

            if (err != null)
            {
                SetStatus("AI 填充失败：" + err, true);
                Dlg.Info(this, "AI 填充失败", null, err, 150);
                return;
            }

            _lastAi = parsed;
            ApplyAi(parsed);
            SetStatus("AI 已填充（" + used + "解析）");
            if (Store.GetBool("create.autopreview", false)) SetTab(1);
        }

        void ApplyAi(Dictionary<string, object> ai)
        {
            if (ai == null || _cfg == null) return;
            for (int i = 1; i < _cfg.Fields.Count && i < _boxes.Count; i++)
            {
                Field f = _cfg.Fields[i];
                string v = Pick(ai, f.Name);
                if (v.Length == 0) v = Pick(ai, f.Key);
                if (v.Length == 0 && f.Name == "中文") v = Pick(ai, "中文释义");
                if (v.Length == 0) v = Pick(ai, "定义");
                _boxes[i].Text = v;
            }
            RefreshPreview();
        }

        static string Pick(Dictionary<string, object> d, string key)
        {
            if (d == null || string.IsNullOrEmpty(key)) return "";
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return "";
            return o.ToString().Trim();
        }

        void SetStatus(string s, bool warn = false)
        {
            if (MainForm.Instance != null) MainForm.Instance.SetStatus(s, warn);
        }

        // ===== 保存 =====
        Dictionary<string, string> CollectFields()
        {
            var note = new Dictionary<string, string>();
            for (int i = 0; i < _cfg.Fields.Count && i < _boxes.Count; i++)
            {
                Field f = _cfg.Fields[i];
                string v = _boxes[i].Text.Trim();
                if (string.IsNullOrEmpty(v)) v = " ";
                v = CardFormat.Escape(v);
                if (f.Latex) v = CardFormat.NormalizeFormula(v);
                note[f.Name] = v;
            }
            return note;
        }

        void Save()
        {
            if (_busy || _cfg == null) return;
            string word = _word.Text.Trim();
            if (word.Length == 0) { Dlg.Info(this, "还没有输入", null, "先在顶上那个框里写一个词吧。", 90); return; }
            string deck = _deck.Text.Trim();
            if (deck.Length == 0) deck = _cfg.DeckOr(CardConfig.AppDefaultDeck);
            var tags = new List<string>();
            foreach (string t in _tags.Text.Split(new char[] { ' ', ',', '，', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                tags.Add(t);

            Dictionary<string, string> fields = CollectFields();
            string model = _cfg.NoteType;
            string err = null;
            long added = 0;

            _busy = true;
            _bSave.Enabled = false;
            SetStatus("正在写入 Anki…");

            Dlg.Wait(this, "保存到 Anki", "正在把「" + word + "」写进牌组「" + deck + "」…", delegate
            {
                try
                {
                    SyncState.RequireConnected();
                    // 牌组名可以是用户手输的新名字 —— AnkiConnect 不会自动建牌组，
                    // 先补建一个，免得辛辛苦苦填好的卡因为一句英文报错白写。
                    AnkiConn.EnsureDeck(deck);
                    if (!AnkiConn.NoteTypeExists(model))
                    {
                        AnkiConn.CreateModel(model, new List<string>(_cfg.FieldNames()), _cfg.CardFront(), _cfg.CardBack(), _cfg.Css);
                    }
                    added = AnkiConn.AddNote(deck, model, fields, tags);
                    if (Store.GetBool("anki.autosync", false))
                    {
                        try { AnkiConn.Sync(); } catch { }
                    }
                }
                catch (Exception ex) { err = AnkiConn.Humanize(ex.Message); }
            });
            _busy = false;
            _bSave.Enabled = true;

            if (err != null)
            {
                SetStatus("保存失败：" + err, true);
                Dlg.Info(this, "保存失败", null, err, 170);
                return;
            }

            SetStatus("已保存「" + word + "」到「" + deck + "」");
            if (Store.GetBool("create.clear", false)) ClearAll();
        }

        // ===== 草稿 =====
        void SaveDraft()
        {
            try
            {
                string dir = Path.Combine(Store.DataDir, "drafts");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var o = new Dictionary<string, object>();
                o["word"] = _word.Text.Trim();
                o["config"] = _cfg.Id;
                o["saved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var fields = new Dictionary<string, string>();
                for (int i = 0; i < _cfg.Fields.Count && i < _boxes.Count; i++)
                    fields[_cfg.Fields[i].Name] = _boxes[i].Text;
                o["fields"] = fields;

                var js = new JavaScriptSerializer();
                string file = Path.Combine(dir, "draft-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                Store.WriteText(file, js.Serialize(o));
                SetStatus("草稿已存到 " + file);
            }
            catch (Exception ex)
            {
                Dlg.Info(this, "存草稿失败", null, ex.Message, 120);
            }
        }
    }
}
