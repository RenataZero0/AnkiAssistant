package com.ankiassistant;

import android.content.Context;
import android.util.Log;

import com.google.protobuf.MessageLite;

import java.io.File;
import java.util.LinkedHashMap;
import java.util.Map;

import anki.backend.BackendError;
import anki.backend.BackendInit;
import anki.collection.CloseCollectionRequest;
import anki.collection.OpenCollectionRequest;
import anki.collection.OpChangesWithId;
import anki.decks.Deck;
import anki.decks.DeckNameId;
import anki.decks.DeckNames;
import anki.decks.GetDeckNamesRequest;
import anki.generic.Int64;
import anki.generic.Json;
import anki.notes.AddNoteRequest;
import anki.notes.AddNoteResponse;
import anki.notes.Note;
import anki.notetypes.NotetypeId;
import anki.sync.FullUploadOrDownloadRequest;
import anki.sync.SyncAuth;
import anki.sync.SyncCollectionRequest;
import anki.sync.SyncCollectionResponse;
import anki.sync.SyncLoginRequest;
import anki.sync.SyncStatusResponse;
import net.ankiweb.rsdroid.NativeMethods;

/**
 * 把 Anki 官方 Rust 后端（rslib / librsdroid.so）包成应用能用的形态。
 *
 * 「工具全部内化进 APK」就是靠这个：APK 里带着 librsdroid.so，设备上自己开一个 Anki 收藏库、
 * 自己写卡片、自己跟 AnkiWeb 同步 —— **既不需要电脑，也不需要装 AnkiDroid**。
 *
 * 调用方式：所有后端能力都通过 {@code NativeMethods.runMethodRaw(ptr, service, method, 请求字节)}
 * 用 protobuf 收发。service/method 编号是从后端生成的 GeneratedBackend.kt 里抄来的常量，
 * 不能凭感觉改（改了会调到别的 RPC 上）。
 *
 * ⚠️ 许可证：rslib/rsdroid 是 AGPL-3.0，随本 APK 分发后本应用也需以 AGPL-3.0 兼容方式发布。
 */
public class AnkiEngine {

    private static final String TAG = "AnkiAssistant";

    // ---- 服务编号 ----
    private static final int S_SYNC = 1;
    private static final int S_COLLECTION = 3;
    private static final int S_DECKS = 7;
    private static final int S_NOTETYPES = 23;
    private static final int S_NOTES = 25;
    private static final int S_SEARCH = 29;

    // ---- 方法编号（sync）----
    private static final int M_SYNC_MEDIA = 0;
    private static final int M_SYNC_LOGIN = 3;
    private static final int M_SYNC_STATUS = 4;
    private static final int M_SYNC_COLLECTION = 5;
    private static final int M_FULL_UPLOAD_OR_DOWNLOAD = 6;

    // ---- 方法编号（collection / decks / notetypes / notes）----
    private static final int M_OPEN_COLLECTION = 0;
    private static final int M_CLOSE_COLLECTION = 1;
    private static final int M_ADD_DECK = 1;
    private static final int M_GET_DECK_NAMES = 13;
    private static final int M_ADD_NOTETYPE_LEGACY = 2;
    private static final int M_GET_NOTETYPE_ID_BY_NAME = 10;
    private static final int M_ADD_NOTE = 1;
    private static final int M_GET_NOTE = 6;
    private static final int M_REMOVE_NOTES = 7;
    private static final int M_GET_FIELD_NAMES = 16;
    private static final int M_SEARCH_NOTES = 2;

    /** 与电脑端一致：这就是写进 Anki 的笔记类型名 */
    private static final String NOTETYPE_NAME = CardFormat.MODEL_NAME;

    private long ptr = -1;
    private boolean collectionOpen;
    private String collectionPath;

    public static class EngineException extends Exception {
        public EngineException(String m) { super(m); }
    }

    // ------------------------------------------------------------ 生命周期

