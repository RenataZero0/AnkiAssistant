#!/usr/bin/env python3
"""把 MathJax 资源扁平化到 assets 根目录。

为什么必须扁平：
    aapt2 在 Windows 上会把「嵌套目录」的 assets 用反斜杠写进 APK
    （APK 里真实存在的是 assets/mathjax\\tex-mml-chtml.js），
    Android 的 AssetManager 把反斜杠当成文件名字符，于是
    assets.open("mathjax/tex-mml-chtml.js") 直接 FileNotFoundException，
    list("mathjax") 返回空 —— 表现为 WebView 里脚本/字体 404、页面空白。
    根目录下的文件没有分隔符，所以一直是好的（editor.html 就没事）。

    所以：所有文件平铺在 assets 根目录，URL 路径 -> 资源名的映射放在
    AssetServer.java 里（MathJax 只认固定的 URL 路径，不能改名）。

用法: python prepare_assets.py
"""
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "assets")
SRC = os.path.join(ASSETS, "mathjax")
FONT_DIR = os.path.join(SRC, "output", "chtml", "fonts", "woff-v2")

JS_OUT = "mj-tex-mml-chtml.js"
FONT_PREFIX = "mj-woff-"


def main():
    if not os.path.isdir(SRC):
        print("assets/mathjax 不存在：可能已经扁平化过了")
        return 0

    js = os.path.join(SRC, "tex-mml-chtml.js")
    if not os.path.isfile(js):
        print("缺少 %s" % js, file=sys.stderr)
        return 1
    shutil.copyfile(js, os.path.join(ASSETS, JS_OUT))
    print("->", JS_OUT, os.path.getsize(js), "bytes")

    if os.path.isdir(FONT_DIR):
        n = 0
        for name in sorted(os.listdir(FONT_DIR)):
            if not name.lower().endswith((".woff", ".woff2")):
                continue
            shutil.copyfile(os.path.join(FONT_DIR, name),
                            os.path.join(ASSETS, FONT_PREFIX + name))
            n += 1
        print("->", n, "fonts")
    else:
        print("缺少字体目录 %s" % FONT_DIR, file=sys.stderr)
        return 1

    shutil.rmtree(SRC)
    print("removed assets/mathjax (扁平化完成)")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
