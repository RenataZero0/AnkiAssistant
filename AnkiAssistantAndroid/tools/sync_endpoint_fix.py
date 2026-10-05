"""记住同步端点：Store 增加 syncEndpoint；AnkiSync 与探针里灌入/保存端点。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# --- Store ---
p = APP + r"\src\com\ankiassistant\Store.java"
s = io.open(p, encoding="utf-8").read()
if "syncEndpoint" not in s:
    anchor = "    /** 上次同步成功的时间（毫秒，0 = 从未） */"
    add = ('    /**\n'
           '     * 同步端点。AnkiWeb 会把部分账号迁到别的同步节点（例如 sync2.ankiweb.net），\n'
           '     * 记下来就不用每次从老节点再跳一次。\n'
           '     */\n'
           '    public String syncEndpoint() { return sp.getString("syncEndpoint", ""); }\n'
           '    public void setSyncEndpoint(String v) { put("syncEndpoint", v == null ? "" : v.trim()); }\n\n')
    s = s.replace(anchor, add + anchor, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("Store.syncEndpoint 已加")

# --- AnkiSync ---
p = APP + r"\src\com\ankiassistant\AnkiSync.java"
s = io.open(p, encoding="utf-8").read()
if "store.syncEndpoint()" not in s:
    s = s.replace(
        "        AnkiEngine e = EngineHolder.get(c);\n\n        String hkey = store.ankiWebHkey();",
        "        AnkiEngine e = EngineHolder.get(c);\n"
        "        e.setEndpoint(store.syncEndpoint());   // 上次记下的同步节点，直接用\n\n"
        "        String hkey = store.ankiWebHkey();")
    # 每个返回点都保存端点
    s = s.replace("        store.setLastSyncAt(System.currentTimeMillis());\n        return new Outcome(\"已是最新，无需同步 ✓\", false);",
                  "        store.setSyncEndpoint(e.endpoint());\n        store.setLastSyncAt(System.currentTimeMillis());\n        return new Outcome(\"已是最新，无需同步 ✓\", false);")
    s = s.replace("        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, true);",
                  "        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, true);\n        store.setSyncEndpoint(e.endpoint());")
    s = s.replace("        store.setLastSyncAt(System.currentTimeMillis());\n        return new Outcome(info.serverMessage.length() > 0",
                  "        store.setSyncEndpoint(e.endpoint());\n        store.setLastSyncAt(System.currentTimeMillis());\n        return new Outcome(info.serverMessage.length() > 0")
    s = s.replace("        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n        safeMediaSync(e, hkey);\n        store.setLastSyncAt(System.currentTimeMillis());",
                  "        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n        safeMediaSync(e, hkey);\n        store.setSyncEndpoint(e.endpoint());\n        store.setLastSyncAt(System.currentTimeMillis());")
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("AnkiSync 已接入端点")

# --- 探针 ---
p = APP + r"\src\com\ankiassistant\MainActivity.java"
s = io.open(p, encoding="utf-8").read()
if "e.setEndpoint(store.syncEndpoint());" not in s:
    s = s.replace('                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 login user=" + user);',
                  '                    e.setEndpoint(store.syncEndpoint());\n'
                  '                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 login user=" + user);')
    s = s.replace('                    android.util.Log.i("AnkiAssistant", "SYNCREAL PASS");',
                  '                    store.setSyncEndpoint(e.endpoint());\n'
                  '                    android.util.Log.i("AnkiAssistant", "SYNCREAL endpoint=" + e.endpoint());\n'
                  '                    android.util.Log.i("AnkiAssistant", "SYNCREAL PASS");')
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("探针已接入端点")