    /** 起一个后端实例（很轻，真正干活前不需要开收藏库） */
    public AnkiEngine() throws EngineException {
        BackendInit init = BackendInit.newBuilder().addPreferredLangs("en").build();
        byte[][] out = NativeMethods.openBackend(init.toByteArray());
        check(out, "启动内置 Anki 引擎");
        try {
            ptr = Int64.parseFrom(out[0]).getVal();
        } catch (Exception e) {
            throw new EngineException("解析后端句柄失败：" + e.getMessage());
        }
        if (ptr == 0) throw new EngineException("内置引擎没有返回有效句柄");
    }

    /**
     * 打开（不存在则创建）设备上的收藏库。库放在应用私有目录，卸载即清除。
     *
     * @param ctx 用来取 filesDir
     */
    public void openCollection(Context ctx) throws EngineException {
        File dir = ctx.getFilesDir();
        File col = new File(dir, "collection.anki2");
        openCollection(col.getAbsolutePath());
    }

    public void openCollection(String path) throws EngineException {
        String base = path.endsWith(".anki2") ? path.substring(0, path.length() - 6) : path;
        OpenCollectionRequest req = OpenCollectionRequest.newBuilder()
                .setCollectionPath(path)
                .setMediaFolderPath(base + ".media")
                .setMediaDbPath(base + ".media.db")
                .build();
        call(S_COLLECTION, M_OPEN_COLLECTION, req);
        collectionOpen = true;
        collectionPath = path;
        Log.i(TAG, "内置引擎已打开收藏库：" + path);
    }

    public String collectionPath() { return collectionPath; }

    public void close() {
        try {
            if (collectionOpen) {
                call(S_COLLECTION, M_CLOSE_COLLECTION,
                        CloseCollectionRequest.newBuilder().setDowngradeToSchema11(false).build());
            }
        } catch (Exception e) {
            Log.w(TAG, "关闭收藏库出错：" + e.getMessage());
        } finally {
            collectionOpen = false;
            if (ptr > 0) {
                try { NativeMethods.closeBackend(ptr); } catch (Throwable t) { /* 忽略 */ }
                ptr = -1;
            }
        }
    }

    // ------------------------------------------------------------ 牌组 / 笔记类型 / 笔记

    /** 现有牌组：名字 -> id */
    public Map<String, Long> deckNames() throws EngineException {
        DeckNames names = parse(DeckNames.parser(), 
                call(S_DECKS, M_GET_DECK_NAMES, GetDeckNamesRequest.newBuilder().build()), "读取牌组列表");
        Map<String, Long> map = new LinkedHashMap<String, Long>();
        for (DeckNameId d : names.getEntriesList()) map.put(d.getName(), d.getId());
        return map;
    }

    /** 建牌组，返回 deckId（名字里带 :: 会自动建父子层级） */
    public long addDeck(String name) throws EngineException {
        // Deck 的 kind 是 oneof（普通 / 筛选），必须显式给出，否则后端报 "missing kind"
        Deck deck = Deck.newBuilder()
                .setName(name)
                .setNormal(Deck.Normal.getDefaultInstance())
                .build();
        OpChangesWithId res = parse(OpChangesWithId.parser(),
                call(S_DECKS, M_ADD_DECK, deck), "建牌组");
        return res.getId();
    }

    /** 有就复用，没有就建 */
    public long ensureDeck(String name) throws EngineException {
        Map<String, Long> decks = deckNames();
        Long id = decks.get(name);
        return id != null ? id : addDeck(name);
    }

    /** 按名字找笔记类型；没有返回 -1 */
    public long notetypeIdByName(String name) throws EngineException {
        try {
            NotetypeId id = parse(NotetypeId.parser(), call(S_NOTETYPES, M_GET_NOTETYPE_ID_BY_NAME,
                    anki.generic.String.newBuilder().setVal(name).build()), "按名字找笔记类型");
            return id.getNtid();
        } catch (EngineException e) {
            return -1;   // 后端在找不到时会报错，这里当成"没有"
        }
    }

