"""清理剩余文案/注释：不再出现 AnkiConnect / 电脑端 的说法。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

EDITS = {
    r"\src\com\ankiassistant\AnkiBackend.java": [
        ("""/**
 * 「卡片往哪写」的唯一决策点。
 *
 *   · 本机模式：装了 AnkiDroid 且已授权、并且设置里的开关是打开的 → 直接写本机 AnkiDroid
 *     （完全不需要电脑；卡片进的是设备上的收藏库，AnkiDroid 自己会同步到 AnkiWeb）
 *   · 电脑模式：否则走电脑上的 Anki + AnkiConnect（需要同一个 Wi-Fi 且 Anki 开着）
 *
 * 制卡页保存、草稿箱补发、浏览页重新保存都从这里走，避免两套逻辑各写各的。
 */""",
         """/**
 * 「卡片往哪写」的唯一决策点。
 *
 *   · 内置引擎（默认）：APK 自带的 Anki 官方后端，写进设备自己的收藏库，并由它同步 AnkiWeb
 *   · 兜底：内置引擎不可用时，退回本机 AnkiDroid（装了才生效）
 *
 * 制卡页保存、草稿箱补发、浏览页重新保存都从这里走，避免两套逻辑各写各的。
 */"""),
        ("""        if (useEngine(c, store)) return "内置引擎（APK 自带，不需要电脑也不需要 AnkiDroid）";
        if (store.useEngine() && !EngineHolder.available(c)) return "电脑 AnkiConnect（本机 ABI 无内置引擎）";
        if (useDevice(c, store)) return "本机 AnkiDroid（不需要电脑）";
        if (!store.useAnkiDroid()) return "电脑 AnkiConnect（本机开关已关）";
        if (!AnkiDroidClient.installed(c)) return "电脑 AnkiConnect（本机没装 AnkiDroid）";
        if (!AnkiDroidClient.hasPermission(c)) return "电脑 AnkiConnect（AnkiDroid 未授权）";
        return "电脑 AnkiConnect";""",
         """        if (useEngine(c, store)) return "内置引擎";
        if (useDevice(c, store)) return "本机 AnkiDroid（兜底）";
        if (!EngineHolder.available(c)) return "不可用（本机 ABI 未包含引擎）";
        if (!AnkiDroidClient.installed(c)) return "不可用（没装 AnkiDroid）";
        if (!AnkiDroidClient.hasPermission(c)) return "不可用（AnkiDroid 未授权）";
        return "不可用";"""),
        ('+ "　点「同步到 AnkiWeb」即可推到云端";',
         '+ "　点同步即可推到 AnkiWeb";'),
        ('    /** 牌组列表（本机或电脑） */', '    /** 牌组列表 */'),
    ],
    r"\src\com\ankiassistant\AnkiDroidClient.java": [
        (" * 直接写本机的 AnkiDroid —— **完全不依赖电脑**。",
         " * 直接写本机的 AnkiDroid（内置引擎不可用时的兜底方案）。"),
        (" * 走的不是 AnkiConnect（那是给桌面版 Anki 用的），而是 AnkiDroid 官方提供的\n"
         " * ContentProvider API（`com.ichi2.anki.flashcards`），AnkiDroid 自己维护设备上的收藏库，",
         " * 走的是 AnkiDroid 官方提供的 ContentProvider API（`com.ichi2.anki.flashcards`），\n"
         " * AnkiDroid 自己维护设备上的收藏库，"),
        ('return "已就绪（本机写入，不需要电脑）";', 'return "已就绪";'),
        (" * 按 Anki 搜索语法查笔记（`deck:\"xxx\" tag:yyy 关键词`），返回**与 AnkiConnect notesInfo 同构**的 JSON：",
         " * 按 Anki 搜索语法查笔记（`deck:\"xxx\" tag:yyy 关键词`），返回统一形状的 JSON："),
        (" * 字段顺序用 {@link CardFormat#FIELDS}，模板与样式也复用 CardFormat 里那套，保证与电脑端写入的卡片完全一致。",
         " * 字段顺序用 {@link CardFormat#FIELDS}，模板与样式也复用 CardFormat 里那套。"),
    ],
    r"\src\com\ankiassistant\AnkiEngine.java": [
        (" * 自己写卡片、自己跟 AnkiWeb 同步 —— **既不需要电脑，也不需要装 AnkiDroid**。",
         " * 自己写卡片、自己跟 AnkiWeb 同步 —— 不需要任何外部客户端。"),
        ("    /** 与电脑端一致：这就是写进 Anki 的笔记类型名 */",
         "    /** 写进 Anki 的笔记类型名 */"),
        ("     * JSON 结构与电脑端 AnkiConnect 的 createModel 参数一致，保证两边卡片长得一样。",
         "     * JSON 用 Anki 自己的 legacy 模型结构，缺的字段由后端补默认值。"),
        ("    /** 按 Anki 搜索语法查笔记，返回**与 AnkiConnect notesInfo 同构**的 JSON（浏览页两边共用一套渲染） */",
         "    /** 按 Anki 搜索语法查笔记，返回浏览页统一使用的 JSON 形状 */"),
    ],
    r"\src\com\ankiassistant\AnkiSync.java": [
        (' * 以及把"需要全量同步"这种决定交回给用户（和桌面版 Anki 的行为一致）。',
         ' * 以及把"需要全量同步"这种决定交回给用户（和 Anki 桌面版的行为一致）。'),
        (" *   4. 全量同步：必须由用户决定 上传本机 还是 下载云端  → fullUploadOrDownload",
         " *   4. 全量同步：必须由用户决定 上传本机 还是 下载云端 → fullUploadOrDownload"),
    ],
    r"\assets\editor.html": [
        ('>▾ 从 Anki 选择<', '>▾ 选择牌组<'),
    ],
}

for rel, pairs in EDITS.items():
    path = APP + rel
    text = io.open(path, encoding="utf-8").read()
    for old, new in pairs:
        if old in text:
            text = text.replace(old, new)
            print("  ok  %s" % rel.split("\\")[-1])
        else:
            print("  !!  未匹配 %s -> %s" % (rel.split("\\")[-1], old.strip().split("\n")[0][:50]))
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
print("done")
