using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnkiAssistant
{
    /// <summary>
    /// 换皮肤：调色板是静态的，改完必须把整个界面重建一遍才生效。
    ///
    /// 重建不能在按钮的点击处理里直接做（老窗口正在处理事件，Dispose 会崩），
    /// 所以用 BeginInvoke 排到当前这轮消息之后。
    /// </summary>
    public static class ThemeSwap
    {
        public static void Apply(string id)
        {
            Store.ThemeId = id;
            Theme.Apply(Theme.Get(id));

            MainForm old = MainForm.Instance;
            if (old == null) return;

            old.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    var f = new MainForm();
                    f.StartPosition = FormStartPosition.Manual;
                    if (old.WindowState == FormWindowState.Normal)
                    {
                        f.Location = old.Location;
                        f.Size = old.Size;
                    }
                    else
                    {
                        f.WindowState = old.WindowState;
                    }
                    f.Show();
                    old.Hide();
                    old.Dispose();
                }
                catch { }
            });
        }
    }
}
