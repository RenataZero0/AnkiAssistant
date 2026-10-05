using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 统一的弹窗外壳：白底卡片、加粗标题、可选说明、正文区、右下角按钮。
    ///
    /// 内容太高时正文区自己滚（内容控件负责），弹窗本身不会超出屏幕 70% 高。
    /// 所有弹窗都从这里派生，别再用系统 MessageBox —— 那个在中文 Windows 上
    /// 字体和按钮风格跟主界面完全不是一套。
    /// </summary>
    public class DlgForm : Form
    {
        protected Panel Head;
        public Panel Body;
        protected FlowLayoutPanel Foot;
        public int Pad = 24;

        public DlgForm(string title, string sub, int widthDp)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.CARD;
            ForeColor = Ui.INK;
            Font = Ui.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            ClientSize = new Size(Ui.Px(widthDp), Ui.Px(120));

            Head = new Panel { Dock = DockStyle.Top, BackColor = Ui.CARD, Height = Ui.Px(56) };
            Head.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Text(g, title, Ui.F(13f, true), Ui.INK, Ui.Px(Pad), Ui.Px(20));
                if (!string.IsNullOrEmpty(sub))
                    Ui.Text(g, sub, Ui.F(9f), Ui.SUB, Ui.Px(Pad), Ui.Px(44));
                using (var p = new Pen(Ui.ACCENT, 1.2f))
                    g.DrawLine(p, 0, Head.Height - 1, Head.Width, Head.Height - 1);
            };
            if (!string.IsNullOrEmpty(sub)) Head.Height = Ui.Px(74);

            Foot = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Ui.CARD,
                Height = Ui.Px(58),
                Padding = new Padding(Ui.Px(10), Ui.Px(8), Ui.Px(Pad - 6), Ui.Px(10)),
                WrapContents = false
            };
            Body = new Panel { Dock = DockStyle.Fill, BackColor = Ui.CARD };

            Controls.Add(Body);
            Controls.Add(Foot);
            Controls.Add(Head);
        }

        /// <summary>加一个按钮。primary = 主色实心。</summary>
        public Pill Button(string text, bool primary, DialogResult result)
        {
            var b = new Pill { Text = text, Primary = primary, Font = Ui.F(9.5f, primary) };
            b.Size = new Size(Pill.Measure(text, Ui.F(9.5f, primary)) + Ui.Px(16), Ui.Px(34));
            b.Margin = new Padding(Ui.Px(8), 0, 0, 0);
            b.Click += delegate
            {
                DialogResult = result;
                Close();
            };
            Foot.Controls.Add(b);
            return b;
        }

        /// <summary>正文区宽度（已扣掉左右留白）。</summary>
        public int BodyWidth { get { return ClientSize.Width - Ui.Px(Pad * 2); } }

        /// <summary>按正文需要的高度收窗口，最高不超过屏幕的 70%。</summary>
        public void FitBody(int bodyHeightDp)
        {
            int max = (int)(Screen.FromControl(this).WorkingArea.Height * 0.70);
            int h = Head.Height + Ui.Px(bodyHeightDp) + Foot.Height;
            ClientSize = new Size(ClientSize.Width, Math.Min(h, max));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Icon = AppIcon.Get(32); } catch { }
        }
    }

    /// <summary>常用弹窗。</summary>
    public static class Dlg
    {
        // ===== 纯文字/说明 =====
        public static void Info(IWin32Window owner, string title, string sub, string markdown, int bodyDp = 240)
        {
            using (var d = new DlgForm(title, sub, 460))
            {
                var md = new MarkdownView
                {
                    Dock = DockStyle.None,
                    Location = new Point(Ui.Px(d.Pad), Ui.Px(10)),
                    Size = new Size(d.BodyWidth, Ui.Px(bodyDp)),
                    Markdown = markdown ?? ""
                };
                d.Body.Controls.Add(md);
                d.Button("知道了", true, DialogResult.OK);
                d.FitBody(bodyDp + 20);
                d.ShowDialog(owner);
            }
        }

        /// <summary>居中显示一段滚动的长文（更新日志之类）。</summary>
        public static void Markdown(IWin32Window owner, string title, string sub, string markdown)
        {
            int max = (int)(Screen.PrimaryScreen.WorkingArea.Height * 0.62);
            int lines = 0;
            if (!string.IsNullOrEmpty(markdown)) lines = markdown.Replace("\r\n", "\n").Split('\n').Length;
            int want = Math.Min(max, Math.Max(Ui.Px(200), lines * Ui.Px(22)));
            Info(owner, title, sub, markdown, want);
        }

        // ===== 确认 =====
        public static bool Confirm(IWin32Window owner, string title, string sub,
            string okText = "确定", string cancelText = "取消", bool danger = false)
        {
            using (var d = new DlgForm(title, sub, 420))
            {
                var ok = d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                if (danger) ok.ForeColor = Ui.RED;
                d.AcceptButton = null;
                d.FitBody(8);
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        // ===== 确认（带正文）=====
        /// <summary>带正文的确认框：sub 是标题下的说明，message 是正文段落。</summary>
        public static bool Confirm(IWin32Window owner, string title, string sub, string message,
            string okText, string cancelText, bool danger)
        {
            using (var d = new DlgForm(title, sub, 460))
            {
                int lines = 0;
                foreach (string part in (message ?? "").Split('\n'))
                    lines += Math.Max(1, (int)Math.Ceiling(part.Length / 34.0));
                if (lines < 1) lines = 1;

                var lbl = new Label();
                lbl.AutoSize = false;
                lbl.Dock = DockStyle.Top;
                lbl.Font = Ui.F(10f);
                lbl.ForeColor = Ui.TEXT_BODY;
                lbl.BackColor = Color.Transparent;
                lbl.Height = Ui.Px(lines * 23);
                lbl.Text = message;
                d.Body.Controls.Add(lbl);

                var ok = d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                if (danger) ok.ForeColor = Ui.RED;
                d.AcceptButton = null;
                d.FitBody(lines * 23 + 8);
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        // ===== 单选列表 =====
        /// <summary>返回选中下标，取消返回 -1。dots 给每项前面画一个色点（选皮肤用）。</summary>
        public static int Choose(IWin32Window owner, string title, string sub, string[] items,
            string[] notes = null, int selected = -1, Color[] dots = null)
        {
            int rowH = Ui.Px(46);
            int bodyH = Math.Min(Ui.Px(320), items.Length * rowH + Ui.Px(8));
            int result = -1;

            using (var d = new DlgForm(title, sub, 420))
            {
                var list = new ListBox
                {
                    BorderStyle = BorderStyle.None,
                    DrawMode = DrawMode.OwnerDrawFixed,
                    ItemHeight = rowH,
                    IntegralHeight = false,
                    BackColor = Ui.CARD,
                    ForeColor = Ui.INK,
                    Font = Ui.F(9.5f),
                    Location = new Point(Ui.Px(d.Pad - 4), Ui.Px(6)),
                    Size = new Size(d.BodyWidth + Ui.Px(8), bodyH)
                };
                list.Items.AddRange(items);
                if (selected >= 0 && selected < items.Length) list.SelectedIndex = selected;

                list.DrawItem += delegate(object s, DrawItemEventArgs e)
                {
                    if (e.Index < 0) return;
                    var g = e.Graphics;
                    bool sel = (e.State & DrawItemState.Selected) != 0;
                    var row = new Rectangle(0, e.Bounds.Top, list.Width, rowH - 1);
                    if (sel) Ui.FillRound(g, new Rectangle(row.X + 2, row.Y + 2, row.Width - 6, row.Height - 6), Ui.Px(8), Ui.ACCENT_SOFT);

                    int tx = Ui.Px(12);
                    if (dots != null && e.Index < dots.Length && dots[e.Index] != Color.Empty)
                    {
                        using (var b = new SolidBrush(dots[e.Index]))
                            g.FillEllipse(b, tx, row.Y + rowH / 2 - Ui.Px(6), Ui.Px(12), Ui.Px(12));
                        tx += Ui.Px(22);
                    }
                    string note = (notes != null && e.Index < notes.Length) ? notes[e.Index] : "";
                    if (string.IsNullOrEmpty(note))
                    {
                        Ui.Text(g, items[e.Index], Ui.F(9.5f), sel ? Ui.ACCENT : Ui.INK, tx, row.Y + rowH / 2 - Ui.Px(9));
                    }
                    else
                    {
                        Ui.Text(g, items[e.Index], Ui.F(9.5f, true), sel ? Ui.ACCENT : Ui.INK, tx, row.Y + Ui.Px(7));
                        Ui.Text(g, note, Ui.F(8.5f), Ui.SUB, tx, row.Y + Ui.Px(25));
                    }
                };
                list.DoubleClick += delegate
                {
                    if (list.SelectedIndex >= 0) { result = list.SelectedIndex; d.DialogResult = DialogResult.OK; d.Close(); }
                };
                d.Body.Controls.Add(list);

                d.Button("确定", true, DialogResult.OK).Click += delegate
                {
                    result = list.SelectedIndex;
                };
                d.Button("取消", false, DialogResult.Cancel);
                d.FitBody(bodyH + 16);
                if (d.ShowDialog(owner) == DialogResult.OK) return result;
                return -1;
            }
        }

        // ===== 表单（多字段输入）=====
        /// <summary>返回各字段的值，取消返回 null。secret[i] = true 的字段用密码框。</summary>
        public static string[] Form(IWin32Window owner, string title, string sub,
            string[] labels, string[] values, bool[] secret = null,
            string okText = "确定", string cancelText = "取消", int widthDp = 460, string hint = null)
        {
            int y = Ui.Px(8);
            var boxes = new List<Input>();
            using (var d = new DlgForm(title, sub, widthDp))
            {
                if (!string.IsNullOrEmpty(hint))
                {
                    var lb = new Label
                    {
                        Text = hint,
                        Font = Ui.F(8.5f),
                        ForeColor = Ui.SUB,
                        AutoSize = false,
                        BackColor = Color.Transparent,
                        Location = new Point(Ui.Px(d.Pad), y),
                        Size = new Size(d.BodyWidth, Ui.Px(34))
                    };
                    d.Body.Controls.Add(lb);
                    y += Ui.Px(38);
                }

                for (int i = 0; i < labels.Length; i++)
                {
                    var lab = new Label
                    {
                        Text = labels[i],
                        Font = Ui.F(9f),
                        ForeColor = Ui.SUB,
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        Location = new Point(Ui.Px(d.Pad), y + Ui.Px(2))
                    };
                    d.Body.Controls.Add(lab);
                    y += Ui.Px(24);

                    var tb = new Input();
                    tb.Text = (values != null && i < values.Length) ? (values[i] ?? "") : "";
                    tb.Font = Ui.F(10f);
                    tb.Location = new Point(Ui.Px(d.Pad), y);
                    tb.Size = new Size(d.BodyWidth, Ui.Px(36));
                    if (secret != null && i < secret.Length && secret[i]) tb.Secret = true;
                    d.Body.Controls.Add(tb);
                    boxes.Add(tb);
                    y += Ui.Px(48);
                }

                d.Button(okText, true, DialogResult.OK);
                d.Button(cancelText, false, DialogResult.Cancel);
                d.FitBody(y / Ui.Px(1) + 8 + 8);
                if (boxes.Count > 0) d.AcceptButton = null;
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                var outp = new string[boxes.Count];
                for (int i = 0; i < boxes.Count; i++) outp[i] = boxes[i].Text;
                return outp;
            }
        }

        // ===== 等着（后台干活）=====
        /// <summary>把活放到后台线程跑，UI 上显示一句提示；干完自动关。</summary>
        public static void Wait(IWin32Window owner, string title, string sub, Action job)
        {
            using (var d = new DlgForm(title, sub, 380))
            {
                var spin = new Label
                {
                    Text = "请稍候…",
                    Font = Ui.F(9.5f),
                    ForeColor = Ui.SUB,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };
                d.Body.Controls.Add(spin);
                d.FitBody(70);
                d.Shown += delegate
                {
                    var t = new Thread(delegate()
                    {
                        try { job(); }
                        catch (Exception ex) { LastError = ex; }
                        finally
                        {
                            try { d.BeginInvoke((MethodInvoker)delegate { d.Close(); }); }
                            catch { }
                        }
                    });
                    t.IsBackground = true;
                    t.Start();
                };
                d.ShowDialog(owner);
            }
        }

        /// <summary>Wait 里后台任务抛出的异常（供调用方判断成败）。</summary>
        public static Exception LastError;
    }
}
