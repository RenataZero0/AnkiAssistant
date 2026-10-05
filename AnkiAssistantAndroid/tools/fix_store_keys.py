"""修掉 Store 里被吞掉引号的 prefs key。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\Store.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

pairs = [
    ("sp.getBoolean(syncWifiOnly, true)", 'sp.getBoolean("syncWifiOnly", true)'),
    ("sp.getBoolean(syncMediaOn, true)", 'sp.getBoolean("syncMediaOn", true)'),
    ("sp.getBoolean(clearAfterSave, true)", 'sp.getBoolean("clearAfterSave", true)'),
    ("sp.getBoolean(previewAfterFill, false)", 'sp.getBoolean("previewAfterFill", false)'),
    ("sp.edit().putBoolean(syncWifiOnly, v)", 'sp.edit().putBoolean("syncWifiOnly", v)'),
    ("sp.edit().putBoolean(syncMediaOn, v)", 'sp.edit().putBoolean("syncMediaOn", v)'),
    ("sp.edit().putBoolean(clearAfterSave, v)", 'sp.edit().putBoolean("clearAfterSave", v)'),
    ("sp.edit().putBoolean(previewAfterFill, v)", 'sp.edit().putBoolean("previewAfterFill", v)'),
    ("sp.edit().putBoolean(autoSyncAfterSave, v)", 'sp.edit().putBoolean("autoSyncAfterSave", v)'),
]
n = 0
for a, b in pairs:
    if a in s:
        s = s.replace(a, b)
        n += 1
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("修正 %d 处" % n)
