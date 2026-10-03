package com.ankiassistant;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * 「卡片往哪写」的唯一决策点。
 *
 *   · 内置引擎（默认）：APK 自带的 Anki 官方后端，写进设备自己的收藏库，并由它同步 AnkiWeb
 *   · 兜底：内置引擎不可用时，退回本机 AnkiDroid（装了才生效）
 *
 * 制卡页保存、草稿箱补发、浏览页重新保存都从这里走，避免两套逻辑各写各的。
 */
public class AnkiBackend {

    /** 当前是否用内置引擎（APK 自带的 Anki 官方后端） */
    public static boolean useEngine(Context c, Store store) {
        return store.useEngine() && EngineHolder.available(c);
    }

    /** 当前是否用本机 AnkiDroid（内置引擎不可用时才考虑） */
    public static boolean useDevice(Context c, Store store) {
        return !useEngine(c, store) && store.useAnkiDroid() && AnkiDroidClient.ready(c);
    }

    /** 当前后端的一句话说明（设置页/状态灯用） */
    public static String describe(Context c, Store store) {
        if (useEngine(c, store)) return "内置引擎";
        if (useDevice(c, store)) return "本机 AnkiDroid（兜底）";
        if (!EngineHolder.available(c)) return "不可用（本机 ABI 未包含引擎）";
        if (!AnkiDroidClient.installed(c)) return "不可用（没装 AnkiDroid）";
        if (!AnkiDroidClient.hasPermission(c)) return "不可用（AnkiDroid 未授权）";
        return "不可用";
    }

    /**
     * 统一保存入口。成功返回一句给用户看的话；失败抛异常（调用方负责转草稿等兜底）。
     */
    public static String save(Context c, Store store, String deck, JSONObject note,
                              String[] tags) throws Exception {
        CardConfig cfg = store.activeConfig();
        if (useEngine(c, store)) {
            AnkiEngine e = EngineHolder.get(c);
            long did = e.ensureDeck(deck);
            long mid = e.ensureCardNotetype(cfg);
            String[] names = cfg.fieldNames();
            String[] values = new String[names.length];
            for (int i = 0; i < names.length; i++) {
                values[i] = note == null ? "" : note.optString(names[i], "");
            }
            long nid = e.addNote(did, mid, values, tags);
            return "已写入本机收藏库（内置引擎）✓ note " + nid
                    + "　点同步即可推到 AnkiWeb";
        }
        if (useDevice(c, store)) {
            long nid = AnkiDroidClient.saveNote(c, deck, note, tags, cfg);
            return "已保存到本机 AnkiDroid ✓" + (nid > 0 ? "（note " + nid + "）" : "")
                    + "　在 AnkiDroid 里同步一次即可推到 AnkiWeb";
        }
        throw new Exception("这台设备上内置引擎不可用（APK 缺少对应 ABI 的原生库）");
    }

    // ------------------------------------------------------------ 读：牌组 / 搜索 / 删除

    /** 牌组列表 */
    public static String[] deckNames(Context c, Store store) throws Exception {
        if (useEngine(c, store)) {
            java.util.Map<String, Long> m = EngineHolder.get(c).deckNames();
            return m.keySet().toArray(new String[0]);
        }
        if (useDevice(c, store)) return AnkiDroidClient.deckNames(c);
        throw new Exception("这台设备上内置引擎不可用（APK 缺少对应 ABI 的原生库）");
    }

    /** 按 Anki 搜索语法查笔记，返回与 notesInfo 同构的 JSON */
    public static JSONArray searchNotes(Context c, Store store, String query, int limit)
            throws Exception {
        if (useEngine(c, store)) return EngineHolder.get(c).searchNotes(query, limit);
        if (useDevice(c, store)) return AnkiDroidClient.searchNotes(c, query, limit);
        throw new Exception("这台设备上内置引擎不可用（APK 缺少对应 ABI 的原生库）");
    }

    /** 查到的总数（本机模式没有单独的计数接口，就用拿到的条数） */
    public static int searchTotal(Context c, Store store, String query) throws Exception {
        if (useEngine(c, store)) return EngineHolder.get(c).searchCount(query);
        return -1;   // AnkiDroid 回退路径没有独立计数接口，UI 用返回条数
    }

    /** 删除笔记 */
    public static int deleteNotes(Context c, Store store, JSONArray ids) throws Exception {
        long[] arr = new long[ids.length()];
        for (int i = 0; i < ids.length(); i++) arr[i] = ids.optLong(i, -1);
        if (useEngine(c, store)) return EngineHolder.get(c).removeNotes(arr);
        if (useDevice(c, store)) return AnkiDroidClient.deleteNotes(c, arr);
        throw new Exception("这台设备上内置引擎不可用（APK 缺少对应 ABI 的原生库）");
    }

    /** 同步：内置引擎走完整的登录/比对/同步流程（设置页那个按钮） */
    public static String sync(Context c, Store store) throws Exception {
        if (useEngine(c, store)) {
            return "请在「设置 → AnkiWeb 同步」里点「登录并同步」";
        }
        AnkiDroidClient.openApp(c);
        return "已打开 AnkiDroid —— 它在启动时会自动同步";
    }

    /** 图形化编辑：AnkiDroid 提供编辑器；内置引擎这边暂不提供 */
    public static String guiEdit(Context c, Store store, long noteId) throws Exception {
        AnkiDroidClient.openApp(c);
        return "已打开 AnkiDroid —— 请在其中找到这张卡片编辑";
    }

    /** 便捷重载：标签用空格/逗号分隔的字符串 */
    public static String save(Context c, Store store, String deck, JSONObject note,
                              String tags) throws Exception {
        return save(c, store, deck, note, CreateView.parseTagsToArray(tags));
    }
}
