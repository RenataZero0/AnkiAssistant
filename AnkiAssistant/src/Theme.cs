using System;
using System.Collections.Generic;
using System.Drawing;

namespace AnkiAssistant
{
    /// <summary>
    /// 一套可切换的调色板。和 Android 版 Theme.java 一一对应，
    /// 皮肤 id / 名称 / 颜色完全相同，方便两端看着一样。
    ///
    /// 用法：启动时 <see cref="Apply"/> 当前主题，把颜色灌进 <see cref="Ui"/> 的静态字段；
    /// 用户换皮肤后重建界面即可。设置里存的是 id（blue / forest / sunset / violet / dark）。
    /// </summary>
    public class Theme
    {
        public readonly string Id;
        public readonly string Name;
        public readonly bool Dark;

        public readonly Color Bg, Card, Panel, Ink, Sub, Line;
        public readonly Color Accent, AccentDark, AccentSoft;
        public readonly Color Green, GreenSoft, Amber, AmberSoft, Red, RedSoft;
        public readonly Color TextBody, TextDim;

        Theme(string id, string name, bool dark,
              uint bg, uint card, uint panel, uint ink, uint sub, uint line,
              uint accent, uint accentDark, uint accentSoft,
              uint green, uint greenSoft, uint amber, uint amberSoft, uint red, uint redSoft,
              uint textBody, uint textDim)
        {
            Id = id; Name = name; Dark = dark;
            Bg = Argb(bg); Card = Argb(card); Panel = Argb(panel);
            Ink = Argb(ink); Sub = Argb(sub); Line = Argb(line);
            Accent = Argb(accent); AccentDark = Argb(accentDark); AccentSoft = Argb(accentSoft);
            Green = Argb(green); GreenSoft = Argb(greenSoft);
            Amber = Argb(amber); AmberSoft = Argb(amberSoft);
            Red = Argb(red); RedSoft = Argb(redSoft);
            TextBody = Argb(textBody); TextDim = Argb(textDim);
        }

        /// <summary>把 0xAARRGGBB 转成 Color（和 Android 的写法保持一致，方便对照）。</summary>
        static Color Argb(uint v)
        {
            return Color.FromArgb((int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
        }

        /// <summary>全部皮肤（顺序就是设置里的顺序）。</summary>
        public static List<Theme> All()
        {
            var out_ = new List<Theme>();
            out_.Add(new Theme("blue", "默认蓝", false,
                0xFFF3F5F9, 0xFFFFFFFF, 0xFFF6F9FE, 0xFF1B2432, 0xFF71809A, 0xFFE5E9F0,
                0xFF3568E8, 0xFF2B52BC, 0xFFE8EFFE,
                0xFF21A366, 0xFFE4F5EC, 0xFFDE9420, 0xFFFDF2DF, 0xFFE0533F, 0xFFFBE9E5,
                0xFF3C4A60, 0xFF9AA6B8));
            out_.Add(new Theme("forest", "森林绿", false,
                0xFFF1F6F2, 0xFFFFFFFF, 0xFFF3F8F4, 0xFF17241C, 0xFF6C8377, 0xFFDEE8E1,
                0xFF1F9D6B, 0xFF157C53, 0xFFE2F4EC,
                0xFF1F9D6B, 0xFFE2F4EC, 0xFFCB8A22, 0xFFFBF1DD, 0xFFD5503C, 0xFFFBE7E3,
                0xFF354A3E, 0xFF93A79A));
            out_.Add(new Theme("sunset", "暖阳橙", false,
                0xFFFAF6F1, 0xFFFFFFFF, 0xFFFBF6F0, 0xFF2A211A, 0xFF8A7A69, 0xFFEDE4D9,
                0xFFE07A2F, 0xFFBC5F19, 0xFFFDEDDD,
                0xFF2F9E63, 0xFFE3F3EA, 0xFFD08A1E, 0xFFFCEFDB, 0xFFD0503C, 0xFFFBE6E1,
                0xFF4C4034, 0xFFA79A8B));
            out_.Add(new Theme("violet", "玫瑰紫", false,
                0xFFF6F4FB, 0xFFFFFFFF, 0xFFF7F5FC, 0xFF211B33, 0xFF7E7595, 0xFFE6E1F2,
                0xFF7C5CFF, 0xFF5F42D8, 0xFFEDE8FF,
                0xFF2AA36B, 0xFFE3F5EB, 0xFFD5931F, 0xFFFCF0DA, 0xFFDC4F58, 0xFFFBE7E9,
                0xFF3F3856, 0xFF9C93B2));
            out_.Add(new Theme("dark", "深色", true,
                0xFF10131A, 0xFF181C25, 0xFF20252F, 0xFFE9EEF7, 0xFF9AA6BC, 0xFF2A3040,
                0xFF6E9BFF, 0xFF4E7BEC, 0xFF1E2A44,
                0xFF43C08B, 0xFF14301F, 0xFFE0A94A, 0xFF33280F, 0xFFFF7B72, 0xFF3A1D1A,
                0xFFC6CEDC, 0xFF7C8798));
            return out_;
        }

        public static Theme Default { get { return All()[0]; } }

        /// <summary>按 id 取；找不到就返回默认（存的是老 id 也不会白屏）。</summary>
        public static Theme Get(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (Theme t in All())
                    if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return t;
            return Default;
        }

        /// <summary>半透明版本（用于浅色底），和 Android 的 Ui.alpha 一致。</summary>
        public static Color Alpha(Color c, int a)
        {
            if (a < 0) a = 0; if (a > 255) a = 255;
            return Color.FromArgb(a, c);
        }

        /// <summary>把整套颜色写进 Ui 的静态调色板（界面上所有颜色都从那里取）。</summary>
        public static void Apply(Theme t)
        {
            if (t == null) t = Default;
            Ui.BG = t.Bg;
            Ui.CARD = t.Card;
            Ui.PANEL = t.Panel;
            Ui.INK = t.Ink;
            Ui.SUB = t.Sub;
            Ui.LINE = t.Line;
            Ui.ACCENT = t.Accent;
            Ui.ACCENT_DARK = t.AccentDark;
            Ui.ACCENT_SOFT = t.AccentSoft;
            Ui.GREEN = t.Green;
            Ui.GREEN_SOFT = t.GreenSoft;
            Ui.AMBER = t.Amber;
            Ui.AMBER_SOFT = t.AmberSoft;
            Ui.RED = t.Red;
            Ui.RED_SOFT = t.RedSoft;
            Ui.TEXT_BODY = t.TextBody;
            Ui.TEXT_DIM = t.TextDim;
            Ui.WHITE = Color.White;
            Current = t;
        }

        public static Theme Current { get; private set; }
    }
}
