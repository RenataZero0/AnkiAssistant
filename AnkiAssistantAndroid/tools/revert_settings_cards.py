"""把"设置新增栏目"相关改动撤下来（弹窗 UI 与账号界面保留）。
   撤销内容：SettingsView 的四个新卡片、Store 的新开关、AnkiSync 的媒体开关、
             MainActivity.syncNow、CollectionBackup 类。
"""
import io
import os
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# 1) SettingsView：删掉四个新卡片块，恢复 loadValues();
p = APP + r"\src\com\ankiassistant\SettingsView.java"
s = io.open(p, encoding="utf-8").read()
start = s.find("        // ================= 同步 =================")
end = s.find("        loadValues();", start)
if start > 0 and end > start:
    s = s[:start] + "        loadValues();" + s[end + len("        loadValues();"):]
    print("SettingsView：四个新卡片已移除")
# 字段声明
s = s.replace("    private CheckBox autoSyncBox, wifiOnlyBox, mediaBox, clearBox, previewBox;\n", "")
s = s.replace("    private TextView syncInfo, dataTip;\n", "")
# 读写里的引用
for pat in (
    r"^\s*if \(autoSyncBox != null\) autoSyncBox\.setChecked\(store\.autoSyncAfterSave\(\)\);\n",
    r"^\s*if \(wifiOnlyBox != null\) wifiOnlyBox\.setChecked\(store\.syncWifiOnly\(\)\);\n",
    r"^\s*if \(mediaBox != null\) mediaBox\.setChecked\(store\.syncMedia\(\)\);\n",
    r"^\s*if \(clearBox != null\) clearBox\.setChecked\(store\.clearAfterSave\(\)\);\n",
    r"^\s*if \(previewBox != null\) previewBox\.setChecked\(store\.previewAfterFill\(\)\);\n",
    r"^\s*if \(syncInfo != null\) syncInfo\.setText\(AnkiSync\.describe\(store\)\);\n",
    r"^\s*refreshDataCard\(\);\n",
    r"^\s*if \(autoSyncBox != null\) store\.setAutoSyncAfterSave\(autoSyncBox\.isChecked\(\)\);\n",
    r"^\s*if \(wifiOnlyBox != null\) store\.setSyncWifiOnly\(wifiOnlyBox\.isChecked\(\)\);\n",
    r"^\s*if \(mediaBox != null\) store\.setSyncMedia\(mediaBox\.isChecked\(\)\);\n",
    r"^\s*if \(clearBox != null\) store\.setClearAfterSave\(clearBox\.isChecked\(\)\);\n",
    r"^\s*if \(previewBox != null\) store\.setPreviewAfterFill\(previewBox\.isChecked\(\)\);\n",
):
    s = re.sub(pat, "", s, flags=re.M)
# 新增的方法块
s = re.sub(r"(?s)    // -+ 数据与维护\n\n    /\*\* 刷新收藏库概况.*?\n    \}\n\n    /\*\* 用内置备份恢复.*?\n    \}\n", "", s, count=1)
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("SettingsView 处理完成")

# 2) Store：删掉新开关
p2 = APP + r"\src\com\ankiassistant\Store.java"
s2 = io.open(p2, encoding="utf-8").read()
s2 = re.sub(r"(?s)    // -+ 同步与制卡习惯\n.*?\n    // -+ 输出格式 config", "    // ------------------------------------------------------------ 输出格式 config", s2, count=1)
io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
print("Store 新开关已移除")

# 3) AnkiSync：恢复原来的媒体处理
p3 = APP + r"\src\com\ankiassistant\AnkiSync.java"
s3 = io.open(p3, encoding="utf-8").read()
s3 = s3.replace("        boolean withMedia = store.syncMedia();\n        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, withMedia);",
                "        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, true);")
s3 = s3.replace("        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n        if (withMedia) safeMediaSync(e, hkey);",
                "        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);\n        safeMediaSync(e, hkey);")
io.open(p3, "w", encoding="utf-8", newline="\n").write(s3)
print("AnkiSync 已恢复")

# 4) MainActivity：去掉 syncNow
p4 = APP + r"\src\com\ankiassistant\MainActivity.java"
s4 = io.open(p4, encoding="utf-8").read()
s4 = s4.replace('''    /** 页面上的「立即同步」入口（未登录会先弹登录） */
    public void syncNow() { onLampClick(); }

''', "")
io.open(p4, "w", encoding="utf-8", newline="\n").write(s4)
print("MainActivity.syncNow 已移除")

# 5) 删掉 CollectionBackup
f5 = APP + r"\src\com\ankiassistant\CollectionBackup.java"
if os.path.exists(f5):
    os.remove(f5)
    print("CollectionBackup.java 已删除")
