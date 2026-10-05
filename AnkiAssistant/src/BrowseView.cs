using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 浏览页。
    ///
    /// 和 Android 版（BrowseView.java）行为一致：
    ///   · 不强加任何字段假设 —— 卡片可能来自任意笔记类型，所以列表标题取「第一个有内容的字段」，
    ///     详情把该笔记的**所有字段**按笔记类型里的顺序原样列出来
    ///   · 本机卡片走 AnkiConnect（Anki 必须开着），本地草稿走 data\drafts\*.json
    ///
    /// 界面用 WinForms 自绘（没有网页控件）：
    ///   · 列表和详情都是自己画 + 自己收滚轮，这样圆角、行高、深色皮肤才和别处一套
    ///   · 控件按需创建，Activate() 只刷数据不动结构（切页频繁，重建控件会闪）
    /// </summary>
    public class BrowseView : Panel
    {
        // ===== 常量 =====
        const int MaxNotes = 200;      // 一次最多列 200 张，再多让用户用搜索缩小范围
        const int RowH = 64;           // 列表行高
        const int TieBreak = 999;      // 字段顺序取不到时的兜底（和 AnkiConnect 返回的 order 对齐）

        // ===== 分段控件 =====
        Pill _segCards, _segDraft;

        // ===== 本机卡片：查询行 =====
        Panel _cardsTop;
        Select _deck;
        Input _search;
        Pill _queryBtn;
        bool _narrow;                  // 窄窗口：搜索框换到第二行

        // ===== 本机卡片：结果 =====
        Label _cardsStatus;
        CardList _list;
        Card _listCard;

        // ===== 详情 =====
        Panel _detail;
        DetailBody _body;
        Button _delBtn;
        AnkiConn.Note _current;

        // ===== 本地草稿 =====
        Panel _draftPane;
        Label _draftStatus;
        DraftList _drafts;

        // ===== 数据 =====
        List<string> _decks = new List<string>();
        List<NoteRow> _rows = new List<NoteRow>();     // noteList 可能被手动 Edit 成 null，所以同一批数据也留一份
        List<AnkiConn.Note> _notes = new List<AnkiConn.Note>();
        List<Draft> _draftData = new List<Draft>();
        string _selectedDeck = "";
        bool _tab;                     // false = 本机卡片，true = 本地草稿
        bool _busy;                    // 查询/删除进行中：按钮要禁用
        int _reqSeq;                   // 只认最后一次查询的结果，避免慢请求把新结果覆盖掉

        // ===== 子控件 =====

        /// <summary>卡片列表：自绘行 + 自己收滚轮。</summary>
        class CardList : Control
        {
            public readonly List<NoteRow> Rows = new List<NoteRow>();
            int _hover = -1;
            int _scroll, _contentH;

            public CardList()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Ui.CARD;
                Cursor = Cursors.Default;
            }

            /// <summary>内容变了要复位滚动位置，否则会停在上一次的偏移上。</summary>
            public void Reset()
            {
                _scroll = 0;
                _hover = -1;
                Invalidate();
            }

            int RowTop(int i) { return i * (Ui.Px(RowH) + 1) - _scroll; }

            int Hit(MouseEventArgs e)
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    int y = RowTop(i);
                    if (e.Y >= y && e.Y < y + Ui.Px(RowH)) return i;
                }
                return -1;
            }

            /// <summary>鼠标位置落在第几行（-1 = 没落在行上），外部靠它取该行数据。</summary>
            public int HitIndex(MouseEventArgs e) { return Hit(e); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = Hit(e);
                if (h != _hover)
                {
                    _hover = h;
                    Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hover != -1) { _hover = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            // 列表行自己处理点击，不用 Click 事件：那边拿不到行号
            public event MouseEventHandler RowActivate;

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int i = Hit(e);
                    if (i >= 0 && RowActivate != null) RowActivate(this, e);
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (_contentH <= Height) return;
                _scroll -= e.Delta;
                if (_scroll < 0) _scroll = 0;
                int max = _contentH - Height;
                if (_scroll > max) _scroll = max;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var b = new SolidBrush(Ui.CARD)) g.FillRectangle(b, ClientRectangle);

                int rh = Ui.Px(RowH);
                _contentH = Rows.Count * (rh + 1) + 2;

                // 空状态：居中灰字。画在列表里而不是单独控件，省得一换状态就要动布局
                if (Rows.Count == 0)
                {
                    Ui.TextC(g, "没有找到卡片，换个关键词试试", Ui.F(10f), Ui.TEXT_DIM,
                        Width / 2, Height / 2);
                    return;
                }

                for (int i = 0; i < Rows.Count; i++)
                {
                    int y = RowTop(i);
                    if (y + rh + 1 < 0) continue;
                    if (y > Height) break;

                    if (i == _hover)
                        Ui.FillRound(g, new Rectangle(0, y, Width, rh), Ui.Px(8), Ui.ACCENT_SOFT);

                    NoteRow r = Rows[i];
                    // 标题和副标题都靠左留 12，和 Android 版的内边距对齐
                    int pad = Ui.Px(12);
                    Ui.TextVC(g, r.Title, Ui.F(10.5f, true), Ui.INK,
                        new Rectangle(pad, y + Ui.Px(8), Width - pad * 2, Ui.Px(24)));
                    Ui.TextVC(g, r.Sub, Ui.F(8.5f), Ui.TEXT_DIM,
                        new Rectangle(pad, y + Ui.Px(34), Width - pad * 2, Ui.Px(20)));

                    // 行之间的 1px 分隔线（缩进 12，和 Android 版的 hairline 一样）
                    if (i < Rows.Count - 1)
                    {
                        using (var p = new Pen(Ui.LINE))
                            g.DrawLine(p, pad, y + rh, Width - pad, y + rh);
                    }
                }

                // 滚动条
                if (_contentH > Height)
                {
                    int trackH = Height - Ui.Px(8);
                    int thumbH = Math.Max(Ui.Px(28), trackH * Height / _contentH);
                    int max = _contentH - Height;
                    int top = Ui.Px(4) + (max <= 0 ? 0 : (trackH - thumbH) * _scroll / max);
                    Ui.FillRound(g, new Rectangle(Width - Ui.Px(7), top, Ui.Px(4), thumbH), Ui.Px(2), Ui.LINE);
                }
            }
        }

        /// <summary>草稿列表：一行一个文件，右侧一个「删除」。</summary>
        class DraftList : Control
        {
            public readonly List<Draft> Items = new List<Draft>();
            int _hover = -1;
            int _scroll, _contentH;
            int _delDown = -1;        // 按下时命中的删除按钮行号，抬起来还在同一行才算点击

            public event EventHandler DeleteAsk;
            public event MouseEventHandler RowActivate;

            public DraftList()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Ui.CARD;
            }

            public void Reset() { _scroll = 0; _hover = -1; _delDown = -1; Invalidate(); }

            int RowTop(int i) { return i * (Ui.Px(RowH) + 1) - _scroll; }

            Rectangle DelRect(int i)
            {
                int w = Pill.Measure("删除", Ui.F(9f));
                return new Rectangle(Width - Ui.Px(16) - w, RowTop(i) + (Ui.Px(RowH) - Ui.Px(28)) / 2, w, Ui.Px(28));
            }

            int Hit(MouseEventArgs e)
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    int y = RowTop(i);
                    if (e.Y >= y && e.Y < y + Ui.Px(RowH)) return i;
                }
                return -1;
            }

            /// <summary>鼠标位置落在第几行（-1 = 没落在行上）。</summary>
            public int HitIndex(MouseEventArgs e) { return Hit(e); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = Hit(e);
                Cursor = (h >= 0 && DelRect(h).Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
                if (h != _hover) { _hover = h; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hover != -1) { _hover = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int i = Hit(e);
                    _delDown = (i >= 0 && DelRect(i).Contains(e.Location)) ? i : -1;
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int i = Hit(e);
                    if (i >= 0 && _delDown == i && i < Items.Count)
                    {
                        if (DelRect(i).Contains(e.Location))
                        {
                            if (DeleteAsk != null) DeleteAsk(Items[i], EventArgs.Empty);
                        }
                        else if (RowActivate != null) RowActivate(this, e);
                    }
                    _delDown = -1;
                }
                base.OnMouseUp(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (_contentH <= Height) return;
                _scroll -= e.Delta;
                if (_scroll < 0) _scroll = 0;
                int max = _contentH - Height;
                if (_scroll > max) _scroll = max;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var b = new SolidBrush(Ui.CARD)) g.FillRectangle(b, ClientRectangle);

                int rh = Ui.Px(RowH);
                _contentH = Items.Count * (rh + 1) + 2;

                if (Items.Count == 0)
                {
                    Ui.TextC(g, "还没有草稿", Ui.F(10f), Ui.TEXT_DIM, Width / 2, Height / 2);
                    return;
                }

                for (int i = 0; i < Items.Count; i++)
                {
                    int y = RowTop(i);
                    if (y + rh + 1 < 0) continue;
                    if (y > Height) break;

                    if (i == _hover) Ui.FillRound(g, new Rectangle(0, y, Width, rh), Ui.Px(8), Ui.ACCENT_SOFT);

                    Draft d = Items[i];
                    int pad = Ui.Px(12);
                    int btnW = Width - pad * 2 - DelRect(i).Width - Ui.Px(10);
                    Ui.TextVC(g, d.Word.Length > 0 ? d.Word : "(无词)", Ui.F(10.5f, true), Ui.INK,
                        new Rectangle(pad, y + Ui.Px(8), btnW, Ui.Px(24)));
                    Ui.TextVC(g, d.Config + "　·　" + d.Saved, Ui.F(8.5f), Ui.TEXT_DIM,
                        new Rectangle(pad, y + Ui.Px(34), btnW, Ui.Px(20)));

                    // 「删除草稿」用自制的小按钮：Pill 是 Control，塞进自绘列表里会把绘制搞乱
                    Rectangle del = DelRect(i);
                    Ui.FillRound(g, del, del.Height / 2, Ui.CARD);
                    Ui.StrokeRound(g, del, del.Height / 2, Ui.RED);
                    Ui.TextC(g, "删除", Ui.F(9f), Ui.RED, del.X + del.Width / 2, del.Y + del.Height / 2);

                    if (i < Items.Count - 1)
                    {
                        using (var p = new Pen(Ui.LINE))
                            g.DrawLine(p, pad, y + rh, Width - pad, y + rh);
                    }
                }

                if (_contentH > Height)
                {
                    int trackH = Height - Ui.Px(8);
                    int thumbH = Math.Max(Ui.Px(28), trackH * Height / _contentH);
                    int max = _contentH - Height;
                    int top = Ui.Px(4) + (max <= 0 ? 0 : (trackH - thumbH) * _scroll / max);
                    Ui.FillRound(g, new Rectangle(Width - Ui.Px(7), top, Ui.Px(4), thumbH), Ui.Px(2), Ui.LINE);
                }
            }
        }

        /// <summary>详情正文：逐字段自绘，字段值自己换行，页面自己滚。</summary>
        class DetailBody : Control
        {
            class Fld
            {
                public string Name = "";
                public string[] Lines;
            }

            readonly List<Fld> _flds = new List<Fld>();
            int _scroll, _contentH;

            public DetailBody()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Ui.CARD;
            }

            /// <summary>重新排版。fields 已经是「有内容的字段」且按笔记类型顺序。</summary>
            public void SetFields(List<KeyValuePair<string, string>> fields)
            {
                _flds.Clear();
                _scroll = 0;
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    int w = Width - Ui.Px(4);
                    if (w < Ui.Px(80)) w = Ui.Px(80);
                    Font fV = Ui.F(10f);
                    int max = Ui.Px(400);   // 单字段行数上限，免得一个超长字段把页面撑到滚不到头

                    foreach (KeyValuePair<string, string> kv in fields)
                    {
                        var fd = new Fld();
                        fd.Name = kv.Key;
                        // 空字符串行不能丢：它们是段落之间的空行
                        string[] lines = Ui.Wrap(g, kv.Value, fV, w);
                        if (lines.Length > max)
                        {
                            var cut = new string[max + 1];
                            Array.Copy(lines, cut, max);
                            cut[max] = "…（字段太长已截断，点「复制全部字段」可拿全文）";
                            lines = cut;
                        }
                        fd.Lines = lines;
                        _flds.Add(fd);
                    }
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var b = new SolidBrush(Ui.CARD)) g.FillRectangle(b, ClientRectangle);

                if (_flds.Count == 0)
                {
                    Ui.TextC(g, "这张卡片没有可显示的字段", Ui.F(10f), Ui.TEXT_DIM, Width / 2, Height / 2);
                    return;
                }

                Font fT = Ui.F(9f);
                Font fV = Ui.F(10f);
                int lineH = Ui.Px(21);
                int pad = Ui.Px(16);
                int y = Ui.Px(10) - _scroll;

                foreach (Fld fd in _flds)
                {
                    // 字段名（小号灰字）
                    Ui.Text(g, fd.Name, fT, Ui.TEXT_DIM, pad, y);
                    // 下划线把「名」和「值」分开，值多行时一眼知道从哪开始
                    using (var p = new Pen(Ui.LINE))
                        g.DrawLine(p, pad, y + Ui.Px(19), Width - pad, y + Ui.Px(19));
                    y += Ui.Px(26);

                    foreach (string ln in fd.Lines)
                    {
                        if (y + lineH >= 0 && y <= Height)
                            Ui.Text(g, ln, fV, Ui.TEXT_BODY, pad, y);
                        y += lineH;
                    }
                    y += Ui.Px(14);
                }

                _contentH = y + _scroll + Ui.Px(10);
                if (_contentH > Height)
                {
                    int trackH = Height - Ui.Px(8);
                    int thumbH = Math.Max(Ui.Px(28), trackH * Height / _contentH);
                    int max = _contentH - Height;
                    int top = Ui.Px(4) + (max <= 0 ? 0 : (trackH - thumbH) * _scroll / max);
                    Ui.FillRound(g, new Rectangle(Width - Ui.Px(7), top, Ui.Px(4), thumbH), Ui.Px(2), Ui.LINE);
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (_contentH <= Height) return;
                _scroll -= e.Delta;
                if (_scroll < 0) _scroll = 0;
                int max = _contentH - Height;
                if (_scroll > max) _scroll = max;
                Invalidate();
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); }
        }

        // ===== 数据载体 =====

        /// <summary>列表行的显示内容（列表只画这些，不用回头再解析一遍 HTML）。</summary>
        class NoteRow
        {
            public string Title = "";
            public string Sub = "";
            public int Index;          // 对应 _notes / _rows 的下标
        }

        /// <summary>一个草稿文件。</summary>
        class Draft
        {
            public string Path = "";
            public string Word = "";
            public string Config = "";
            public string Saved = "";
            public List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();
        }

        // ===== 构造 =====

        public BrowseView()
        {
            BackColor = Ui.BG;
            Font = Ui.F(9f);
            Ui.EnableDoubleBuffer(this);
            Build();
        }

        void Build()
        {
            // ---- 分段控件（两个并排胶囊，选中的 On=true）----
            _segCards = new Pill { Text = "本机卡片", Toggle = true, On = true, Font = Ui.F(9f, true) };
            _segDraft = new Pill { Text = "本地草稿", Toggle = true, On = false, Font = Ui.F(9f) };
            _segCards.Height = Ui.Px(34);
            _segDraft.Height = Ui.Px(34);
            _segCards.Click += delegate { SelectTab(false); };
            _segDraft.Click += delegate { SelectTab(true); };
            Controls.Add(_segCards);
            Controls.Add(_segDraft);

            // ---- 本机卡片上半部（牌组 + 搜索 + 查询）----
            _cardsTop = new Panel { BackColor = Ui.BG };
            _cardsTop.Resize += delegate { LayoutQuery(); };

            // 原来是系统 ComboBox：折叠态是 Win7 那块灰底、下拉还是个系统弹窗。
            // 换成自绘的 Select（同文件 Ui.cs），弹出的也是自绘卡片列表。
            _deck = new Select();
            _deck.Font = Ui.F(9.5f);
            _deck.Placeholder = "全部牌组";
            _deck.SetItems(new string[] { "全部牌组" });
            _deck.SelectedIndex = 0;
            _deck.SelectedIndexChanged += delegate
            {
                if (_deck.SelectedIndex <= 0) _selectedDeck = "";
                else if (_deck.SelectedIndex - 1 < _decks.Count) _selectedDeck = _decks[_deck.SelectedIndex - 1];
                else _selectedDeck = "";
            };
            _cardsTop.Controls.Add(_deck);

            _search = new Input();
            _search.Font = Ui.F(9.5f);
            _search.Placeholder = "搜索卡片（可用 deck: tag: 等语法）";
            _search.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoQuery(); }
            };
            _cardsTop.Controls.Add(_search);

            _queryBtn = new Pill { Text = "查询", Primary = true, Font = Ui.F(9.5f, true) };
            _queryBtn.Height = Ui.Px(32);
            _queryBtn.Width = Pill.Measure("查询", _queryBtn.Font) + Ui.Px(22);
            _queryBtn.Click += delegate { DoQuery(); };
            _cardsTop.Controls.Add(_queryBtn);

            _cardsStatus = new Label();
            _cardsStatus.Font = Ui.F(8.5f);
            _cardsStatus.ForeColor = Ui.TEXT_DIM;
            _cardsStatus.BackColor = Ui.BG;
            _cardsStatus.AutoSize = false;
            _cardsStatus.TextAlign = ContentAlignment.MiddleLeft;
            _cardsStatus.Text = "点「查询」列出卡片";
            Controls.Add(_cardsTop);
            Controls.Add(_cardsStatus);

            // ---- 结果列表 ----
            _list = new CardList();
            _list.RowActivate += delegate(object s, MouseEventArgs e) { OpenDetailAt(_list, e); };
            _listCard = new Card { Radius = 14, Fill = Ui.CARD, Border = Ui.LINE };
            _listCard.Controls.Add(_list);
            _listCard.Resize += delegate { _list.Bounds = InnerRect(_listCard); };
            Controls.Add(_listCard);

            // ---- 详情 ----
            _detail = new Panel { BackColor = Ui.BG, Visible = false };

            var back = new Pill { Text = "← 返回列表", Font = Ui.F(9f) };
            back.Height = Ui.Px(30);
            back.Width = Pill.Measure(back.Text, back.Font) + Ui.Px(10);
            back.Click += delegate { CloseDetail(); };
            _detail.Controls.Add(back);
            _backBtn = back;

            // 删除用系统 Button 而不是 Pill：Ui.Pill 没有 ForeColor，画不了红字
            _delBtn = new Button();
            _delBtn.Text = "删除本机卡片";
            _delBtn.Font = Ui.F(9f);
            _delBtn.FlatStyle = FlatStyle.Flat;
            _delBtn.FlatAppearance.BorderSize = 0;
            _delBtn.BackColor = Ui.BG;
            _delBtn.ForeColor = Ui.RED;
            _delBtn.Cursor = Cursors.Hand;
            _delBtn.UseVisualStyleBackColor = false;
            _delBtn.Click += delegate { ConfirmDelete(); };
            _detail.Controls.Add(_delBtn);

            _detailTip = new Label();
            _detailTip.Font = Ui.F(8.5f);
            _detailTip.ForeColor = Ui.TEXT_DIM;
            _detailTip.BackColor = Ui.BG;
            _detailTip.AutoSize = false;
            _detailTip.TextAlign = ContentAlignment.MiddleLeft;
            _detailTip.Text = "按笔记类型原样列出所有字段（HTML 与公式都会渲染）";
            _detail.Controls.Add(_detailTip);

            _detailMeta = new Label();
            _detailMeta.Font = Ui.F(8.5f);
            _detailMeta.ForeColor = Ui.SUB;
            _detailMeta.BackColor = Ui.BG;
            _detailMeta.AutoSize = false;
            _detailMeta.TextAlign = ContentAlignment.MiddleLeft;
            _detail.Controls.Add(_detailMeta);

            _copyBtn = new Pill { Text = "复制全部字段", Font = Ui.F(8.5f) };
            _copyBtn.Height = Ui.Px(26);
            _copyBtn.Width = Pill.Measure(_copyBtn.Text, _copyBtn.Font) + Ui.Px(10);
            _copyBtn.Click += delegate { CopyFields(); };
            _detail.Controls.Add(_copyBtn);

            _body = new DetailBody();
            _detailCard = new Card { Radius = 14, Fill = Ui.CARD, Border = Ui.LINE };
            _detailCard.Controls.Add(_body);
            _detailCard.Resize += delegate { _body.Bounds = InnerRect(_detailCard); };
            _detail.Controls.Add(_detailCard);

            _detail.Resize += delegate { LayoutDetail(); };
            Controls.Add(_detail);

            // ---- 本地草稿 ----
            _draftPane = new Panel { BackColor = Ui.BG, Visible = false };
            _draftStatus = new Label();
            _draftStatus.Font = Ui.F(8.5f);
            _draftStatus.ForeColor = Ui.TEXT_DIM;
            _draftStatus.BackColor = Ui.BG;
            _draftStatus.AutoSize = false;
            _draftStatus.TextAlign = ContentAlignment.MiddleLeft;
            _draftPane.Controls.Add(_draftStatus);

            _drafts = new DraftList();
            _drafts.RowActivate += delegate(object s, MouseEventArgs e) { ShowDraft(_drafts.HitIndex(e)); };
            _drafts.DeleteAsk += delegate(object s, EventArgs e) { ConfirmDeleteDraft((Draft)s); };
            _draftCard = new Card { Radius = 14, Fill = Ui.CARD, Border = Ui.LINE };
            _draftCard.Controls.Add(_drafts);
            _draftCard.Resize += delegate { _drafts.Bounds = InnerRect(_draftCard); };
            _draftPane.Controls.Add(_draftCard);
            _draftPane.Resize += delegate { LayoutDraft(); };
            Controls.Add(_draftPane);

            Resize += delegate { LayoutAll(); };
        }

        Pill _backBtn, _copyBtn;
        Label _detailTip, _detailMeta;
        Card _detailCard, _draftCard;

        /// <summary>卡片内部的可用区域（扣掉 1px 描边）。</summary>
        static Rectangle InnerRect(Card c)
        {
            return new Rectangle(Ui.Px(1), Ui.Px(1), Math.Max(0, c.Width - Ui.Px(2)), Math.Max(0, c.Height - Ui.Px(2)));
        }

        // ===== 布局 =====

        void LayoutAll()
        {
            int pad = Ui.Px(4);
            int w = Width - pad * 2;
            if (w < Ui.Px(40)) return;

            int y = 0;
            if (_segCards.Visible)
            {
                _segCards.Bounds = new Rectangle(pad, y, Ui.Px(96), Ui.Px(34));
                _segDraft.Bounds = new Rectangle(pad + Ui.Px(102), y, Ui.Px(96), Ui.Px(34));
                y += Ui.Px(34) + Ui.Px(14);
            }

            _narrow = Width < Ui.Px(720);
            int rowH = _narrow ? Ui.Px(32) * 2 + Ui.Px(8) : Ui.Px(32);

            if (_cardsTop.Visible)
            {
                _cardsTop.Bounds = new Rectangle(pad, y, w, rowH);
                LayoutQuery();
                y += rowH + Ui.Px(6);
            }

            if (_cardsStatus.Visible)
            {
                _cardsStatus.Bounds = new Rectangle(pad, y, w, Ui.Px(22));
                y += Ui.Px(24);
            }

            if (_listCard.Visible)
            {
                _listCard.Bounds = new Rectangle(pad, y, w, Math.Max(Ui.Px(80), Height - y - pad));
                _list.Bounds = InnerRect(_listCard);
            }

            LayoutDetail();
            LayoutDraft();
        }

        /// <summary>查询行：窄窗口搜索框换到第二行（和 Android 版 phone 分支一个道理）。</summary>
        void LayoutQuery()
        {
            if (_cardsTop == null) return;
            int w = _cardsTop.Width, h = _cardsTop.Height;
            if (w < Ui.Px(40)) return;

            int bh = Ui.Px(32);
            int gap = Ui.Px(8);
            int deckW = Math.Min(Ui.Px(200), Math.Max(Ui.Px(120), w / 4));

            _queryBtn.Bounds = new Rectangle(w - _queryBtn.Width, 0, _queryBtn.Width, bh);

            if (_narrow)
            {
                // 第一行：牌组 + 查询；第二行：搜索框占满
                _deck.Bounds = new Rectangle(0, 0, Math.Max(Ui.Px(80), w - _queryBtn.Width - gap), bh);
                _search.Bounds = new Rectangle(0, bh + gap, w, bh);
            }
            else
            {
                _deck.Bounds = new Rectangle(0, 0, deckW, bh);
                _queryBtn.Bounds = new Rectangle(w - _queryBtn.Width, 0, _queryBtn.Width, bh);
                int sx = deckW + Ui.Px(10);
                int sw = w - sx - _queryBtn.Width - Ui.Px(10);
                if (sw < Ui.Px(80)) sw = Ui.Px(80);
                _search.Bounds = new Rectangle(sx, 0, sw, bh);
            }
        }

        void LayoutDetail()
        {
            if (_detail == null) return;
            int pad = Ui.Px(4);
            int w = _detail.Width - pad * 2;
            if (w < Ui.Px(40)) return;

            int y = 0;
            _backBtn.Bounds = new Rectangle(pad, y, _backBtn.Width, Ui.Px(30));
            _delBtn.Bounds = new Rectangle(_detail.Width - pad - Ui.Px(110), y, Ui.Px(110), Ui.Px(30));
            y += Ui.Px(34);

            _detailTip.Bounds = new Rectangle(pad, y, w, Ui.Px(20));
            y += Ui.Px(22);

            // 一行：笔记类型 · 标签，右边跟「复制全部字段」
            _copyBtn.Bounds = new Rectangle(_detail.Width - pad - _copyBtn.Width, y, _copyBtn.Width, Ui.Px(26));
            _detailMeta.Bounds = new Rectangle(pad, y, Math.Max(Ui.Px(80), w - _copyBtn.Width - Ui.Px(10)), Ui.Px(26));
            y += Ui.Px(30);

            _detailCard.Bounds = new Rectangle(pad, y, w, Math.Max(Ui.Px(80), _detail.Height - y - pad));
            _body.Bounds = InnerRect(_detailCard);
        }

        void LayoutDraft()
        {
            if (_draftPane == null) return;
            int pad = Ui.Px(4);
            int w = _draftPane.Width - pad * 2;
            if (w < Ui.Px(40)) return;

            _draftStatus.Bounds = new Rectangle(pad, 0, w, Ui.Px(24));
            _draftCard.Bounds = new Rectangle(pad, Ui.Px(26), w, Math.Max(Ui.Px(80), _draftPane.Height - Ui.Px(26) - pad));
            _drafts.Bounds = InnerRect(_draftCard);
        }

        // ===== 对外 =====

        /// <summary>切到这一页时调用：刷新牌组列表，必要时查询。</summary>
        public void Activate()
        {
            if (_busy) return;
            if (_tab) { RenderDrafts(); return; }
            LoadDecks();
        }

        void SelectTab(bool draft)
        {
            _tab = draft;
            _segCards.On = !draft;
            _segDraft.On = draft;
            _segCards.Font = Ui.F(9f, !draft);
            _segDraft.Font = Ui.F(9f, draft);
            _segCards.Invalidate();
            _segDraft.Invalidate();

            // 详情算「本机卡片」里的一个子视图，切回本机卡片时回到列表
            if (!draft) _detail.Visible = false;

            _cardsTop.Visible = !draft;
            _cardsStatus.Visible = !draft;
            _listCard.Visible = !draft;
            _draftPane.Visible = draft;

            LayoutAll();
            if (draft) RenderDrafts();
            else if (_decks.Count == 0) LoadDecks();
        }

        // ===== 本机卡片：读牌组 =====

        void LoadDecks()
        {
            SetCardsStatus("正在读取牌组…", StatusKind.Dim);
            var t = new Thread(delegate()
            {
                List<string> ds = null;
                string err = "";
                try { ds = AnkiConn.DeckNames(); }
                catch (Exception ex) { err = ex.Message; }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    if (ds != null)
                    {
                        _decks = ds;
                        RebuildDeckItems();
                        // 和 Android 版一样：读完牌组自动查一次，省得用户还要再点
                        if (_notes.Count == 0) DoQuery();
                        else SetCardsStatus(SyncState.StatusText(), StatusKind.Dim);
                    }
                    else SetCardsStatus("读取牌组失败：" + err, StatusKind.Red);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void RebuildDeckItems()
        {
            var items = new List<string>();
            items.Add("全部牌组");
            foreach (string d in _decks) items.Add(d);
            _deck.SetItems(items.ToArray());
            int idx = 0;
            for (int i = 0; i < _decks.Count; i++) if (_decks[i] == _selectedDeck) idx = i + 1;
            if (idx >= items.Count) idx = 0;
            _deck.SelectedIndex = idx;
        }

        // ===== 本机卡片：查询 =====

        /// <summary>组装 Anki 搜索语句（和 Android 版 buildQuery 一致）。</summary>
        string BuildQuery(string user)
        {
            string q = "";
            if (_selectedDeck.Length > 0) q = "deck:\"" + _selectedDeck + "\"";
            if (user != null && user.Length > 0) q = (q.Length > 0 ? q + " " : "") + user;
            if (q.Length == 0) q = "deck:*";     // 什么都不选时给个「所有卡片」
            return q;
        }

        string UserQuery()
        {
            return _search.Text.Trim();
        }

        void DoQuery()
        {
            if (_busy) return;
            string q = BuildQuery(UserQuery());
            int seq = ++_reqSeq;

            _busy = true;
            _queryBtn.Enabled = false;
            SetCardsStatus("正在查询…", StatusKind.Dim);
            if (MainForm.Instance != null) MainForm.Instance.SetStatus("正在查询…");

            var t = new Thread(delegate()
            {
                List<long> ids = null;
                List<AnkiConn.Note> notes = null;
                string err = "";
                try
                {
                    ids = AnkiConn.FindNotes(q);
                    int take = Math.Min(ids.Count, MaxNotes);
                    // 只取要显示的那些；AnkiConnect 一次塞 200 条也不会太慢
                    notes = AnkiConn.NotesInfo(ids.GetRange(0, take));
                }
                catch (Exception ex) { err = ex.Message; }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || seq != _reqSeq) return;
                    _busy = false;
                    _queryBtn.Enabled = true;

                    if (notes == null)
                    {
                        SetCardsStatus("查询失败：" + err, StatusKind.Red);
                        MainForm.Instance.SetStatus(Tips(), SyncState.Kind == SyncKind.Error);
                        return;
                    }

                    _notes = notes;
                    _rows = new List<NoteRow>();
                    foreach (AnkiConn.Note n in _notes)
                    {
                        var r = new NoteRow();
                        string title = StripHtml(FirstFieldValue(n));
                        if (title.Length == 0) title = "(空卡片)";
                        if (title.Length > 80) title = title.Substring(0, 80) + "…";
                        r.Title = title;
                        r.Sub = (n.Model.Length > 0 ? n.Model : "笔记") +
                                (TagString(n).Length > 0 ? "　·　" + TagString(n) : "");
                        r.Index = _rows.Count;
                        _rows.Add(r);
                    }
                    _list.Rows.Clear();
                    _list.Rows.AddRange(_rows);
                    _list.Reset();
                    _list.Invalidate();

                    int total = ids.Count;
                    if (total == 0)
                    {
                        SetCardsStatus("没有找到卡片，换个关键词试试", StatusKind.Amber);
                    }
                    else
                    {
                        string s = "共 " + total + " 张，显示前 " + _notes.Count + " 张";
                        if (total > MaxNotes) s += "（只显示前 " + MaxNotes + " 张）";
                        s += total > _notes.Count ? "，用搜索缩小范围" : "";
                        SetCardsStatus(s, StatusKind.Dim);
                    }
                    MainForm.Instance.SetStatus(SyncState.StatusText(), SyncState.Kind == SyncKind.Error);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>引擎没起来时给一句能照着做的提示。</summary>
        static string Tips()
        {
            if (SyncState.Kind == SyncKind.Error)
                return "内置引擎没起来，点右上角头像看怎么办";
            return SyncState.StatusText();
        }

        enum StatusKind { Dim, Green, Amber, Red }

        void SetCardsStatus(string text, StatusKind kind)
        {
            _cardsStatus.Text = text;
            _cardsStatus.ForeColor =
                kind == StatusKind.Green ? Ui.GREEN :
                kind == StatusKind.Amber ? Ui.AMBER :
                kind == StatusKind.Red ? Ui.RED : Ui.TEXT_DIM;
        }

        // ===== 本机卡片：详情 =====

        void OpenDetailAt(CardList list, MouseEventArgs e)
        {
            // 行顶算在 CardList 里（含滚动偏移），所以行号必须问它，不能在这边重算
            int i = list.HitIndex(e);
            if (i < 0 || i >= _notes.Count) return;
            OpenDetail(_notes[i]);
        }

        void OpenDetail(AnkiConn.Note note)
        {
            if (note == null) return;
            _current = note;

            string tags = TagString(note);
            _detailMeta.Text = (note.Model.Length > 0 ? note.Model : "笔记") +
                               (tags.Length > 0 ? "　·　" + tags : "");

            _detail.Visible = true;
            _cardsTop.Visible = false;
            _cardsStatus.Visible = false;
            _listCard.Visible = false;
            // 先排版再折行：DetailBody 要知道自己的宽度才能算换行
            LayoutAll();
            _body.SetFields(DetailFields(note));
            _body.Invalidate();
        }

        void CloseDetail()
        {
            _current = null;
            _detail.Visible = false;
            _cardsTop.Visible = !_tab;
            _cardsStatus.Visible = !_tab;
            _listCard.Visible = !_tab;
            LayoutAll();
        }

        /// <summary>按笔记类型的字段顺序取「有内容」的字段（值已剥 HTML）。</summary>
        List<KeyValuePair<string, string>> DetailFields(AnkiConn.Note note)
        {
            var order = new List<string>();
            try
            {
                if (note.Model.Length > 0)
                    foreach (string k in AnkiConn.ModelFieldNames(note.Model))
                        if (!order.Contains(k)) order.Add(k);
            }
            catch { }
            // 拿不到（离线 / 类型已删）就用 NotesInfo 给的键顺序兜底
            if (order.Count == 0) foreach (KeyValuePair<string, string> kv in note.Fields) order.Add(kv.Key);
            foreach (KeyValuePair<string, string> kv in note.Fields)
                if (!order.Contains(kv.Key)) order.Add(kv.Key);

            var out_ = new List<KeyValuePair<string, string>>();
            foreach (string k in order)
            {
                string v;
                if (!note.Fields.TryGetValue(k, out v)) v = "";
                if (v == null) v = "";
                // 全空（连标签都没有）的字段跳过，和 Android 版一致
                if (v.Replace("\r", "").Replace("\n", "").Trim().Length == 0)
                {
                    if (StripHtml(v).Trim().Length == 0) continue;
                }
                out_.Add(new KeyValuePair<string, string>(k, RichText(v)));
            }
            return out_;
        }

        void CopyFields()
        {
            if (_current == null) return;
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in DetailFields(_current))
            {
                sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n\r\n");
            }
            try { Clipboard.SetText(sb.ToString()); }
            catch { }
            if (MainForm.Instance != null) MainForm.Instance.SetStatus("字段已复制到剪贴板");
        }

        void ConfirmDelete()
        {
            if (_current == null || _busy) return;
            long id = _current.Id;
            if (!Dlg.Confirm(this, "删除卡片",
                    "从本机收藏库删除这张卡片。下次与云端同步时也会一并删除。",
                    "删除", "取消", true)) return;

            _busy = true;
            _delBtn.Enabled = false;
            if (MainForm.Instance != null) MainForm.Instance.SetStatus("正在删除…");

            var t = new Thread(delegate()
            {
                string err = "";
                try
                {
                    var ids = new List<long>();
                    ids.Add(id);
                    AnkiConn.DeleteNotes(ids);
                }
                catch (Exception ex) { err = ex.Message; }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    _busy = false;
                    _delBtn.Enabled = true;
                    if (err.Length > 0)
                    {
                        Dlg.Info(this, "删除失败", "本机卡片", err, 160);
                        return;
                    }
                    CloseDetail();
                    DoQuery();   // 删完重新查，列表才不会留着一张已经不存在的卡片
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ===== 本地草稿 =====

        static string DraftDir { get { return Path.Combine(Store.DataDir, "drafts"); } }

        void RenderDrafts()
        {
            // 目录可能压根不存在（没存过草稿），所以先判存在再枚举
            _draftData = new List<Draft>();
            string dir = DraftDir;
            if (Directory.Exists(dir))
            {
                string[] files = null;
                try { files = Directory.GetFiles(dir, "*.json"); } catch { files = null; }
                if (files != null)
                {
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        Draft d = ReadDraft(f);
                        if (d != null) _draftData.Add(d);
                    }
                }
            }

            _drafts.Items.Clear();
            _drafts.Items.AddRange(_draftData);
            _drafts.Reset();
            _drafts.Invalidate();

            if (_draftData.Count == 0) _draftStatus.Text = "还没有草稿";
            else _draftStatus.Text = "共 " + _draftData.Count + " 条草稿　·　点一行看字段，右侧可删除";
            _draftStatus.ForeColor = Ui.TEXT_DIM;
        }

        /// <summary>读一个草稿文件；解析失败就跳过（宁可不显示，也别弹一堆错）。</summary>
        static Draft ReadDraft(string path)
        {
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var map = ser.DeserializeObject(raw) as Dictionary<string, object>;
                if (map == null) return null;

                var d = new Draft();
                d.Path = path;
                object v;
                if (map.TryGetValue("word", out v)) d.Word = Convert.ToString(v);
                if (map.TryGetValue("config", out v)) d.Config = Convert.ToString(v);
                if (map.TryGetValue("saved", out v)) d.Saved = Convert.ToString(v);
                if (d.Config.Length == 0) d.Config = "default";
                if (d.Saved.Length == 0) d.Saved = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");

                object fo;
                if (map.TryGetValue("fields", out fo))
                {
                    var fm = fo as Dictionary<string, object>;
                    if (fm != null)
                        foreach (KeyValuePair<string, object> kv in fm)
                            d.Fields.Add(new KeyValuePair<string, string>(kv.Key, Convert.ToString(kv.Value)));
                }
                return d;
            }
            catch { return null; }
        }

        void ShowDraft(int i)
        {
            if (i < 0 || i >= _draftData.Count) return;
            Draft d = _draftData[i];

            // 草稿摘要用 MarkdownView 渲染，字段里的 * _ ` 会被当成格式，所以逐行转义
            var sb = new StringBuilder();
            sb.Append("**").Append(Esc(d.Word.Length > 0 ? d.Word : "(无词)")).Append("**\r\n\r\n");
            sb.Append("格式：").Append(Esc(d.Config)).Append("　·　保存于 ").Append(Esc(d.Saved)).Append("\r\n\r\n");
            sb.Append("---\r\n\r\n");
            if (d.Fields.Count == 0) sb.Append("(这条草稿没有字段)\r\n");
            foreach (KeyValuePair<string, string> kv in d.Fields)
                sb.Append("**").Append(Esc(kv.Key)).Append("**：").Append(Esc(kv.Value)).Append("\r\n\r\n");

            Dlg.Info(this, "草稿详情", Path.GetFileName(d.Path), sb.ToString(), 300);
        }

        void ConfirmDeleteDraft(Draft d)
        {
            if (d == null) return;
            if (!Dlg.Confirm(this, "删除草稿",
                    "删掉 " + Path.GetFileName(d.Path) + "，删了就没法恢复。",
                    "删除", "取消", true)) return;
            try
            {
                File.Delete(d.Path);
                RenderDrafts();
                if (MainForm.Instance != null) MainForm.Instance.SetStatus("草稿已删除");
            }
            catch (Exception ex)
            {
                Dlg.Info(this, "删除失败", "本地草稿", ex.Message, 160);
            }
        }

        // ===== 文本工具 =====

        /// <summary>取「第一个有内容的字段」的值（值里可能带 HTML，只用来做标题）。</summary>
        static string FirstFieldValue(AnkiConn.Note note)
        {
            if (note == null) return "";
            foreach (KeyValuePair<string, string> kv in note.Fields)
            {
                if (kv.Value != null && StripHtml(kv.Value).Trim().Length > 0) return kv.Value;
            }
            return "";
        }

        /// <summary>"# a # b" 形式（和 Android 版 tagString 一致，这里用空格分隔）。</summary>
        static string TagString(AnkiConn.Note note)
        {
            if (note == null) return "";
            var sb = new StringBuilder();
            foreach (string t in note.Tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append("# ").Append(t);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 剥 HTML 做单行摘要：块级标签换成空格，其余标签去掉，实体还原，空白收成一个空格。
        /// 用状态机而不是正则：C# 里 replaceAll 的等价物太啰嗦，而且正则跑在长字段上更慢。
        /// </summary>
        static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var sb = new StringBuilder(html.Length);
            int i = 0;
            while (i < html.Length)
            {
                char c = html[i];
                if (c == '<')
                {
                    int end = html.IndexOf('>', i + 1);
                    if (end < 0) break;                      // 落单的 '<'，后面都没意义了
                    string tag = html.Substring(i + 1, end - i - 1).Trim().ToLowerInvariant();
                    if (tag.StartsWith("/p") || tag.StartsWith("br") || tag.StartsWith("/div") ||
                        tag.StartsWith("p ") || tag.StartsWith("div ") || tag.StartsWith("/li") ||
                        tag.StartsWith("/tr") || tag.StartsWith("hr"))
                        sb.Append(' ');
                    i = end + 1;
                    continue;
                }
                if (c == '&')
                {
                    int end = html.IndexOf(';', i + 1);
                    if (end > i && end - i <= 9)
                    {
                        string ent = html.Substring(i + 1, end - i - 1).ToLowerInvariant();
                        if (ent == "nbsp") { sb.Append(' '); i = end + 1; continue; }
                        if (ent == "amp") { sb.Append('&'); i = end + 1; continue; }
                        if (ent == "lt") { sb.Append('<'); i = end + 1; continue; }
                        if (ent == "gt") { sb.Append('>'); i = end + 1; continue; }
                        if (ent == "quot") { sb.Append('"'); i = end + 1; continue; }
                        if (ent == "apos" || ent == "#39") { sb.Append('\''); i = end + 1; continue; }
                    }
                }
                sb.Append(c);
                i++;
            }

            // 折叠空白
            var out_ = new StringBuilder(sb.Length);
            bool sp = false;
            foreach (char c in sb.ToString())
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { sp = true; continue; }
                if (sp && out_.Length > 0) out_.Append(' ');
                sp = false;
                out_.Append(c);
            }
            return out_.ToString().Trim();
        }

        /// <summary>
        /// 详情里显示的字段值：块级标签换成换行（保留段落），其余标签去掉，实体还原。
        /// 公式（\(...\)、\[...\]）原样留着 —— 剥符号就看不出来了。
        /// </summary>
        static string RichText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var sb = new StringBuilder(html.Length);
            int i = 0;
            while (i < html.Length)
            {
                char c = html[i];
                if (c == '<')
                {
                    int end = html.IndexOf('>', i + 1);
                    if (end < 0) break;
                    string tag = html.Substring(i + 1, end - i - 1).Trim().ToLowerInvariant();
                    if (tag.StartsWith("br") || tag.StartsWith("/p") || tag.StartsWith("/div") ||
                        tag.StartsWith("p ") || tag.StartsWith("p>") || tag.StartsWith("div ") ||
                        tag.StartsWith("div>") || tag.StartsWith("/li") || tag.StartsWith("/tr") ||
                        tag.StartsWith("hr"))
                        sb.Append('\n');
                    i = end + 1;
                    continue;
                }
                if (c == '&')
                {
                    int end = html.IndexOf(';', i + 1);
                    if (end > i && end - i <= 9)
                    {
                        string ent = html.Substring(i + 1, end - i - 1).ToLowerInvariant();
                        if (ent == "nbsp") { sb.Append(' '); i = end + 1; continue; }
                        if (ent == "amp") { sb.Append('&'); i = end + 1; continue; }
                        if (ent == "lt") { sb.Append('<'); i = end + 1; continue; }
                        if (ent == "gt") { sb.Append('>'); i = end + 1; continue; }
                        if (ent == "quot") { sb.Append('"'); i = end + 1; continue; }
                        if (ent == "apos" || ent == "#39") { sb.Append('\''); i = end + 1; continue; }
                    }
                }
                sb.Append(c);
                i++;
            }

            // 清掉行尾空白和连续空行（三行以上并成一行）
            var out_ = new StringBuilder(sb.Length);
            int blank = 0;
            foreach (string raw in sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string ln = raw.TrimEnd();
                if (ln.Trim().Length == 0)
                {
                    blank++;
                    if (blank > 2) continue;
                }
                else blank = 0;
                out_.Append(ln).Append('\n');
            }
            return out_.ToString().Trim('\n');
        }

        /// <summary>MarkdownView 只认 ** 和 ` —— 草稿正文里出现这些字符要转义掉。</summary>
        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("`", "\\`");
        }

        // ===== 布局用的行高 =====
    }
}
