"""README 更新：把「电脑端 AnkiConnect」那套说明换成内置引擎 + config。"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")
path = APP + r"\README.md"
src = io.open(path, encoding="utf-8").read()

NEW_SECTION = '''## 三、卡片写入与同步（内置引擎）

**不需要电脑，也不需要装 AnkiDroid**：APK 里带着 Anki 官方的 Rust 后端（rslib/rsdroid），
在设备上自己维护一个收藏库，并由它直接与 AnkiWeb 同步。编译方法见 `tools/BUILD_ENGINE.md`。

```
Anki 助手（内置引擎） → 本机收藏库（filesDir/collection.anki2） → 同步 → AnkiWeb 云端账号
```

设置 → **AnkiWeb 同步**：填邮箱 + 密码 → 点「登录并同步」。
- 密码只用于登录，**不会保存在设备上**；登录后只保留后端签发的 hkey
- 首次同步如果云端已有内容，会让你选择「上传本机」或「下载云端」（云端才是你的真实数据时选下载）
- 之后每次点同步都是增量同步，并顺带同步媒体文件

兜底：如果某台设备的 ABI 没有内置库（或编译时被移除），会退回**本机 AnkiDroid**（装了才生效）。

### 输出格式 config

设置 → **输出格式（config）**：决定 AI 按什么格式产出、卡片有哪些字段。
- 内置一套默认（A Level / NCUK IFY 数学物理术语卡），开箱即用
- 「新建」可以自定：名字、字段清单（每行 `字段名 = AI键 = 提示`，第一行是卡片正面）、提示词（支持 `{word}` / `{subject}`）
- 每个自定义 config 会生成自己的 Anki 笔记类型（字段与正反面模板按 config 生成）
- 制卡页的输入框会**按当前 config 动态重建**

'''

# 1) 整段替换「## 三、…」到「## 四、」之前
m = re.search(r"## 三、.*?(?=## 四、)", src, re.S)
if m:
    src = src[:m.start()] + NEW_SECTION + src[m.end():]
    print("  第三节已重写")
else:
    print("  !! 找不到第三节")

# 2) 需求对照表那行
src = src.replace(
    "| (2) 调用 Anki API 自动保存到云端账号 | AnkiConnect：`addNote` 写入 + `sync()` 推到 AnkiWeb，见下文「为什么是 AnkiConnect」 |",
    "| (2) 调用 Anki API 自动保存到云端账号 | 内置 Anki 官方引擎（rslib/rsdroid）：写本机收藏库 + 同步到 AnkiWeb |")

# 3) 内置默认值表里那条端口/apiKey
src = src.replace("| 端口 / AnkiConnect Key | `8765` / 留空 |", "| AnkiWeb 账号 | 登录一次即可（只存 hkey） |")

# 4) AGPL 说明里的兜底说法
src = src.replace("可在设置里关掉「优先使用内置引擎」，此时可改用 AnkiDroid 或电脑端 AnkiConnect，",
                  "可在设置里关掉「优先使用内置引擎」，此时改用本机 AnkiDroid，")

# 5) 项目结构里已删除的文件
src = src.replace("│   ├─ AnkiClient.java        AnkiConnect 客户端（纯 java.* + org.json，可自检）\n", "")
src = src.replace("├─ mock_servers.py        联调用：假 AnkiConnect(8765) + 假 AI(8899)\n", "├─ mock_servers.py        联调用：假 AI(8899)\n")
src = src.replace("├─ seed_mock_note.py      往假 AnkiConnect 里塞测试卡片\n", "")
src = src.replace("AnkiConnect 请求构造、响应 `error` 处理、地址归一化、AI 请求体与响应解析。",
                  "CardConfig 构造与 JSON 往返、AI 请求体与响应解析、公式归一化。")

# 6) 历史排障小节：路径已不存在，整段删掉
m2 = re.search(r"### 装 AnkiConnect 时踩到的坑.*?(?=## 八、)", src, re.S)
if m2:
    src = src[:m2.start()] + src[m2.end():]
    print("  AnkiConnect 排障小节已删")

# 7) 最后扫一遍
left = [l for l in src.split("\n") if "AnkiConnect" in l]
print("README 剩余 AnkiConnect 行数：%d" % len(left))
for l in left[:6]:
    print("   " + l.strip()[:100])

io.open(path, "w", encoding="utf-8", newline="\n").write(src)
