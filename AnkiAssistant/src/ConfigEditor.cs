using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 输出格式编辑器：名称 / 笔记类型 / 牌组 / 标签 / 学科背景 / 提示词 / 系统提示词 / 字段列表。
    ///
    /// 字段太多，Dlg.Form 那套"标签 + 输入框"竖直排布装不下，所以自己排版：
    /// 上面是基础信息与提示词，下面是可滚动的字段表（每行一个新字段）。
    /// 只改传进来的 cfg 副本，用户点「保存」才由调用方 Configs.Save 落盘。
    /// </summary>
    public class ConfigEditor : DlgForm
    {
        // 字段行的列宽（dp）
        const int WName = 110, WKey = 110, WHint = 180, WLatex = 52;
        const int RowH = 32, RowGap = 6;

        readonly CardConfig _cfg;

        Panel _fieldsOuter;
        Panel _rowsHost;
        readonly List<Panel> _rowPanels = new List<Panel>();

        // 基础信息
        Input _name, _noteType, _deck, _tags, _subject;
        Input _prompt, _sysPrompt;
        Label _hintDefaults, _hintPrompt;

        public bool Result;

        public ConfigEditor(IWin32Window owner, CardConfig cfg)
            : base(cfg != null && Configs.IsBuiltin(cfg.Id) ? "编辑输出格式" : "编辑输出格式",
                   cfg != null && Configs.IsBuiltin(cfg.Id)
                       ? "内置格式可以改，保存后会记为「已覆盖」；Id 不允许改。"
                       : "自定义格式，随便改。",
                   820)
        {
            _cfg = cfg;
            BuildBasic();
            BuildFieldsArea();

            // 底部按钮：保存(主色) / 取消。DialogResult 由基类 Button() 内部设置，点完自己 Close。
            // 注意 Pill 既不是 IButtonControl 也没有 DialogResult 属性，别往 AcceptButton 里塞（会 CS0266）。
            Button("保存", true, DialogResult.OK);
            Button("取消", false, DialogResult.Cancel);

            _pad = Ui.Px(16);
            Load += delegate { DoLayout(); };
        }

        int _pad;

        // ================================================================ 上面：基础信息 + 提示词

        Panel _basic;

        void BuildBasic()
        {
            _basic = new Panel();
            _basic.BackColor = Ui.CARD;
            _basic.AutoScroll = true;
            Body.Controls.Add(_basic);

            int y = 0;
            _name = AddInput(_basic, "名称", ref y, 420, false);
            _noteType = AddInput(_basic, "笔记类型（Anki 里的 Note Type）", ref y, 420, false);
            _deck = AddInput(_basic, "默认牌组", ref y, 420, false);
            _tags = AddInput(_basic, "默认标签", ref y, 420, false);
            _hintDefaults = AddNote(_basic, ref y,
                "牌组 / 标签 / 学科背景留空 = 用应用级默认值（" +
                CardConfig.AppDefaultDeck + " · " + CardConfig.AppDefaultTags + "）。");
            _subject = AddInput(_basic, "学科背景（会替换提示词里的 {subject}）", ref y, 620, false);

            y += Ui.Px(12);
            _prompt = AddMulti(_basic, "提示词模板", ref y, 110, true);
            _hintPrompt = AddNote(_basic, ref y,
                "占位符：{word} 换成本次要查的词，{subject} 换成上面的学科背景。");
            _sysPrompt = AddMulti(_basic, "系统提示词", ref y, 96, false);
        }

        Input AddInput(Control host, string label, ref int y, int widthDp, bool secret)
        {
            var l = new Label();
            l.Text = label;
            l.Font = Ui.F(9f);
            l.ForeColor = Ui.SUB;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(Ui.Px(2), y, Ui.Px(widthDp), Ui.Px(20));
            host.Controls.Add(l);
            y += Ui.Px(20) + Ui.Px(2);

            var t = new Input();
            t.Font = Ui.F(9.5f);
            t.Secret = secret;
            t.SetBounds(Ui.Px(2), y, Ui.Px(widthDp), Ui.Px(34));
            host.Controls.Add(t);
            y += Ui.Px(34) + Ui.Px(6);
            return t;
        }

        Input AddMulti(Control host, string label, ref int y, int heightDp, bool withPreview)
        {
            var l = new Label();
            l.Text = label;
            l.Font = Ui.F(9f);
            l.ForeColor = Ui.SUB;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(Ui.Px(2), y, Ui.Px(200), Ui.Px(20));
            host.Controls.Add(l);

            if (withPreview)
            {
                var pv = new Pill();
                pv.Text = "看拼出来的提示词";
                pv.Font = Ui.F(8.5f);
                pv.Cursor = Cursors.Hand;
                pv.SetBounds(Ui.Px(120), y - Ui.Px(1), Ui.Px(150), Ui.Px(22));
                pv.Click += delegate { ShowPreview(); };
                host.Controls.Add(pv);
            }
            y += Ui.Px(20) + Ui.Px(2);

            var t = new Input(true, false);
            t.Font = Ui.F(9.5f);
            t.SetBounds(Ui.Px(2), y, Ui.Px(660), Ui.Px(heightDp));
            host.Controls.Add(t);
            y += Ui.Px(heightDp) + Ui.Px(6);
            return t;
        }

        Label AddNote(Control host, ref int y, string text)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Ui.F(8.5f);
            l.ForeColor = Ui.TEXT_DIM;
            l.TextAlign = ContentAlignment.TopLeft;
            l.SetBounds(Ui.Px(2), y, Ui.Px(660), Ui.Px(18));
            host.Controls.Add(l);
            y += Ui.Px(18) + Ui.Px(2);
            return l;
        }

        // ================================================================ 下面：字段表

        void BuildFieldsArea()
        {
            _fieldsOuter = new Panel();
            _fieldsOuter.BackColor = Ui.CARD;
            _fieldsOuter.AutoScroll = true;
            Body.Controls.Add(_fieldsOuter);

            var head = new Label();
            head.Text = "字段（第 1 个字段 = 卡片正面，必须保留，不能删也不能改 AI 键）";
            head.Font = Ui.F(9f, true);
            head.ForeColor = Ui.INK;
            head.TextAlign = ContentAlignment.MiddleLeft;
            head.SetBounds(0, 0, Ui.Px(660), Ui.Px(22));
            _fieldsOuter.Controls.Add(head);

            _rowsHost = new Panel();
            _rowsHost.BackColor = Ui.CARD;
            _fieldsOuter.Controls.Add(_rowsHost);

            BuildRows();
        }

        /// <summary>按 _cfg.Fields 重建所有字段行。</summary>
        void BuildRows()
        {
            _rowsHost.Controls.Clear();
            _rowPanels.Clear();

            int y = Ui.Px(28);
            for (int i = 0; i < _cfg.Fields.Count; i++)
            {
                Panel row = MakeRow(i);
                row.SetBounds(0, y, Ui.Px(WName + WKey + WHint + WLatex + 190), Ui.Px(RowH));
                _rowsHost.Controls.Add(row);
                _rowPanels.Add(row);
                y += Ui.Px(RowH + RowGap);
            }

            var add = new Pill();
            add.Text = "＋ 新增字段";
            add.Font = Ui.F(9f);
            add.Cursor = Cursors.Hand;
            add.SetBounds(0, y, Ui.Px(120), Ui.Px(28));
            add.Click += delegate
            {
                _cfg.Fields.Add(new Field("新字段", "key" + (_cfg.Fields.Count), "", false));
                BuildRows();
                DoLayout();
            };
            _rowsHost.Controls.Add(add);

            _rowsHost.Height = y + Ui.Px(34);
            // 行比可视区宽时靠 AutoScroll 横向滚，别把列挤变形
            int total = Ui.Px(WName + WKey + WHint + WLatex + 200);
            _rowsHost.Width = Math.Max(total, _fieldsOuter.ClientSize.Width);
        }

        Panel MakeRow(int i)
        {
            var row = new Panel();
            row.BackColor = Ui.CARD;
            Field f = _cfg.Fields[i];
            bool first = (i == 0);

            var tName = RowInput(row, 0, WName, f.Name);
            var tKey = RowInput(row, WName, WKey, f.Key);
            var tHint = RowInput(row, WName + WKey, WHint, f.Hint);

            var latex = new CheckBox();
            latex.Text = "公式";
            latex.Font = Ui.F(8.5f);
            latex.ForeColor = Ui.TEXT_BODY;
            latex.BackColor = Ui.CARD;
            latex.FlatStyle = FlatStyle.System;
            latex.Checked = f.Latex;
            latex.SetBounds(Ui.Px(WName + WKey + WHint + 4), Ui.Px(6), Ui.Px(WLatex), Ui.Px(20));
            row.Controls.Add(latex);

            // 行内控件是显示态：改完就把值写回 cfg，免得换行时丢改动。
            // 注意不能捕获循环变量 i —— C# 5 的 for 变量是同一个实例，
            // 所有行的事件最后都会指向最后一行。用行控件反查下标最稳。
            tHint.TextChanged += delegate { int k = RowIndex(row); if (k >= 0) _cfg.Fields[k].Hint = tHint.Text; };
            tName.TextChanged += delegate { int k = RowIndex(row); if (k > 0) _cfg.Fields[k].Name = tName.Text; };
            tKey.TextChanged += delegate { int k = RowIndex(row); if (k > 0) _cfg.Fields[k].Key = tKey.Text.Trim(); };
            latex.CheckedChanged += delegate { int k = RowIndex(row); if (k > 0) _cfg.Fields[k].Latex = latex.Checked; };
            tName.Leave += delegate { CommitRow(RowIndex(row), tName, tKey, tHint, latex.Checked); };
            tKey.Leave += delegate { CommitRow(RowIndex(row), tName, tKey, tHint, latex.Checked); };

            // 第一行是卡片正面：锁住名字与 AI 键，只留"提示"
            if (first)
            {
                tName.ReadOnly = true;
                tKey.ReadOnly = true;
                tName.BackColor = Ui.CARD;
                tKey.BackColor = Ui.CARD;

                var front = new Label();
                front.Text = "卡片正面";
                front.Font = Ui.F(8.5f);
                front.ForeColor = Ui.ACCENT;
                front.TextAlign = ContentAlignment.MiddleLeft;
                front.SetBounds(Ui.Px(WName + WKey + WHint + WLatex + 14), Ui.Px(6), Ui.Px(70), Ui.Px(20));
                row.Controls.Add(front);
            }
            else
            {
                // 每次点击现查下标：上移/删除会重建所有行，闭包里的旧下标会失效
                int bx = WName + WKey + WHint + WLatex + 14;
                AddRowButton(row, "↑", bx, delegate
                {
                    int k = RowIndex(row);
                    if (k <= 0 || k >= _cfg.Fields.Count) return;
                    Field tmp = _cfg.Fields[k];
                    _cfg.Fields[k] = _cfg.Fields[k - 1];
                    _cfg.Fields[k - 1] = tmp;
                    BuildRows();
                    DoLayout();
                });
                AddRowButton(row, "↓", bx + 30, delegate
                {
                    int k = RowIndex(row);
                    if (k < 0 || k >= _cfg.Fields.Count - 1) return;
                    Field tmp = _cfg.Fields[k];
                    _cfg.Fields[k] = _cfg.Fields[k + 1];
                    _cfg.Fields[k + 1] = tmp;
                    BuildRows();
                    DoLayout();
                });
                AddRowButton(row, "删", bx + 60, delegate
                {
                    int k = RowIndex(row);
                    if (k <= 0 || k >= _cfg.Fields.Count) return;   // 第 1 个字段是卡片正面，不给删
                    _cfg.Fields.RemoveAt(k);
                    BuildRows();
                    DoLayout();
                });
            }
            return row;
        }

        /// <summary>字段行 → 字段下标。找不到返回 -1（重建后旧行已经不在表里）。</summary>
        int RowIndex(Panel row)
        {
            return _rowPanels.IndexOf(row);
        }

        void CommitRow(int i, Input n, Input k, Input h, bool? latex)
        {
            if (i < 0 || i >= _cfg.Fields.Count) return;
            Field f = _cfg.Fields[i];
            if (i > 0)
            {
                f.Name = n.Text.Trim().Length == 0 ? f.Name : n.Text.Trim();
                string key = k.Text.Trim();
                if (key.Length == 0) key = "key" + i;
                f.Key = key;
                if (latex.HasValue) f.Latex = latex.Value;
            }
            f.Hint = h.Text;
        }

        Input RowInput(Control row, int xDp, int wDp, string value)
        {
            var t = new Input();
            t.Font = Ui.F(9.5f);
            t.PadX = 6;
            t.PadY = 4;
            t.Text = value;
            t.SetBounds(Ui.Px(xDp), Ui.Px(3), Ui.Px(wDp - 8), Ui.Px(26));
            row.Controls.Add(t);
            return t;
        }

        void AddRowButton(Control row, string text, int xDp, EventHandler onClick)
        {
            var b = new Pill();
            b.Text = text;
            b.Font = Ui.F(8.5f);
            b.Cursor = Cursors.Hand;
            b.SetBounds(Ui.Px(xDp), Ui.Px(4), Ui.Px(26), Ui.Px(24));
            b.Click += onClick;
            row.Controls.Add(b);
        }

        void ShowPreview()
        {
            try
            {
                string subj = _subject.Text.Trim();
                string full = _cfg.BuildPrompt("friction", _cfg.SubjectOr(subj));
                if (string.IsNullOrEmpty(full)) full = "（这个格式的提示词模板是空的）";
                string head = full.Length > 400 ? full.Substring(0, 400) + "\n…（后面还有 " +
                              (full.Length - 400) + " 个字符）" : full;
                Dlg.Info(this, "提示词预览", "把 {word} 填成 friction 拼出来的效果（截前 400 字）",
                    "```\n" + head + "\n```", 300);
            }
            catch (Exception ex) { Dlg.Info(this, "预览失败", "", ex.Message); }
        }

        // ================================================================ 布局 / 保存

        /// <summary>
        /// 基类只按 BodyWidth 摆横向，纵向得自己算：
        /// 上面基础信息 + 中间提示词 = 可滚的 Fill 区，下面字段表固定高（也自己滚）。
        /// </summary>
        void DoLayout()
        {
            int pad = Ui.Px(_pad);
            int foot = Ui.Px(58), head = Head.Height;
            int area = Screen.FromControl(this).WorkingArea.Height;
            int maxH = (int)(area * 0.78) - head - foot;
            int want = Ui.Px(760);
            int bodyH = Math.Min(want, Math.Max(Ui.Px(420), maxH));

            ClientSize = new Size(ClientSize.Width, head + bodyH + foot);
            Body.Padding = new Padding(pad, Ui.Px(10), pad, Ui.Px(12));

            int innerW = Body.ClientSize.Width - Body.Padding.Horizontal;
            int fieldsH = Ui.Px(250);

            // Dock 的顺序：先加 Fill 再加 Bottom，Fill 才会让位给 Bottom
            _basic.SetBounds(0, 0, innerW, Body.ClientSize.Height - fieldsH);
            _basic.Height = Math.Max(Ui.Px(120), Body.ClientSize.Height - fieldsH - Ui.Px(8));
            _fieldsOuter.SetBounds(0, _basic.Height + Ui.Px(8), innerW, fieldsH - Ui.Px(8));

            foreach (Control c in _basic.Controls)
            {
                if (c is Input && ((Input)c).Multiline)
                    c.Width = Math.Max(Ui.Px(240), innerW - Ui.Px(4));
                else if (c is Label && ((Label)c).Font.Size <= 8.6f)
                    c.Width = Math.Max(Ui.Px(200), innerW - Ui.Px(4));
            }

            int total = Ui.Px(WName + WKey + WHint + WLatex + 200);
            _rowsHost.Width = Math.Max(total, _fieldsOuter.ClientSize.Width - Ui.Px(4));
            _fieldsOuter.AutoScrollMinSize = new Size(
                Math.Max(total, _fieldsOuter.ClientSize.Width - Ui.Px(4)), _rowsHost.Height + Ui.Px(28));
            _rowsHost.Top = Ui.Px(28);

            _hintDefaults.Height = Ui.Px(18);
            _hintPrompt.Height = Ui.Px(18);
        }

        /// <summary>把界面上的值写回 _cfg（点「保存」时调，取消就不写）。</summary>
        bool Store()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0)
            {
                Dlg.Info(this, "名称不能为空", "", "给这个格式起个名字，切换的时候好认。");
                return false;
            }
            if (_cfg.Fields.Count == 0)
            {
                Dlg.Info(this, "至少要有一个字段", "", "第 1 个字段是卡片正面，不能全删掉。");
                return false;
            }

            // 字段行里没来得及 Commit 的（比如直接在文本框里改完就点保存）这里补一次
            for (int i = 0; i < _rowPanels.Count && i < _cfg.Fields.Count; i++)
            {
                Panel row = _rowPanels[i];
                var boxes = new List<Input>();
                foreach (Control c in row.Controls) if (c is Input) boxes.Add((Input)c);
                if (boxes.Count < 3) continue;
                CheckBox lx = null;
                foreach (Control c in row.Controls) if (c is CheckBox) { lx = (CheckBox)c; break; }
                CommitRow(i, boxes[0], boxes[1], boxes[2], lx == null ? (bool?)null : lx.Checked);
            }

            _cfg.Name = name;
            _cfg.NoteType = _noteType.Text.Trim().Length == 0 ? _cfg.NoteType : _noteType.Text.Trim();
            _cfg.DefaultDeck = _deck.Text.Trim();
            _cfg.DefaultTags = _tags.Text.Trim();
            _cfg.Subject = _subject.Text.Trim();
            _cfg.Prompt = _prompt.Text;
            _cfg.SystemPrompt = _sysPrompt.Text;
            // Id 不动：内置格式的 Id 是认格式用的；自建格式的 Id 是落盘文件名
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (!Store()) { e.Cancel = true; return; }
                Result = true;
            }
            base.OnFormClosing(e);
        }

        // ================================================================ 静态入口

        /// <summary>打开编辑器。返回 true 表示用户点了保存（此时 cfg 已被改好，等调用方落盘）。</summary>
        public static bool Edit(IWin32Window owner, CardConfig cfg)
        {
            if (cfg == null) return false;
            using (var d = new ConfigEditor(owner, cfg))
            {
                d.Icon = null;
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        /// <summary>深拷一份格式：编辑内置格式时也先拷，用户取消就不能留痕。</summary>
        public static CardConfig CopyOf(CardConfig src)
        {
            if (src == null) return null;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                CardConfig c = CardConfig.FromJson(
                    (Dictionary<string, object>)ser.DeserializeObject(ser.Serialize(src)));
                if (c != null) return c;
            }
            catch { }

            // 兜底：手工拷。字段必须逐个 new，否则两个格式会共用同一个 Field 对象
            var n = new CardConfig();
            n.Id = src.Id;
            n.Name = src.Name;
            n.NoteType = src.NoteType;
            n.Css = src.Css;
            n.SystemPrompt = src.SystemPrompt;
            n.Prompt = src.Prompt;
            n.DefaultDeck = src.DefaultDeck;
            n.DefaultTags = src.DefaultTags;
            n.Subject = src.Subject;
            foreach (Field f in src.Fields)
                n.Fields.Add(new Field(f.Name, f.Key, f.Hint, f.Latex));
            return n;
        }
    }
}
