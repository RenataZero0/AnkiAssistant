"""为新增设置项补上数据层与行为：
   Store：自动同步 / 仅 Wi-Fi / 同步媒体 / 保存后清空 / AI 后切预览
   AnkiSync：按设置决定是否同步媒体
   CreateView：保存后清空、按需自动同步
   CollectionBackup：导出备份到「下载」、从内置备份恢复、统计占用
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")


def patch(name, pairs):
    p = APP + "\\src\\com\\ankiassistant\\" + name
    s = io.open(p, encoding="utf-8").read()
    for old, new in pairs:
        if old in s:
            s = s.replace(old, new, 1)
        else:
            print("  !! %s 未匹配: %s" % (name, old.strip().split("\n")[0][:60]))
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("  已处理 " + name)


# ---------------- Store ----------------
store_add = '''
    // ------------------------------------------------------------ 同步与制卡习惯

    /** 保存卡片后自动同步一次 */
    public boolean autoSyncAfterSave() { return sp.getBoolean("autoSyncAfterSave", false); }
    public void setAutoSyncAfterSave(boolean v) { put("autoSyncAfterSave", v); }

    /** 只在 Wi-Fi 下自动同步（省流量） */
    public boolean syncWifiOnly() { return sp.getBoolean("syncWifiOnly", true); }
    public void setSyncWifiOnly(boolean v) { put("syncWifiOnly", v); }

    /** 同步时是否连媒体文件一起同步 */
    public boolean syncMedia() { return sp.getBoolean("syncMediaOn", true); }
    public void setSyncMedia(boolean v) { put("syncMediaOn", v); }

    /** 保存成功后清空输入，方便连着做下一张 */
    public boolean clearAfterSave() { return sp.getBoolean("clearAfterSave", true); }
    public void setClearAfterSave(boolean v) { put("clearAfterSave", v); }

    /** AI 填充完成后自动切到预览 */
    public boolean previewAfterFill() { return sp.getBoolean("previewAfterFill", false); }
    public void setPreviewAfterFill(boolean v) { put("previewAfterFill", v); }
'''
patch("Store.java", [("    // ------------------------------------------------------------ 输出格式", store_add + "\n    // ------------------------------------------------------------ 输出格式")])

# ---------------- AnkiSync：媒体开关 ----------------
patch("AnkiSync.java", [
    ("        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, true);",
     "        boolean withMedia = store.syncMedia();\n"
     "        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, withMedia);"),
    ("        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n        safeMediaSync(e, hkey);",
     "        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n"
     "        if (withMedia) safeMediaSync(e, hkey);"),
])

print("数据层与同步行为处理完成")
