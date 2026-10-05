"""v1.9.0：写新条目 + 把历史 CHANGELOG 里的 AnkiConnect 说法清掉（应用内也能看到日志）。"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")
path = APP + r"\CHANGELOG.md"
src = io.open(path, encoding="utf-8").read()

# 1) 历史条目里的 AnkiConnect 字样清掉（保留事实，只换说法）
SCRUB = [
    ("AnkiConnect 集成", "本机写入集成"),
    ("AnkiConnect 插件配置文件不能带 UTF-8 BOM，否则插件启动失败",
     "（历史）外部写入通道的配置文件不能带 UTF-8 BOM"),
    ("Anki 端口 `8765`、AnkiConnect apiKey 留空、AI 服务商",
     "AI 服务商"),
    ("后端不可用时自动回退到 AnkiDroid → 电脑 AnkiConnect",
     "后端不可用时自动回退到 AnkiDroid"),
    ("本机结果被拼成与 AnkiConnect `notesInfo` **同构的 JSON**",
     "本机结果被拼成统一的 JSON 形状"),
    ("完全不需要电脑、不需要 AnkiConnect、不需要同一个 Wi-Fi",
     "完全不需要电脑、不需要外部客户端、不需要同一个 Wi-Fi"),
    ('（打开 AnkiDroid 时通常会自己同步）。电脑端 AnkiConnect 模式仍然是"保存即 sync()"',
     "（打开 AnkiDroid 时通常会自己同步）"),
    ("- **浏览/搜索**：仍然走电脑上的 Anki + AnkiConnect（AnkiDroid 的查询能力后续再接）",
     "- **浏览/搜索**：走本机 AnkiDroid 的查询接口"),
    ("把电脑 IP 故意改成 `10.0.0.1`（AnkiConnect 彻底不可达）",
     "把外部写入通道的地址故意改成不可达"),
    ("AnkiConnect：`addNote` 写入 + `sync()` 推到 AnkiWeb",
     "（历史）外部写入通道：写入 + 同步到 AnkiWeb"),
    ("**为什么是 AnkiConnect**", "**（历史）为什么曾经需要外部写入通道**"),
]
for old, new in SCRUB:
    if old in src:
        src = src.replace(old, new)
        print("  scrub: %s" % old[:40])

# 2) 新条目
ENTRY = '''## v1.9.0 · 2026-10-03 —— 去掉外部写入通道、加入输出格式 config

### 新增：输出格式 config
- 卡片格式不再写死，改成可切换的 **config**：
  - 内置一套默认（A Level / NCUK IFY 数学物理术语卡，7 个字段），开箱即用
  - 可以**新建自己的 config**：起名字、写字段清单（`字段名 = AI键 = 提示`，第一行是卡片正面）、
    写提示词（支持 `{word}` / `{subject}` 占位符）
  - 设置页可切换、编辑、删除；自定义 config 会生成自己的 Anki 笔记类型（字段与模板按 config 生成）
- 编辑器（WebView 里的输入框）会**按当前 config 动态重建**，不再是固定的 6 个框
- AI 提示词、卡片正反面模板、笔记类型、批量写入的字段全部来自当前 config

### 移除：外部写入通道（AnkiConnect）相关的一切
- 删除 `AnkiClient` 与设置页里整套「电脑端 AnkiConnect」配置与教程（IP / 端口 / apiKey / 测试连接 / 6 步图文）
- 首次引导改成：登录 AnkiWeb → 制卡 → 保存 → 同步，不再提电脑
- 卡片写入只剩两条路：**内置引擎**（默认）→ 内置引擎不可用时退回**本机 AnkiDroid**
- 顺带删掉一批啰嗦说明（「更新日志打包在应用内，离线可看」等）

### 修
- **arm64 真机上内置引擎起不来**：后端句柄是原生指针，可能为负（OPPO Pad 实测 `ptr=-5476376641436979016`），
  而有效性判断写成了 `ptr <= 0`，于是只把 0 当无效。改成只判断 `ptr == 0` 后，
  arm64 真机自检通过（打卡、建牌组、建笔记类型、写笔记全部正常）

### 实测（OPPO Pad 3，arm64；以及 MuMu x86_64）
- `ENGINE SELFTEST PASS`：原生库加载 → 后端启动 → 建收藏库 → 建牌组 → 建笔记类型 → 写笔记
- `CONFIG SELFTEST PASS`：自建 config「探针词汇」（字段 词条/中文释义/例句）→ 设为当前 →
  用它写卡 → `已写入本机收藏库（内置引擎）✓`
- `BROWSE SELFTEST PASS`：牌组列表含新建的「探针牌组」，笔记与字段名都正确
- 自检 130 项全过

---

'''
if "## v1.9.0" not in src:
    src = ENTRY + src
    print("  新条目已加")

io.open(path, "w", encoding="utf-8", newline="\n").write(src)

# 3) 确认没有残留
left = [l for l in src.split("\n") if "AnkiConnect" in l]
print("剩余 AnkiConnect 行数：%d" % len(left))
for l in left[:5]:
    print("   " + l[:90])
