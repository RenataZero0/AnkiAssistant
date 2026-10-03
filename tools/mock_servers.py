#!/usr/bin/env python3
"""联调用的两个 mock 服务（本机跑，通过 `adb reverse` 让平板访问到）。

1) AnkiConnect mock   -> 127.0.0.1:8765   模拟桌面版 Anki + AnkiConnect 插件
2) OpenAI 兼容 mock   -> 127.0.0.1:8899   模拟 AI 的 /chat/completions

用途：不装 Anki、不花 API 额度，也能把「AI 填充 -> 保存到 Anki -> 浏览卡片」
整条链路在真机上跑一遍。所有请求都会打到 tools/mock_log.txt，方便核对 App 发出的报文。

用法:
    python mock_servers.py
    adb reverse tcp:8765 tcp:8765
    adb reverse tcp:8899 tcp:8899
"""
import json
import os
import re
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, "mock_log.txt")

_lock = threading.Lock()
_notes = []
_models = []
_next_id = [1000]


def log(tag, text):
    with _lock:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write("[%s] %s\n%s\n\n" % (time.strftime("%H:%M:%S"), tag, text))


def canned_ai(word):
    """返回一份符合 PRMOPT 第二节第 3 条格式的假回复（带 MathJax 公式，可验证渲染）。"""
    w = word.strip()
    low = w.lower()
    table = {
        "probability": {
            "phonetic": "英 /ˌprɒbəˈbɪləti/；美 /ˌprɑːbəˈbɪləti/",
            "pos": "n",
            "definition": "(n) the extent to which an event is likely to occur, measured by the ratio of favourable cases to the total number of equally likely cases",
            "formula": "\\(P(A)=\\dfrac{n(A)}{n(S)}\\)",
            "confusables": "probably /ˈprɒbəbli/ — 大概（副词），与原词只差词性\n"
                           "probity /ˈprəʊbəti/ — 正直、诚实，拼写相近意思完全不同\n"
                           "possibility /ˌpɒsəˈbɪləti/ — 可能性，强调「是否可行」",
            "chinese": "概率，可能性",
        },
        "velocity": {
            "phonetic": "英 /vəˈlɒsəti/；美 /vəˈlɑːsəti/",
            "pos": "n",
            "definition": "(n) the rate of change of displacement with respect to time; a vector quantity",
            "formula": "\\(\\vec{v}=\\dfrac{\\Delta \\vec{s}}{\\Delta t}\\)",
            "confusables": "speed /spiːd/ — 速率，标量，无方向\n"
                           "acceleration /əkˌseləˈreɪʃn/ — 加速度，速度的变化率",
            "chinese": "速度（矢量）",
        },
    }
    if low in table:
        d = table[low]
    else:
        d = {
            "phonetic": "英 /%s/；美 /%s/" % (low, low),
            "pos": "n",
            "definition": "(n) mock definition for %s used by the local self-test server" % w,
            "formula": "\\(x=\\dfrac{a}{b}\\)",
            "confusables": "%s-s /%s-s/ — 复数形式\n%sion /%sʃn/ — 词尾相近的派生词" % (w, low, w, low),
            "chinese": "（mock）中文释义",
        }
    return json.dumps(d, ensure_ascii=False)


# --------------------------------------------------------------------- AnkiConnect

class AnkiHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, fmt, *args):
        pass

    def do_GET(self):
        self._send(200, "Anki-Connect")

    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(n).decode("utf-8")
        try:
            req = json.loads(raw)
        except Exception as e:
            self._send(200, json.dumps({"result": None, "error": "bad json: %s" % e}))
            return
        action = req.get("action")
        params = req.get("params") or {}
        log("AnkiConnect", "%s %s" % (action, json.dumps(params, ensure_ascii=False)[:1200]))
        try:
            result = self.dispatch(action, params)
            self._send(200, json.dumps({"result": result, "error": None}, ensure_ascii=False))
        except Exception as e:
            self._send(200, json.dumps({"result": None, "error": str(e)}))

    def dispatch(self, action, p):
        if action == "version":
            return 6
        if action == "requestPermission":
            return {"permission": "granted"}
        if action == "deckNames":
            decks = ["Default"]
            for n in _notes:
                if n["deck"] not in decks:
                    decks.append(n["deck"])
            return decks
        if action == "modelNames":
            return _models
        if action == "createDeck":
            return p.get("name")
        if action == "createModel":
            # 把 App 传来的笔记类型定义原样记下来，供人工核对
            log("createModel", json.dumps(p, ensure_ascii=False, indent=1)[:4000])
            _models.append(p.get("modelName"))
            return None
        if action == "sync":
            log("sync", "(App 请求同步到 AnkiWeb 云端)")
            return None
        if action == "addNote":
            note = p.get("note") or {}
            _next_id[0] += 1
            rec = {
                "noteId": _next_id[0],
                "modelName": note.get("modelName", "专业术语卡"),
                "tags": note.get("tags") or [],
                "fields": {},
                "deck": note.get("deckName", "Default"),
            }
            fields = note.get("fields") or {}
            order = 0
            for k, v in fields.items():
                rec["fields"][k] = {"value": v, "order": order}
                order += 1
            _notes.append(rec)
            return rec["noteId"]
        if action == "findNotes":
            q = p.get("query", "")
            deck = None
            m = re.search(r'deck:"([^"]+)"', q)
            if m:
                deck = m.group(1)
            text = re.sub(r'deck:"[^"]+"', " ", q)
            text = re.sub(r'deck:\S+', " ", text).strip()
            out = []
            for n in _notes:
                if deck and n["deck"] != deck:
                    continue
                if text and text != "*":
                    blob = " ".join(str(v.get("value", "")) for v in n["fields"].values())
                    if text.lower() not in blob.lower():
                        continue
                out.append(n["noteId"])
            return out
        if action == "notesInfo":
            ids = p.get("notes") or []
            out = []
            for n in _notes:
                if n["noteId"] in ids:
                    out.append({
                        "noteId": n["noteId"],
                        "modelName": n["modelName"],
                        "tags": n["tags"],
                        "fields": n["fields"],
                        "cards": [],
                    })
            return out
        if action == "deleteNotes":
            ids = set(p.get("notes") or [])
            before = len(_notes)
            _notes[:] = [n for n in _notes if n["noteId"] not in ids]
            return [i for i in ids] if len(_notes) != before else []
        if action == "guiEditNote":
            return None
        raise Exception("mock 未实现的动作: %s" % action)

    def _send(self, code, text):
        data = text.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


# --------------------------------------------------------------------- AI

class AiHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, fmt, *args):
        pass

    def do_GET(self):
        self._send(200, "ai-mock")

    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(n).decode("utf-8")
        log("AI", "%s %s" % (self.path, raw[:1500]))
        try:
            req = json.loads(raw)
            msgs = req.get("messages") or []
            user = ""
            for m in msgs:
                if m.get("role") == "user":
                    user = m.get("content", "")
            m = re.search(r"「([^」]+)」", user)
            if m:
                content = canned_ai(m.group(1))
            else:
                content = "可用"
            body = {
                "id": "mock-1",
                "object": "chat.completion",
                "model": req.get("model", "mock"),
                "choices": [{"index": 0, "message": {"role": "assistant", "content": content},
                             "finish_reason": "stop"}],
            }
            self._send(200, json.dumps(body, ensure_ascii=False))
        except Exception as e:
            self._send(500, json.dumps({"error": {"message": str(e)}}, ensure_ascii=False))

    def _send(self, code, text):
        data = text.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def serve(port, handler):
    srv = ThreadingHTTPServer(("0.0.0.0", port), handler)
    t = threading.Thread(target=srv.serve_forever, daemon=True)
    t.start()
    return srv


def main():
    if os.path.exists(LOG):
        os.remove(LOG)
    serve(8765, AnkiHandler)
    serve(8899, AiHandler)
    print("AnkiConnect mock : http://127.0.0.1:8765")
    print("AI mock          : http://127.0.0.1:8899/chat/completions")
    print("log              :", LOG)
    print("按 Ctrl+C 退出")
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
