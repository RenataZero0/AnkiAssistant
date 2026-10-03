#!/usr/bin/env python3
"""更严谨的"开/关思考"对比：用 raw_decode 解析（和应用 parseAi 一样容忍代码围栏），
并显式检测"输出被截断"。结果全量落盘到 tools/_thinking_eval.json 供人工复核。"""
import json
import re
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")
KEY = open(r"D:\DeepseekHarness\apikey.txt", encoding="utf-8").read().split("zhipu")[1].strip().splitlines()[0].strip()

SYSTEM = ("你是资深的英汉词典编辑，同时熟悉 CIE A-Level / NCUK IFY 的数学与物理术语。"
          "你只输出一个 JSON 对象，不输出任何解释、注释或 Markdown 代码块。")
TEMPLATE = """请为单词「{w}」生成一张词卡。
学科背景：CIE A-Level / NCUK IFY 数学、物理术语。

只输出一个 JSON 对象，字段如下（值都是字符串，不要用 markdown）：
phonetic：音标，英式与美式都给，写成 英 /.../；美 /.../
pos：词性缩写，多个用 / 连接，例如 adj/n、v、n，不要加点号。
definition：英文释义。严格按这个格式：([词性]) 英文释义; ([词性]) 另一个释义
formula：与该词相关的公式或符号表示。只用行内 MathJax \\( ... \\)，不要用 \\[ ... \\]。
confusables：2-3 个读音或意义相近、容易混淆的词，每个写成一行，格式固定为：
        词 /音标/ 词性缩写 中文释义
        词性一律用英文缩写（n. / v. / adj. 等），并且不要用破折号或连字符。
chinese：该词的中文释义，多个用「；」分隔。"""

FACTS = {
    "velocity": ["displacement", "time"],
    "momentum": ["mass", "velocity"],
    "discriminant": ["b^2", "4ac"],
    "gradient": ["slope", "steep"],
    "resistance": ["voltage", "current"],
}
DEC = json.JSONDecoder()


def extract_json(text):
    """像应用那样：剥掉代码围栏，从第一个 { 开始尝试解析第一个完整 JSON 对象。"""
    i = text.find("{")
    if i < 0:
        return None
    try:
        obj, _ = DEC.raw_decode(text[i:])
        return obj if isinstance(obj, dict) else None
    except Exception:
        return None


def call(word, thinking):
    payload = {"model": "glm-4.5-flash",
               "messages": [{"role": "system", "content": SYSTEM},
                            {"role": "user", "content": TEMPLATE.format(w=word)}],
               "temperature": 0.4}
    if not thinking:
        payload["thinking"] = {"type": "disabled"}
    body = json.dumps(payload, ensure_ascii=False).encode()
    req = urllib.request.Request("https://open.bigmodel.cn/api/paas/v4/chat/completions",
                                 data=body, headers={"Content-Type": "application/json",
                                                     "Authorization": "Bearer " + KEY})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=200) as r:
            d = json.loads(r.read().decode())
        msg = d["choices"][0]["message"]
        return (msg.get("content") or "", msg.get("reasoning_content") or "",
                time.time() - t0, d.get("usage", {}))
    except Exception as e:
        return "", "", time.time() - t0, {"err": repr(e)[:90]}


def score(word, content, reasoning):
    s = {"json": 0, "complete": 0, "formula": 0, "confus": 0, "ipa": 0, "pos": 0, "fact": 0}
    d = extract_json(content)
    if d is None:
        return s, None
    s["json"] = 1
    # 完整性：内容里最后一个 } 是否闭合（截断时通常没有收尾）
    s["complete"] = 1 if content.strip().rstrip("`").rstrip().endswith("}") else 0
    f = (d.get("formula") or "").strip()
    if f == "" or ("\\(" in f or "\\[" in f):
        s["formula"] = 1
    lines = [l.strip() for l in (d.get("confusables") or "").splitlines() if l.strip()]
    if lines and all(re.match(r"^\S+\s+/[^/]+/\s+\S+\s+\S+", l) and "—" not in l and " - " not in l
                     for l in lines):
        s["confus"] = 1
    ph = d.get("phonetic") or ""
    if "英" in ph and "美" in ph:
        s["ipa"] = 1
    if re.search(r"\((n|v|adj|adv|prep|conj|num|pron)", d.get("definition") or ""):
        s["pos"] = 1
    blob = ((d.get("definition") or "") + " " + f + " " + (d.get("chinese") or "")).lower()
    need = FACTS.get(word, ["x"])
    s["fact"] = 1 if all(k.lower() in blob for k in need) else 0
    return s, d


results = {}
words = list(FACTS.keys())
for thinking in (False, True):
    label = "think" if thinking else "nothink"
    results[label] = {"words": {}, "totals": {k: 0 for k in
                      ("json", "complete", "formula", "confus", "ipa", "pos", "fact")},
                      "t": 0.0, "tok": 0, "reason_chars": 0}
    print("=" * 58)
    print("模式：%s" % ("开思考" if thinking else "关思考"))
    for w in words:
        content, reasoning, dt, usage = call(w, thinking)
        s, d = score(w, content, reasoning)
        R = results[label]
        R["t"] += dt
        R["tok"] += usage.get("total_tokens", 0) or 0
        R["reason_chars"] += len(reasoning)
        for k in R["totals"]:
            R["totals"][k] += s[k]
        R["words"][w] = {"score": s, "content": content, "reasoning": reasoning[:400],
                         "usage": usage, "seconds": round(dt, 1)}
        print("  %-13s %5.1fs tok=%-5s 思考%-5s | json=%d 完整=%d 公式=%d 易混=%d 音标=%d 词性=%d 事实=%d"
              % (w, dt, usage.get("total_tokens", "?"), len(reasoning), s["json"], s["complete"],
                 s["formula"], s["confus"], s["ipa"], s["pos"], s["fact"]))
    n = len(words)
    print("  --- json %d/%d  完整 %d/%d  公式 %d/%d  易混 %d/%d  音标 %d/%d  词性 %d/%d  事实 %d/%d"
          % (R["totals"]["json"], n, R["totals"]["complete"], n, R["totals"]["formula"], n,
             R["totals"]["confus"], n, R["totals"]["ipa"], n, R["totals"]["pos"], n,
             R["totals"]["fact"], n))
    print("  平均 %.1fs / %d tokens / 思考 %d 字" % (R["t"] / n, R["tok"] / n, R["reason_chars"] / n))

with open(r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistantAndroid\tools\_thinking_eval.json",
          "w", encoding="utf-8") as fh:
    json.dump(results, fh, ensure_ascii=False, indent=1)
print("\n明细已写入 tools/_thinking_eval.json")
