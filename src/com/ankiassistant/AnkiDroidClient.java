package com.ankiassistant;

import android.content.ContentValues;
import android.content.Context;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;

/**
 * 直接写本机的 AnkiDroid —— **完全不依赖电脑**。
 *
 * 走的不是 AnkiConnect（那是给桌面版 Anki 用的），而是 AnkiDroid 官方提供的
 * ContentProvider API（`com.ichi2.anki.flashcards`），AnkiDroid 自己维护设备上的收藏库，
 * 也能同步到 AnkiWeb。本文件按官方 API 的契约复刻了三个动作，只用 android/java 自带的东西：
 *
 *   1. 建牌组      insert  content://…/decks                {"deck_name": "A Level Pure Mathematics"}
 *   2. 建笔记类型  insert  content://…/models               {"name","field_names","num_cards","css","sort_field_index"}
 *                  update  content://…/models/<id>/templates/0  {"name","question_format","answer_format"}
 *   3. 加笔记      insert  content://…/notes                {"mid","flds","tags"}
 *                  再把该笔记的卡片移进目标牌组：query …/notes/<id>/cards → update …/cards/<ord> {"deck_id": id}
 *
 * 字段分隔符是 U+001F（官方 Utils.joinFields 就是用它），标签用空格分隔（前后各留一个空格）。
 *
 * 权限：`com.ichi2.anki.permission.READ_WRITE_DATABASE`（危险权限，安装后还要在运行时申请，
 * 授权界面由 AnkiDroid 自己弹）。Android 6 以下装的时候就有了。
 *
 * 注意：这套 API **没有提供同步接口**，所以本机写完要在 AnkiDroid 里同步一次才会推到 AnkiWeb
 * （AnkiDroid 打开时通常会自动同步）。
 */
public class AnkiDroidClient {

    public static final String PKG = "com.ichi2.anki";
    public static final String AUTHORITY = "com.ichi2.anki.flashcards";
    public static final String PERMISSION = "com.ichi2.anki.permission.READ_WRITE_DATABASE";
    /** 请求码，Activity 回调用得到 */
    public static final int PERM_REQUEST = 4211;

    static final String FIELD_SEP = "\u001f";
    private static final Uri BASE = Uri.parse("content://" + AUTHORITY);
    private static final Uri DECKS = Uri.withAppendedPath(BASE, "decks");
    private static final Uri MODELS = Uri.withAppendedPath(BASE, "models");
    private static final Uri NOTES = Uri.withAppendedPath(BASE, "notes");

    public static class ApiException extends Exception {
        public ApiException(String m) { super(m); }
    }

    // ------------------------------------------------------------ 状态

    /** AnkiDroid 装了没（用 provider 是否能解析来判断，比包名更准：可能是 parallel 版） */
    public static boolean installed(Context c) {
        try {
            return c.getPackageManager().resolveContentProvider(AUTHORITY, 0) != null;
        } catch (Exception e) {
            return false;
        }
    }

    public static boolean hasPermission(Context c) {
        try {
            if (android.os.Build.VERSION.SDK_INT < 23) return true;
            return c.checkPermission(PERMISSION, android.os.Process.myPid(),
                    android.os.Process.myUid()) == PackageManager.PERMISSION_GRANTED;
        } catch (Exception e) {
            return false;
        }
    }

    /** 可以用了（装了 + 有权限） */
    public static boolean ready(Context c) {
        return installed(c) && hasPermission(c);
    }

    /** 设置页用的一句话状态 */
    public static String status(Context c) {
        if (!installed(c)) return "未安装 AnkiDroid";
        if (!hasPermission(c)) return "已安装，尚未授权";
        return "已就绪（本机写入，不需要电脑）";
    }

    /** 该在哪个应用里弹授权界面（parallel 版包名不同，从 provider 反查） */
    public static String providerPackage(Context c) {
        try {
            return c.getPackageManager().resolveContentProvider(AUTHORITY, 0).packageName;
        } catch (Exception e) {
            return PKG;
        }
    }

    // ------------------------------------------------------------ 三个动作

