#!/usr/bin/env python3
"""核对：新保存的卡片是否落在目标牌组，且字段/笔记类型正确。"""
import json
import sys
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")
URL = "http://127.0.0.1:8765"


def call(a, p=None):
    b = json.dumps({"action": a, "version": 6, "params": p or {}}, ensure_ascii=False).encode()
    r = urllib.request.Request(URL, data=b, headers={"Content-Type": "application/json; charset=utf-8"})
    return json.loads(urllib.request.urlopen(r, timeout=15).read().decode())


print("牌组:", json.dumps(call("deckNames")["result"], ensure_ascii=False))
print("笔记类型:", json.dumps(call("modelNames")["result"], ensure_ascii=False))
ids = call("findNotes", {"query": 'deck:"NCUK::专业术语"'})["result"]
print("目标牌组里的笔记:", ids)
if not ids:
    ids = call("findNotes", {"query": "deck:*"})["result"][-3:]
    print("（目标牌组为空，改看最近 3 条）:", ids)
for nid in ids:
    info = call("notesInfo", {"notes": [nid]})["result"][0]
    cards = call("cardsInfo", {"cards": info["cards"]})["result"]
    for c in cards:
        print("  note %s card %s  deck=%s  word=%s" % (
            nid, c["cardId"], c["deckName"], c["fields"].get("单词", {}).get("value")))
    print("     字段:", {k: v["value"][:40] for k, v in info["fields"].items()})
PYEOF_MARKER = None
