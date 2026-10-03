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

    // ------------------------------------------------------------ 读：牌组 / 搜索 / 删除

    /** 牌组列表（本机或电脑） */
    public static String[] deckNames(Context c, Store store) throws Exception {
        if (useDevice(c, store)) return AnkiDroidClient.deckNames(c);
        JSONArray a = new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey()).deckNames();
        String[] out = new String[a.length()];
        for (int i = 0; i < out.length; i++) out[i] = a.optString(i, "");
        return out;
    }

    /** 按 Anki 搜索语法查笔记，返回与 notesInfo 同构的 JSON */
    public static JSONArray searchNotes(Context c, Store store, String query, int limit)
            throws Exception {
        if (useDevice(c, store)) return AnkiDroidClient.searchNotes(c, query, limit);
        AnkiClient anki = new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey());
        JSONArray ids = anki.findNotes(query);
        JSONArray take = new JSONArray();
        for (int i = 0; i < ids.length() && i < limit; i++) take.put(ids.opt(i));
        return take.length() == 0 ? new JSONArray() : anki.notesInfo(take);
    }

    /** 查到的总数（本机模式没有单独的计数接口，就用拿到的条数） */
    public static int searchTotal(Context c, Store store, String query) throws Exception {
        if (!useDevice(c, store)) {
            return new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey())
                    .findNotes(query).length();
        }
        return -1;   // 本机模式：UI 用返回条数
    }

    /** 删除笔记 */
    public static int deleteNotes(Context c, Store store, JSONArray ids) throws Exception {
        long[] arr = new long[ids.length()];
        for (int i = 0; i < ids.length(); i++) arr[i] = ids.optLong(i, -1);
        if (useDevice(c, store)) return AnkiDroidClient.deleteNotes(c, arr);
        JSONArray a = new JSONArray();
        for (long id : arr) a.put(id);
        return new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey())
                .deleteNotes(a).length();
    }

    /**
     * 「同步」这件事两边不一样：
     *   · 电脑模式：直接调用 AnkiConnect 的 sync()
     *   · 本机模式：AnkiDroid 的 API 没有同步接口 → 把 AnkiDroid 拉到前台，它自己会同步
     * 返回一句给用户看的话。
     */
    public static String sync(Context c, Store store) throws Exception {
        if (useDevice(c, store)) {
            AnkiDroidClient.openApp(c);
            return "已打开 AnkiDroid —— 它在启动时会自动同步（本机 API 不提供同步调用）";
        }
        new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey()).sync();
        return "已同步到 AnkiWeb ✓";
    }

    /** 图形化编辑：电脑模式用 AnkiConnect 的 guiEditNote；本机模式打开 AnkiDroid */
    public static String guiEdit(Context c, Store store, long noteId) throws Exception {
        if (useDevice(c, store)) {
            AnkiDroidClient.openApp(c);
            return "已打开 AnkiDroid —— 请在其中找到这张卡片编辑（本机 API 不提供图形编辑）";
        }
        new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey()).guiEditNote(noteId);
        return "已在电脑上的 Anki 里打开这张卡片";
    }

    /** 便捷重载：标签用空格/逗号分隔的字符串 */
    public static String save(Context c, Store store, String deck, JSONObject note,
                              String tags) throws Exception {
        return save(c, store, deck, note, CreateView.parseTagsToArray(tags));
    }
}
