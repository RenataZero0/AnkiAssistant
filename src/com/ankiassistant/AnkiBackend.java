package com.ankiassistant;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * 「卡片往哪写」的唯一决策点。
 *
 *   · 本机模式：装了 AnkiDroid 且已授权、并且设置里的开关是打开的 → 直接写本机 AnkiDroid
 *     （完全不需要电脑；卡片进的是设备上的收藏库，AnkiDroid 自己会同步到 AnkiWeb）
 *   · 电脑模式：否则走电脑上的 Anki + AnkiConnect（需要同一个 Wi-Fi 且 Anki 开着）
 *
 * 制卡页保存、草稿箱补发、浏览页重新保存都从这里走，避免两套逻辑各写各的。
 */
public class AnkiBackend {

    /** 当前是否用本机 AnkiDroid */
    public static boolean useDevice(Context c, Store store) {
        return store.useAnkiDroid() && AnkiDroidClient.ready(c);
    }

    /** 当前后端的一句话说明（设置页/状态灯用） */
    public static String describe(Context c, Store store) {
        if (useDevice(c, store)) return "本机 AnkiDroid（不需要电脑）";
        if (!store.useAnkiDroid()) return "电脑 AnkiConnect（本机开关已关）";
        if (!AnkiDroidClient.installed(c)) return "电脑 AnkiConnect（本机没装 AnkiDroid）";
        if (!AnkiDroidClient.hasPermission(c)) return "电脑 AnkiConnect（AnkiDroid 未授权）";
        return "电脑 AnkiConnect";
    }

    /**
     * 统一保存入口。成功返回一句给用户看的话；失败抛异常（调用方负责转草稿等兜底）。
     */
    public static String save(Context c, Store store, String deck, JSONObject note,
                              String[] tags) throws Exception {
        if (useDevice(c, store)) {
            long nid = AnkiDroidClient.saveNote(c, deck, note, tags);
            return "已保存到本机 AnkiDroid ✓" + (nid > 0 ? "（note " + nid + "）" : "")
                    + "　在 AnkiDroid 里同步一次即可推到 AnkiWeb";
        }
        AnkiClient anki = new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey());
        JSONArray arr = new JSONArray();
        if (tags != null) {
            for (String t : tags) if (t != null && t.trim().length() > 0) arr.put(t.trim());
        }
        anki.saveNote(deck, note, arr, store.autoSync());
        return store.autoSync() ? "已保存并同步到 AnkiWeb 云端 ✓"
                : "已保存到本地 Anki（自动同步已关闭）✓";
    }

    /** 便捷重载：标签用空格/逗号分隔的字符串 */
    public static String save(Context c, Store store, String deck, JSONObject note,
                              String tags) throws Exception {
        return save(c, store, deck, note, CreateView.parseTagsToArray(tags));
    }
}
