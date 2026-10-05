#!/usr/bin/env python3
"""往 mock AnkiConnect 里塞一张测试卡片（真机浏览页联调用）。"""
import json
import sys
import urllib.request

URL = "http://127.0.0.1:8765"


def call(action, params=None):
    body = json.dumps({"action": action, "version": 6, "params": params or {}},
                      ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(URL, data=body,
                                headers={"Content-Type": "application/json; charset=utf-8"})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode("utf-8"))


fields = {
    "单词": "velocity",
    "音标": "/vəˈlɒsəti/",
    "词性": "n.",
    "定义": "(n.) the rate of change of displacement with respect to time; a vector quantity",
    "关联公式/符号": "\\[ \\vec{v} = \\dfrac{\\Delta \\vec{s}}{\\Delta t} \\]",
    "易混": "speed /spiːd/ — 速率，标量，无方向<br>acceleration /əkˌseləˈreɪʃn/ — 加速度，速度的变化率",
}

print(call("addNote", {"note": {
    "deckName": "NCUK::专业术语",
    "modelName": "专业术语卡",
    "fields": fields,
    "tags": ["自建", "物理"],
    "options": {"allowDuplicate": True},
}}))

fields2 = dict(fields)
fields2["单词"] = "probability"
fields2["音标"] = "/ˌprɒbəˈbɪləti/"
fields2["定义"] = "(n.) the extent to which an event is likely to occur"
fields2["关联公式/符号"] = "\\(P(A)=\\dfrac{n(A)}{n(S)}\\)"
fields2["易混"] = "probably /ˈprɒbəbli/ — 大概（副词）<br>possibility /ˌpɒsəˈbɪləti/ — 可能性"
print(call("addNote", {"note": {
    "deckName": "NCUK::专业术语",
    "modelName": "专业术语卡",
    "fields": fields2,
    "tags": ["自建", "数学"],
    "options": {"allowDuplicate": True},
}}))
sys.exit(0)