    /**
     * 用 Anki 的 legacy JSON 建笔记类型（比自己拼 Notetype proto 稳，缺的字段由后端补默认值）。
     * JSON 结构与电脑端 AnkiConnect 的 createModel 参数一致，保证两边卡片长得一样。
     */
    public long addNotetypeLegacy(String name, String[] fields, String css,
                                  String qfmt, String afmt) throws EngineException {
        // flds 必须是"字段对象"数组（NoteFieldSchema11），tmpls 同理（CardTemplateSchema11），
        // 写成字符串数组后端会报 invalid type: string ... expected struct NoteFieldSchema11。
        // 新版 schema 要求字段/模板各带 id，模型本身还要 mod/usn —— 一次给全，免得逐个报缺字段
        long base = System.currentTimeMillis();
        long modSec = base / 1000L;
        StringBuilder flds = new StringBuilder();
        for (int i = 0; i < fields.length; i++) {
            if (i > 0) flds.append(',');
            flds.append("{\"id\":").append(base + i)
                    .append(",\"name\":\"").append(esc(fields[i])).append("\",\"ord\":").append(i)
                    .append(",\"sticky\":false,\"rtl\":false,\"font\":\"Arial\",\"size\":20,\"media\":[]}");
        }
        String json = "{"
                // 新增笔记类型时 id 必须为 0 —— 后端 storage 里有 assert_eq!(nt.id.0, 0)，由它自己分配 id
                + "\"id\":0,"
                + "\"name\":\"" + esc(name) + "\","
                + "\"type\":0,\"mod\":" + modSec + ",\"usn\":-1,\"sortf\":0,\"did\":1,"
                + "\"latexPre\":\"\",\"latexPost\":\"\","
                + "\"flds\":[" + flds + "],"
                + "\"tmpls\":[{\"id\":" + (base + 200) + ",\"name\":\"卡片 1\",\"ord\":0,"
                + "\"qfmt\":\"" + esc(qfmt) + "\",\"afmt\":\"" + esc(afmt) + "\","
                + "\"bqfmt\":\"\",\"bafmt\":\"\",\"did\":1,\"bfont\":\"Arial\",\"bsize\":12}],"
                + "\"css\":\"" + esc(css) + "\","
                // req 是 (card_ord, kind, field_ords) 的元组数组，必须与模板一一对应，空数组会触发后端断言
                + "\"req\":[[0,\"any\",[0]]]"
                + "}";
        OpChangesWithId res = parse(OpChangesWithId.parser(), call(S_NOTETYPES, M_ADD_NOTETYPE_LEGACY,
                Json.newBuilder().setJson(com.google.protobuf.ByteString.copyFromUtf8(json)).build()),
                "建笔记类型");
        return res.getId();
    }

    /** 保证「专业术语卡」存在（字段名与模板都对得上才复用），返回 notetypeId */
    public long ensureCardNotetype() throws EngineException {
        long mid = notetypeIdByName(NOTETYPE_NAME);
        if (mid > 0) return mid;
        return addNotetypeLegacy(NOTETYPE_NAME, CardFormat.FIELDS, CardFormat.CARD_CSS,
                CardFormat.CARD_FRONT, CardFormat.CARD_BACK);
    }

    /** 写一张笔记，返回 noteId */
    public long addNote(long deckId, long notetypeId, String[] fields, String[] tags)
            throws EngineException {
        Note.Builder note = Note.newBuilder().setNotetypeId(notetypeId);
        for (String f : fields) note.addFields(f == null ? "" : f);
        if (tags != null) {
            for (String t : tags) if (t != null && t.trim().length() > 0) note.addTags(t.trim());
        }
        AddNoteRequest req = AddNoteRequest.newBuilder()
                .setNote(note.build())
                .setDeckId(deckId)
                .build();
        AddNoteResponse res = parse(AddNoteResponse.parser(), call(S_NOTES, M_ADD_NOTE, req), "写入笔记");
        return res.getNoteId();
    }

