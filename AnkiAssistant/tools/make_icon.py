# -*- coding: utf-8 -*-
"""Generate assets\\app.ico for the Windows build from the Android launcher icon.

Usage:  python tools\\make_icon.py

The Android project keeps the artwork in
    ..\\AnkiAssistantAndroid\\res\\mipmap-*\\ic_launcher.png
and we simply repack the biggest one into a multi-size Windows .ico
(16/24/32/48/64/128/256), which is what Explorer and the taskbar pick from.
"""
import os
import sys

try:
    from PIL import Image
except ImportError:
    sys.stderr.write("需要 Pillow：pip install pillow\n")
    raise SystemExit(1)

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                       # ...\AnkiAssistant\AnkiAssistant
ANDROID = os.path.join(os.path.dirname(ROOT), "AnkiAssistantAndroid", "res")

CANDIDATES = [
    os.path.join(ANDROID, "mipmap-xxxhdpi", "ic_launcher.png"),
    os.path.join(ANDROID, "mipmap-xxhdpi", "ic_launcher.png"),
    os.path.join(ANDROID, "mipmap-xhdpi", "ic_launcher.png"),
    os.path.join(ANDROID, "mipmap-hdpi", "ic_launcher.png"),
    os.path.join(ANDROID, "mipmap-mdpi", "ic_launcher.png"),
    os.path.join(ROOT, "assets", "app.png"),
]

SIZES = [16, 24, 32, 48, 64, 128, 256]


def main():
    src = None
    for p in CANDIDATES:
        if os.path.isfile(p):
            src = p
            break
    if src is None:
        sys.stderr.write("找不到源图，试过：\n  " + "\n  ".join(CANDIDATES) + "\n")
        return 1

    img = Image.open(src).convert("RGBA")
    out_dir = os.path.join(ROOT, "assets")
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)
    out = os.path.join(out_dir, "app.ico")

    # Pillow writes every requested size into one .ico when given `sizes`.
    biggest = max(SIZES)
    if img.width != img.height:
        side = min(img.width, img.height)
        left = (img.width - side) // 2
        top = (img.height - side) // 2
        img = img.crop((left, top, left + side, top + side))
    if img.width != biggest:
        img = img.resize((biggest, biggest), Image.LANCZOS)
    img.save(out, format="ICO", sizes=[(s, s) for s in SIZES])

    print("src : " + src)
    print("out : " + out + "  (" + str(os.path.getsize(out)) + " bytes, " +
          "/".join(str(s) for s in SIZES) + ")")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