    /** 找牌组，没有就建；返回 deckId */
    public static long ensureDeck(Context c, String deckName) throws ApiException {
        String name = deckName == null ? "" : deckName.trim();
        if (name.length() == 0) throw new ApiException("牌组名为空");
        try {
            Cursor cur = c.getContentResolver().query(DECKS,
                    new String[]{"_id", "deck_name"}, null, null, null);
            if (cur != null) {
                try {
                    while (cur.moveToNext()) {
                        if (name.equals(cur.getString(1))) return cur.getLong(0);
                    }
                } finally {
                    cur.close();
                }
            }
            ContentValues v = new ContentValues();
            v.put("deck_name", name);
            Uri u = c.getContentResolver().insert(DECKS, v);
            if (u == null) throw new ApiException("建牌组失败（AnkiDroid 拒绝了写入）");
            return Long.parseLong(u.getLastPathSegment());
        } catch (ApiException e) {
            throw e;
        } catch (Exception e) {
            throw new ApiException("建牌组出错：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
    }

    /**
     * 找笔记类型（名字与字段都对得上就复用），没有就建一个单模板的自定义类型；返回 modelId。
     */
    public static long ensureModel(Context c, String modelName, String[] fields,
                                   String qfmt, String afmt, String css) throws ApiException {
        String want = join(fields);
        try {
            Cursor cur = c.getContentResolver().query(MODELS,
                    new String[]{"_id", "name", "field_names"}, null, null, null);
            if (cur != null) {
                try {
                    while (cur.moveToNext()) {
                        if (!modelName.equals(cur.getString(1))) continue;
                        String f = cur.getString(2);
                        if (f != null && want.equals(f.trim())) return cur.getLong(0);
                    }
                } finally {
                    cur.close();
                }
            }
            ContentValues v = new ContentValues();
            v.put("name", modelName);
            v.put("field_names", want);
            v.put("num_cards", 1);
            v.put("css", css == null ? "" : css);
            v.put("sort_field_index", 0);
            Uri u = c.getContentResolver().insert(MODELS, v);
            if (u == null) throw new ApiException("建笔记类型失败（AnkiDroid 拒绝了写入）");
            long mid = Long.parseLong(u.getLastPathSegment());

            // 模板（第 0 个）
            Uri tpl = Uri.withAppendedPath(Uri.withAppendedPath(u, "templates"), "0");
            ContentValues t = new ContentValues();
            t.put("name", "卡片 1");
            t.put("question_format", qfmt == null ? "" : qfmt);
            t.put("answer_format", afmt == null ? "" : afmt);
            c.getContentResolver().update(tpl, t, null, null);
            return mid;
        } catch (ApiException e) {
            throw e;
        } catch (Exception e) {
            throw new ApiException("建笔记类型出错：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
    }

    /** 加一张笔记，并把它的卡片放进指定牌组 */
    public static long addNote(Context c, long deckId, long modelId,
                               String[] fields, String[] tags) throws ApiException {
        try {
            ContentValues v = new ContentValues();
            v.put("mid", modelId);
            v.put("flds", join(fields));
            v.put("tags", joinTags(tags));
            Uri noteUri = c.getContentResolver().insert(NOTES, v);
            if (noteUri == null) throw new ApiException("写入失败（AnkiDroid 拒绝了）");

            // 默认会进"默认牌组"，这里把卡片挪到目标牌组
            Uri cardsUri = Uri.withAppendedPath(noteUri, "cards");
            Cursor cur = c.getContentResolver().query(cardsUri, null, null, null, null);
            if (cur != null) {
                try {
                    while (cur.moveToNext()) {
                        int idx = cur.getColumnIndex("ord");
                        if (idx < 0) continue;
                        String ord = cur.getString(idx);
                        ContentValues cv = new ContentValues();
                        cv.put("deck_id", deckId);
                        c.getContentResolver().update(
                                Uri.withAppendedPath(cardsUri, ord), cv, null, null);
                    }
                } finally {
                    cur.close();
                }
            }
            try {
                return Long.parseLong(noteUri.getLastPathSegment());
            } catch (Exception e) {
                return -1;
            }
        } catch (ApiException e) {
            throw e;
        } catch (Exception e) {
            throw new ApiException("加笔记出错：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
    }

    // ------------------------------------------------------------ 读：牌组 / 搜索 / 删除

    private static final Uri MODELS_FOR_READ = Uri.withAppendedPath(BASE, "models");

    /**
     * 本机所有牌组名。
     * 注意列名要用契约里的名字：`deck_id` / `deck_name`（牌组没有 `_id` 这一列，
     * provider 是按契约列名逐个匹配填值的，写错列名就一行都拿不到）。
     */
    public static String[] deckNames(Context c) throws ApiException {
        java.util.ArrayList<String> out = new java.util.ArrayList<String>();
        try {
            Cursor cur = c.getContentResolver().query(DECKS,
                    new String[]{"deck_id", "deck_name"}, null, null, null);
            if (cur != null) {
                try {
                    while (cur.moveToNext()) {
                        String n = cur.getString(cur.getColumnIndex("deck_name"));
                        if (n != null && n.trim().length() > 0) out.add(n.trim());
                    }
                } finally {
                    cur.close();
                }
            }
        } catch (Exception e) {
            throw new ApiException("读取牌组失败：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
        return out.toArray(new String[0]);
    }

    /** modelId -> 字段名数组（做详情展示时要知道字段名） */
    static java.util.HashMap<Long, String[]> fieldNames(Context c) throws ApiException {
        java.util.HashMap<Long, String[]> map = new java.util.HashMap<Long, String[]>();
        try {
            Cursor cur = c.getContentResolver().query(MODELS_FOR_READ,
                    new String[]{"_id", "name", "field_names"}, null, null, null);
            if (cur != null) {
                try {
                    while (cur.moveToNext()) {
                        long id = cur.getLong(0);
                        String f = cur.getString(2);
                        map.put(id, f == null ? new String[0] : split(f.trim()));
                    }
                } finally {
                    cur.close();
                }
            }
        } catch (Exception e) {
            throw new ApiException("读取笔记类型失败：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
        return map;
    }

    /**
     * 按 Anki 搜索语法查笔记（`deck:"xxx" tag:yyy 关键词`），返回**与 AnkiConnect notesInfo 同构**的 JSON：
     * `[{noteId, modelName, tags:[...], fields:{"字段名":{value, order}}}]`
     * —— 这样浏览页的渲染代码两边共用一套。
     */
    public static org.json.JSONArray searchNotes(Context c, String query, int limit)
            throws ApiException {
        org.json.JSONArray out = new org.json.JSONArray();
        String sel = (query == null || query.trim().length() == 0) ? null : query.trim();
        try {
            java.util.HashMap<Long, String[]> fnames = fieldNames(c);
            Cursor cur = c.getContentResolver().query(NOTES,
                    new String[]{"_id", "mid", "flds", "tags"}, sel, null, "mod DESC");
            if (cur == null) return out;
            try {
                while (cur.moveToNext() && out.length() < limit) {
                    long id = cur.getLong(0);
                    long mid = cur.getLong(1);
                    String[] vals = split(cur.getString(2));
                    String tagsStr = cur.getString(3);
                    String[] names = fnames.get(mid);
                    if (names == null || names.length == 0) {
                        names = new String[vals.length];
                        for (int i = 0; i < vals.length; i++) names[i] = "字段" + (i + 1);
                    }
                    org.json.JSONObject n = new org.json.JSONObject();
                    n.put("noteId", id);
                    n.put("modelName", "");
                    org.json.JSONArray tags = new org.json.JSONArray();
                    if (tagsStr != null) {
                        for (String t : tagsStr.trim().split("\\s+")) {
                            if (t.length() > 0) tags.put(t);
                        }
                    }
                    n.put("tags", tags);
                    org.json.JSONObject fields = new org.json.JSONObject();
                    for (int i = 0; i < names.length; i++) {
                        org.json.JSONObject fv = new org.json.JSONObject();
                        fv.put("value", i < vals.length ? vals[i] : "");
                        fv.put("order", i);
                        fields.put(names[i], fv);
                    }
                    n.put("fields", fields);
                    out.put(n);
                }
            } finally {
                cur.close();
            }
        } catch (Exception e) {
            throw new ApiException("查询失败：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
        return out;
    }

    /** 删除本机的一张笔记（按 note id） */
    public static int deleteNotes(Context c, long[] ids) throws ApiException {
        if (ids == null || ids.length == 0) return 0;
        int n = 0;
        try {
            for (long id : ids) {
                n += c.getContentResolver().delete(
                        Uri.withAppendedPath(NOTES, Long.toString(id)), null, null);
            }
        } catch (Exception e) {
            throw new ApiException("删除失败：" + e.getMessage()
                    + (e instanceof SecurityException ? "（AnkiDroid 未授权）" : ""));
        }
        return n;
    }

    /** 把 AnkiDroid 拉到前台（同步、图形化编辑都交给它做——API 里没有这两个能力） */
    public static void openApp(Context c) {
        try {
            android.content.Intent i = c.getPackageManager()
                    .getLaunchIntentForPackage(providerPackage(c));
            if (i == null) i = new android.content.Intent(
                    android.content.Intent.ACTION_MAIN).addCategory(
                    android.content.Intent.CATEGORY_LAUNCHER)
                    .setPackage(providerPackage(c));
            i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
            c.startActivity(i);
        } catch (Exception ignored) { }
    }

    // ------------------------------------------------------------ 一步到位

    /**
     * 确保牌组与笔记类型存在，然后把这条笔记写进本机 AnkiDroid。
     * 字段顺序用 {@link CardFormat#FIELDS}，模板与样式也复用 CardFormat 里那套，保证与电脑端写入的卡片完全一致。
     */
    public static long saveNote(Context c, String deckName, org.json.JSONObject note,
                                String[] tags) throws ApiException {
        long did = ensureDeck(c, deckName);
        long mid = ensureModel(c, CardFormat.MODEL_NAME, CardFormat.FIELDS,
                CardFormat.CARD_FRONT, CardFormat.CARD_BACK, CardFormat.CARD_CSS);
        String[] values = new String[CardFormat.FIELDS.length];
        for (int i = 0; i < CardFormat.FIELDS.length; i++) {
            values[i] = note == null ? "" : note.optString(CardFormat.FIELDS[i], "");
        }
        return addNote(c, did, mid, values, tags);
    }

    // ------------------------------------------------------------ 小工具

    static String join(String[] a) {
        if (a == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < a.length; i++) {
            if (i > 0) sb.append(FIELD_SEP);
            sb.append(a[i] == null ? "" : a[i].replace(FIELD_SEP, " "));
        }
        return sb.toString();
    }

    static String[] split(String flds) {
        if (flds == null) return new String[0];
        return flds.split(FIELD_SEP, -1);
    }

    static String joinTags(String[] tags) {
        if (tags == null || tags.length == 0) return "";
        StringBuilder sb = new StringBuilder(" ");
        for (String t : tags) {
            if (t == null) continue;
            String s = t.trim();
            if (s.length() == 0) continue;
            sb.append(s).append(' ');
        }
        return sb.length() <= 2 ? "" : sb.toString();
    }
}
