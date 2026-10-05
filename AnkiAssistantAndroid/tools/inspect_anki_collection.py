#!/usr/bin/env python3
"""读取本机 Anki 收藏库，看清用户「已有的内容」——尤其是公式的写法。

只读：先把 collection.anki2(-wal) 复制到临时目录再打开，绝不碰原库。

新版 Anki（schema 18）里：
  * 笔记类型在 notetypes / templates / fields 表，不在 col.models
  * notes.flds 与 notes.tags 是 **zstd 压缩的 BLOB**，需要 zstandard 才能读
    （没装的话脚本依然会打印笔记类型/模板/样式）

用法:
    python inspect_anki_collection.py
    # 需要看笔记正文时：
    python -m pip install --target tools/lib/py zstandard
"""
import json
import os
import shutil
import sqlite3
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lib", "py"))

APP = os.path.join(os.environ.get("APPDATA", ""), "Anki2")
PROFILE = "账户 1"
MATH_MARKERS = ["[latex]", "[/latex]", "[$]", "[/$]", "\\(", "\\[", "$$",
                "anki-mathjax", "mathjax", "\\begin{"]


def find_collection():
    base = os.path.join(APP, PROFILE)
    for name in ("collection.anki21", "collection.anki2"):
        p = os.path.join(base, name)
        if os.path.isfile(p):
            return p
    return None


def decompress(blob):
    if isinstance(blob, str):
        return blob
    try:
        import zstandard
        return zstandard.ZstdDecompressor().decompress(blob).decode("utf-8")
    except Exception:
        return None


def main():
    src = find_collection()
    if not src:
        print("找不到收藏库 under", os.path.join(APP, PROFILE), file=sys.stderr)
        return 1
    tmpdir = tempfile.mkdtemp(prefix="ankidump-")
    dst = os.path.join(tmpdir, os.path.basename(src))
    shutil.copyfile(src, dst)
    for suffix in ("-wal", "-shm"):
        if os.path.isfile(src + suffix):
            shutil.copyfile(src + suffix, dst + suffix)

    con = sqlite3.connect(dst)
    cur = con.cursor()
    print("库:", src)
    try:
        print("schema:", cur.execute("select ver from col").fetchone()[0])
    except Exception:
        print("schema: ?")

    # ---------------- 笔记类型 ----------------
    print("\n=== 笔记类型 ===")
    ntypes = {}
    try:
        for ntid, name in cur.execute("select id, name from notetypes"):
            ntypes[ntid] = name
            print("- %s (id=%s)" % (name, ntid))
            try:
                for fname, ordv in cur.execute(
                        "select name, ord from fields where ntid=? order by ord", (ntid,)):
                    print("    字段: %s" % fname)
            except Exception:
                pass
            try:
                for tname, qfmt, afmt in cur.execute(
                        "select name, qfmt, afmt from templates where ntid=?", (ntid,)):
                    print("    模板 %s" % tname)
                    print("      Front:", (qfmt or "").replace("\n", " ")[:200])
                    print("      Back :", (afmt or "").replace("\n", " ")[:200])
            except Exception:
                pass
            try:
                cfg = cur.execute("select config from notetypes where id=?", (ntid,)).fetchone()
                conf = json.loads(cfg[0]) if cfg and cfg[0] else {}
                css = (conf.get("css") or "").replace("\n", " ")
                if css:
                    print("    CSS  :", css[:400])
            except Exception as e:
                print("    (CSS 读取失败: %s)" % e)
            print()
    except Exception as e:
        print("  notetypes 读取失败:", e)

    # 兼容老库
    try:
        models = json.loads(cur.execute("select models from col").fetchone()[0] or "{}")
        for mid, m in models.items():
            ntypes[int(mid)] = m.get("name")
            print("- (col.models) %s 字段=%s" % (m.get("name"),
                  [f["name"] for f in m.get("flds", [])]))
    except Exception:
        pass

    # ---------------- 牌组 ----------------
    print("=== 牌组 ===")
    try:
        print(" ", sorted(n for (n,) in cur.execute("select name from decks")))
    except Exception:
        try:
            decks = json.loads(cur.execute("select decks from col").fetchone()[0])
            print(" ", sorted(d.get("name") for d in decks.values()))
        except Exception as e:
            print("  读取失败", e)

    # ---------------- 笔记 ----------------
    total = cur.execute("select count(*) from notes").fetchone()[0]
    print("\n=== 笔记总数: %d ===" % total)

    print("\n=== 含公式标记的笔记（最多 10 条） ===")
    shown = 0
    zstd_ok = True
    for nid, mid, flds, tags in cur.execute("select id, mid, flds, tags from notes"):
        f = decompress(flds)
        if f is None:
            zstd_ok = False
            break
        if any(mk in f for mk in MATH_MARKERS):
            fields = f.split("\x1f")
            print("--- note %s  类型=%s" % (nid, ntypes.get(mid, mid)))
            for i, v in enumerate(fields):
                print("    [%d] %s" % (i, v[:420].replace("\n", " ")))
            print()
            shown += 1
            if shown >= 10:
                break
    if shown == 0:
        print("  （没有；%s）" % ("正文无法解压，请装 zstandard" if not zstd_ok else "确实没有公式"))

    print("=== 样例笔记（最多 5 条） ===")
    if zstd_ok:
        for nid, mid, flds in cur.execute("select id, mid, flds from notes limit 5"):
            f = decompress(flds)
            print("--- %s (%s)" % (nid, ntypes.get(mid, mid)))
            for i, v in enumerate(f.split("\x1f")):
                print("    [%d] %s" % (i, v[:220].replace("\n", " ")))
            print()

    con.close()
    shutil.rmtree(tmpdir, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
