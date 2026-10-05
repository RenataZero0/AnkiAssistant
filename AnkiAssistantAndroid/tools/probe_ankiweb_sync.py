"""手动复现 AnkiWeb 同步请求，看服务器到底返回什么（只读：不会上传任何东西）。

用法：
  python tools/probe_ankiweb_sync.py <邮箱> <密码>

会做：
  1. POST /sync/hostKey  拿 hkey
  2. POST /sync/meta     看需要哪种同步
  3. POST /sync/download 复现我们遇到的那一步，并打印状态码/响应头/原始响应体
"""
import json
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant\tools\lib\py")

import urllib.request
import urllib.error

try:
    import zstandard as zstd
except ImportError:
    print("需要 zstandard：pip install --target tools/lib/py zstandard")
    raise

BASE = "https://sync.ankiweb.net/sync/"
CLIENT_VER = "anki,25.07 (test)"   # 只用于探测
SESSION = "abcdefgh"

CCTX = zstd.ZstdCompressor()
DCTX = zstd.ZstdDecompressor()


def call(method, payload, hkey="", session=SESSION, version=11, timeout=40):
    url = BASE + method
    raw = json.dumps(payload).encode() if payload is not None else b""
    body = CCTX.compress(raw)
    header = json.dumps({"v": version, "k": hkey, "c": CLIENT_VER, "s": session})
    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("anki-sync", header)
    req.add_header("Content-Type", "application/octet-stream")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read()
            return resp.status, dict(resp.headers), data
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read()


def decode(data):
    try:
        return DCTX.decompress(data, max_output_size=64 * 1024 * 1024)
    except Exception as e:
        return b"<not zstd: %s> %r" % (str(e).encode(), data[:200])


def main():
    user, pw = sys.argv[1], sys.argv[2]

    print("=== 1) hostKey ===")
    code, hdrs, data = call("hostKey", {"u": user, "p": pw}, hkey="")
    print("status:", code, " anki-original-size:", hdrs.get("anki-original-size"))
    body = decode(data)
    print("body:", body[:300])
    try:
        hkey = json.loads(body)["key"]
    except Exception:
        print("登录失败，终止")
        return
    print("hkey len:", len(hkey))

    print()
    print("=== 2) meta ===")
    code, hdrs, data = call("meta", {"v": 11, "cv": CLIENT_VER}, hkey=hkey)
    print("status:", code, " anki-original-size:", hdrs.get("anki-original-size"))
    print("body:", decode(data)[:400])

    print()
    print("=== 3) download（复现失败的那一步）===")
    code, hdrs, data = call("download", None, hkey=hkey)
    print("status:", code)
    print("headers:", json.dumps({k: v for k, v in hdrs.items()}, ensure_ascii=False)[:600])
    if code == 200 and hdrs.get("anki-original-size"):
        print("下载成功，字节数:", len(data), "（压缩后）")
    else:
        print("body:", decode(data)[:600])


if __name__ == "__main__":
    main()