    /** 按 Anki 搜索语法查笔记，返回**与 AnkiConnect notesInfo 同构**的 JSON（浏览页两边共用一套渲染） */
    public org.json.JSONArray searchNotes(String query, int limit) throws EngineException {
        java.util.LinkedHashMap<Long, String[]> cache = new java.util.LinkedHashMap<Long, String[]>();
        org.json.JSONArray out = new org.json.JSONArray();
        byte[] raw = call(S_SEARCH, M_SEARCH_NOTES,
                anki.search.SearchRequest.newBuilder()
                        .setSearch(query == null ? "" : query)
                        .build());
        anki.search.SearchResponse resp = parse(anki.search.SearchResponse.parser(), raw, "查询笔记");
        java.util.List<Long> ids = resp.getIdsList();
        for (int i = 0; i < ids.size() && i < limit; i++) {
            long id = ids.get(i);
            Note n;
            try {
                n = parse(Note.parser(), call(S_NOTES, M_GET_NOTE,
                        anki.notes.NoteId.newBuilder().setNid(id).build()), "读取笔记");
            } catch (EngineException e) {
                continue;   // 单条读取失败不影响其它结果
            }
            String[] names = cache.get(n.getNotetypeId());
            if (names == null) {
                names = fieldNames(n.getNotetypeId());
                cache.put(n.getNotetypeId(), names);
            }
            org.json.JSONObject o = new org.json.JSONObject();
            try {
                o.put("noteId", n.getId());
                org.json.JSONArray tags = new org.json.JSONArray();
                for (String t : n.getTagsList()) tags.put(t);
                o.put("tags", tags);
                org.json.JSONObject fields = new org.json.JSONObject();
                java.util.List<String> vals = n.getFieldsList();
                for (int k = 0; k < names.length; k++) {
                    org.json.JSONObject fv = new org.json.JSONObject();
                    fv.put("value", k < vals.size() ? vals.get(k) : "");
                    fv.put("order", k);
                    fields.put(names[k], fv);
                }
                o.put("fields", fields);
                o.put("modelName", "");
                out.put(o);
            } catch (org.json.JSONException ignored) { }
        }
        return out;
    }

    /** 只要 id（调试/计数用） */
    public long[] searchIds(String query, int limit) throws EngineException {
        java.util.List<Long> ids = parse(anki.search.SearchResponse.parser(),
                call(S_SEARCH, M_SEARCH_NOTES, anki.search.SearchRequest.newBuilder()
                        .setSearch(query == null ? "" : query).build()), "查询笔记").getIdsList();
        int n = Math.min(ids.size(), limit);
        long[] out = new long[n];
        for (int i = 0; i < n; i++) out[i] = ids.get(i);
        return out;
    }

    /** 某条笔记的笔记类型 id */
    public long noteNotetypeId(long noteId) throws EngineException {
        return parse(Note.parser(), call(S_NOTES, M_GET_NOTE,
                anki.notes.NoteId.newBuilder().setNid(noteId).build()), "读取笔记").getNotetypeId();
    }
    /** 查到的总数（只取 id，不读内容） */
    public int searchCount(String query) throws EngineException {
        return parse(anki.search.SearchResponse.parser(),
                call(S_SEARCH, M_SEARCH_NOTES, anki.search.SearchRequest.newBuilder()
                        .setSearch(query == null ? "" : query).build()), "查询笔记").getIdsCount();
    }

    /** 某个笔记类型的字段名 */
    public String[] fieldNames(long notetypeId) throws EngineException {
        anki.generic.StringList list = parse(anki.generic.StringList.parser(),
                call(S_NOTETYPES, M_GET_FIELD_NAMES,
                        anki.notetypes.NotetypeId.newBuilder().setNtid(notetypeId).build()),
                "读取字段名");
        return list.getValsList().toArray(new String[0]);
    }

    /** 删除笔记 */
    public int removeNotes(long[] ids) throws EngineException {
        anki.notes.RemoveNotesRequest.Builder b = anki.notes.RemoveNotesRequest.newBuilder();
        for (long id : ids) b.addNoteIds(id);
        parse(anki.collection.OpChangesWithCount.parser(),
                call(S_NOTES, M_REMOVE_NOTES, b.build()), "删除笔记");
        return ids.length;
    }

    // ------------------------------------------------------------ 同步（AnkiWeb）

    /**
     * 用 AnkiWeb 账号登录，成功返回 hkey（后续调用都用它）。
     * endpoint 传空则用官方 AnkiWeb。
     */
    public String syncLogin(String username, String password, String endpoint)
            throws EngineException {
        SyncLoginRequest.Builder b = SyncLoginRequest.newBuilder()
                .setUsername(username == null ? "" : username)
                .setPassword(password == null ? "" : password);
        if (endpoint != null && endpoint.trim().length() > 0) b.setEndpoint(endpoint.trim());
        SyncAuth auth = parse(SyncAuth.parser(), call(S_SYNC, M_SYNC_LOGIN, b.build()), "登录 AnkiWeb");
        return auth.getHkey();
    }

