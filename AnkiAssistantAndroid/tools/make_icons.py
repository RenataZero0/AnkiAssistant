#!/usr/bin/env python3
"""生成 Anki 助手的启动图标（蓝色渐变圆角方块 + 白色卡片组）。

用法: python make_icons.py
输出: res/mipmap-*/ic_launcher.png 与 preview/ic_launcher-512.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
RES = os.path.join(ROOT, "res")
PREVIEW = os.path.join(ROOT, "preview")

SS = 6  # supersample

TOP = (94, 146, 245)
BOT = (43, 86, 219)
WHITE = (255, 255, 255)
ACCENT = (47, 96, 224)
DECK = (214, 228, 253)

DENSITIES = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}


def gradient(size):
    im = Image.new("RGB", (1, size))
    d = ImageDraw.Draw(im)
    for y in range(size):
        t = y / max(1, size - 1)
        d.point((0, y), tuple(int(TOP[i] + (BOT[i] - TOP[i]) * t) for i in range(3)))
    return im.resize((size, size), Image.BILINEAR)


def squircle_mask(size, ratio=0.235):
    s = size * SS
    m = Image.new("L", (s, s), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * ratio), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def draw_card(d, box, fill, outline=None, width=0):
    d.rounded_rectangle(box, radius=int((box[2] - box[0]) * 0.16), fill=fill,
                        outline=outline, width=width)


def icon(size, square=False):
    s = size * SS
    im = gradient(size).convert("RGBA")
    if square:
        im.putalpha(Image.new("L", (size, size), 255))
    else:
        im.putalpha(squircle_mask(size))

    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    u = s / 24.0

    # 背后那张卡（浅蓝，表示"牌组"）
    draw_card(d, (7.2 * u, 5.0 * u, 21.5 * u, 15.5 * u), DECK)

    # 前面那张卡（白）
    draw_card(d, (2.6 * u, 8.4 * u, 17.8 * u, 20.6 * u), WHITE)

    # 卡片上的两行文字
    lw = max(2, int(1.35 * u))
    d.line([(5.4 * u, 12.4 * u), (13.6 * u, 12.4 * u)], fill=ACCENT, width=lw)
    d.line([(5.4 * u, 15.6 * u), (10.6 * u, 15.6 * u)], fill=(150, 170, 210), width=lw)

    # 右上角的加号
    r = 2.9 * u
    cx, cy = 18.6 * u, 17.4 * u
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ACCENT)
    t = max(2, int(1.2 * u))
    d.line([(cx - r * 0.5, cy), (cx + r * 0.5, cy)], fill=WHITE, width=t)
    d.line([(cx, cy - r * 0.5), (cx, cy + r * 0.5)], fill=WHITE, width=t)

    out = layer.resize((size, size), Image.LANCZOS)
    composed = Image.alpha_composite(im, out)
    # 圆角裁剪（alpha 通道已经是 squircle）
    composed.putalpha(squircle_mask(size) if not square else Image.new("L", (size, size), 255))
    return composed


def main():
    for name in DENSITIES:
        folder = os.path.join(RES, "mipmap-" + name)
        os.makedirs(folder, exist_ok=True)
        px = DENSITIES[name]
        icon(px).save(os.path.join(folder, "ic_launcher.png"))
        # Android 5-7 也接受方版，但我们统一用圆角版即可
        print("icon", name, px)

    os.makedirs(PREVIEW, exist_ok=True)
    icon(512).save(os.path.join(PREVIEW, "ic_launcher-512.png"))
    print("preview 512")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
