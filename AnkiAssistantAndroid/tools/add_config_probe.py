"""加 config 端到端探针：--ez configProbe true
   自建一个 config → 设为当前 → 用它写一张卡 → 打印字段与笔记类型。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")
path = APP + r"\src\com\ankiassistant\MainActivity.java"
src = io.open(path, encoding="utf-8").read()

PROBE = '''    /** 调试用：`--ez configProbe true` 自建 config → 设为当前 → 用它写一张卡 */
    private void runConfigProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    CardConfig c = new CardConfig();
                    c.id = "cfgprobe";
                    c.name = "探针词汇";
                    c.noteType = "探针词汇";
                    c.prompt = "给 {word} 出一张词卡，学科 {subject}，只输出 JSON：\\n"
                            + "term：词条\\nmeaning：中文释义\\nsentence：例句";
                    c.fields.add(new CardConfig.Field("词条", "", "", false));
                    c.fields.add(new CardConfig.Field("中文释义", "meaning", "中文", false));
                    c.fields.add(new CardConfig.Field("例句", "sentence", "英文例句", false));
                    store.saveConfig(c);
                    store.setActiveConfigId(c.id);
                    android.util.Log.i("AnkiAssistant", "CONFIG active = " + store.activeConfig().name
                            + " fields=" + java.util.Arrays.toString(store.activeConfig().fieldNames()));

                    org.json.JSONObject note = new org.json.JSONObject();
                    note.put("词条", "probe-word");
                    note.put("中文释义", "探针释义");
                    note.put("例句", "This is a probe sentence.");
                    String msg = AnkiBackend.save(MainActivity.this, store, "探针牌组", note,
                            new String[]{"Probe"});
                    android.util.Log.i("AnkiAssistant", "CONFIG save -> " + msg);
                    android.util.Log.i("AnkiAssistant", "CONFIG SELFTEST PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "CONFIG SELFTEST FAIL: " + t, t);
                }
            }
        }).start();
    }

'''
anchor = "    /** 调试用：`--ez browseProbe true` 走一遍 AnkiBackend 的搜索，验证浏览也由内置引擎提供 */"
if "runConfigProbe" in src:
    print("已存在，跳过")
else:
    src = src.replace(anchor, PROBE + anchor, 1)
    src = src.replace('if (getIntent() != null && getIntent().getBooleanExtra("browseProbe", false)) runBrowseProbe();',
                      'if (getIntent() != null && getIntent().getBooleanExtra("browseProbe", false)) runBrowseProbe();\n'
                      '        if (getIntent() != null && getIntent().getBooleanExtra("configProbe", false)) runConfigProbe();')
    io.open(path, "w", encoding="utf-8", newline="\n").write(src)
    print("探针已加入")