    /** 需要哪种同步：0=无需同步 1=普通同步 2=需要全量同步 */
    public int syncRequired(String hkey) throws EngineException {
        SyncStatusResponse res = parse(SyncStatusResponse.parser(), 
                call(S_SYNC, M_SYNC_STATUS, authReq(hkey)), "查询同步状态");
        return res.getRequired().getNumber();
    }

    /** 全量上传（upload=true）或下载（false） */
    public void fullUploadOrDownload(String hkey, boolean upload) throws EngineException {
        call(S_SYNC, M_FULL_UPLOAD_OR_DOWNLOAD, FullUploadOrDownloadRequest.newBuilder()
                .setAuth(auth(hkey)).setUpload(upload).build());
    }

    /** 普通同步；返回后端给的"下一步还需要什么"（见 SyncCollectionResponse.ChangesRequired） */
    public int syncCollection(String hkey, boolean withMedia) throws EngineException {
        SyncCollectionResponse res = parse(SyncCollectionResponse.parser(), 
                call(S_SYNC, M_SYNC_COLLECTION, SyncCollectionRequest.newBuilder()
                        .setAuth(auth(hkey)).setSyncMedia(withMedia).build()), "同步收藏库");
        return res.getRequired().getNumber();
    }

    /** 单独同步媒体文件 */
    public void syncMedia(String hkey) throws EngineException {
        call(S_SYNC, M_SYNC_MEDIA, authReq(hkey));
    }

    private static SyncAuth auth(String hkey) {
        return SyncAuth.newBuilder().setHkey(hkey == null ? "" : hkey).build();
    }

    private static MessageLite authReq(String hkey) { return auth(hkey); }

    // ------------------------------------------------------------ 底层收发

    /**
     * 调一个 RPC。返回结果字节；后端报错时抛 EngineException（带后端给的可读消息）。
     */
    private byte[] call(int service, int method, MessageLite req) throws EngineException {
        if (ptr <= 0) throw new EngineException("内置引擎尚未启动");
        byte[][] out;
        try {
            out = NativeMethods.runMethodRaw(ptr, service, method, req.toByteArray());
        } catch (Throwable t) {
            throw new EngineException("调用内置引擎失败：" + t);
        }
        check(out, "RPC " + service + "/" + method);
        return out[0];
    }

    /** 原生返回约定：[结果, null] 成功；[null, 错误] 失败；null = 转换失败（通常是内存） */
    private static void check(byte[][] out, String what) throws EngineException {
        if (out == null) throw new EngineException(what + "：原生层返回空（可能内存不足）");
        if (out.length >= 2 && out[1] != null && out[1].length > 0) {
            throw new EngineException(what + "：" + backendMessage(out[1]));
        }
        if (out.length == 0 || out[0] == null) throw new EngineException(what + "：没有拿到结果");
    }

    /** protobuf 解析包装：把受检异常转成统一异常 */
    private static <T> T parse(com.google.protobuf.Parser<T> parser, byte[] data, String what)
            throws EngineException {
        try {
            return parser.parseFrom(data);
        } catch (Exception e) {
            throw new EngineException(what + "：解析后端返回失败（" + e.getMessage() + "）");
        }
    }
    private static String backendMessage(byte[] err) {
        try {
            BackendError e = BackendError.parseFrom(err);
            String m = e.getMessage();
            return (m == null || m.length() == 0) ? ("后端错误 " + e.getKind()) : m;
        } catch (Exception ex) {
            return "无法解析的后端错误（" + err.length + " 字节）";
        }
    }

    private static String esc(String s) {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.length() + 16);
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': sb.append("\\\""); break;
                case '\\': sb.append("\\\\"); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default:
                    if (c < 0x20) sb.append(String.format("\\u%04x", (int) c));
                    else sb.append(c);
            }
        }
        return sb.toString();
    }
}
