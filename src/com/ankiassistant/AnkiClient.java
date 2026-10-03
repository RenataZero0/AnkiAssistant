package com.ankiassistant;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.Charset;

/**
 * AnkiConnect 客户端（https://git.sr.ht/~foosoft/anki-connect，插件 ID 2055492159）。
 *
 * 为什么用它：AnkiWeb 本身没有公开的"往云端账号写卡片"的 API，
 * 官方认可的写入入口是桌面版 Anki 的 AnkiConnect 插件 ——
 * 本应用 -> 家里/宿舍电脑上的 AnkiConnect -> addNote -> sync() -> AnkiWeb 云端。
 * 同一个接口也负责"查看我的 Anki 内容"（deckNames / findNotes / notesInfo）。
 *
 * 纯 java.* + org.json，没有安卓依赖 —— 请求体构造与响应解析可以在电脑 JVM 上直接自检。
 */
public class AnkiClient {

    /** AnkiConnect 返回 error 字段时抛出，message 为 Anki 给的原因 */
    public static class AnkiException extends Exception {
        public AnkiException(String msg) { super(msg); }
        public AnkiException(String msg, Throwable t) { super(msg, t); }
    }

    public static final int API_VERSION = 6;

    private final String base;     // 形如 http://192.168.1.7:8765
    private final String apiKey;   // 可为空
    private int connectTimeout = 4000;
    private int readTimeout = 20000;

    public AnkiClient(String hostOrUrl, int port, String apiKey) {
        this.base = normalizeBase(hostOrUrl, port);
        this.apiKey = apiKey == null ? "" : apiKey.trim();
    }

    /** 容错：用户可能填 192.168.1.5、192.168.1.5:8765、http://192.168.1.5:8765/ */
    public static String normalizeBase(String hostOrUrl, int port) {
        String h = hostOrUrl == null ? "" : hostOrUrl.trim();
        if (h.length() == 0) h = "127.0.0.1";
        if (!h.contains("://")) h = "http://" + h;
        int scheme = h.indexOf("://") + 3;
        int slash = h.indexOf('/', scheme);
        if (slash >= 0) h = h.substring(0, slash);          // 去掉多余路径
        if (countColon(h.substring(scheme)) == 0) h = h + ":" + port;  // 没写端口就补默认端口
        return h;
    }

    private static int countColon(String s) {
        int n = 0;
        for (int i = 0; i < s.length(); i++) if (s.charAt(i) == ':') n++;
        return n;
    }

    public String base() { return base; }

    public void setTimeouts(int connectMs, int readMs) {
        connectTimeout = connectMs;
        readTimeout = readMs;
    }

    // ------------------------------------------------------------------ 请求/响应（可自检）

    /** 构造请求体 */
    public static String buildRequest(String action, JSONObject params) {
        try {
            JSONObject o = new JSONObject();
            o.put("action", action);
            o.put("version", API_VERSION);
            if (params != null) o.put("params", params);
            return o.toString();
        } catch (JSONException e) {
            return "{\"action\":\"" + action + "\",\"version\":" + API_VERSION + "}";
        }
    }

    /** 解析响应：error 非空 -> 抛 AnkiException；否则返回 result */
    public static Object parseResponse(String body) throws AnkiException {
        if (body == null || body.trim().length() == 0) {
            throw new AnkiException("Anki 返回了空响应");
        }
        JSONObject o;
        try {
            o = new JSONObject(body);
        } catch (JSONException e) {
            throw new AnkiException("Anki 返回了无法解析的内容：" + trim(body, 200), e);
        }
        String err = o.isNull("error") ? null : o.optString("error", null);
        if (err != null && err.length() > 0) throw new AnkiException(err);
        return o.has("result") ? o.opt("result") : null;
    }

    private static String trim(String s, int n) {
        return s.length() <= n ? s : s.substring(0, n) + "…";
    }

    // ------------------------------------------------------------------ 网络

    public Object call(String action, JSONObject params) throws AnkiException {
        return call(action, params, readTimeout);
    }

    public Object call(String action, JSONObject params, int readMs) throws AnkiException {
        String body = buildRequest(action, params);
        HttpURLConnection conn = null;
        try {
            URL url = new URL(base + "/");
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(connectTimeout);
            conn.setReadTimeout(readMs);
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            if (apiKey.length() > 0) conn.setRequestProperty("X-API-Key", apiKey);

            OutputStream os = conn.getOutputStream();
            os.write(body.getBytes("UTF-8"));
            os.flush();
            os.close();

            int code = conn.getResponseCode();
            InputStream in = (code >= 200 && code < 400) ? conn.getInputStream() : conn.getErrorStream();
            String resp = readAll(in);
            if (code < 200 || code >= 400) {
                throw new AnkiException("HTTP " + code + "：" + trim(resp, 200));
            }
            return parseResponse(resp);
        } catch (AnkiException e) {
            throw e;
        } catch (java.net.SocketTimeoutException e) {
            throw new AnkiException("连接 Anki 超时（电脑是否休眠？Anki 是否开着？）", e);
        } catch (java.net.ConnectException e) {
            throw new AnkiException("连不上 Anki：" + base + "\n请确认：电脑上的 Anki 已打开、"
                    + "已安装 AnkiConnect 插件、设置里的 IP 和端口填对、两者在同一 Wi-Fi。", e);
        } catch (Exception e) {
            throw new AnkiException("访问 Anki 失败：" + e.getMessage(), e);
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private static String readAll(InputStream in) throws Exception {
        if (in == null) return "";
        BufferedReader r = new BufferedReader(new InputStreamReader(in, Charset.forName("UTF-8")));
        StringBuilder sb = new StringBuilder();
        String line;
        while ((line = r.readLine()) != null) sb.append(line).append('\n');
        r.close();
        return sb.toString();
    }

    // ------------------------------------------------------------------ 语义封装

    public String versionString() throws AnkiException {
        Object v = call("version", null);
        return v == null ? "?" : String.valueOf(v);
    }

    /** 首次连接：AnkiConnect 会对陌生来源弹一次"允许"对话框 */
    public String requestPermission() throws AnkiException {
        Object o = call("requestPermission", null, 60000);
        return o == null ? "" : o.toString();
    }

    public JSONArray deckNames() throws AnkiException {
        return asArray(call("deckNames", null));
    }

    public JSONArray modelNames() throws AnkiException {
        return asArray(call("modelNames", null));
    }

    public JSONArray findNotes(String query) throws AnkiException {
        JSONObject p = new JSONObject();
        try { p.put("query", query); } catch (JSONException ignored) { }
        return asArray(call("findNotes", p));
    }

    public JSONArray notesInfo(JSONArray ids) throws AnkiException {
        JSONObject p = new JSONObject();
        try { p.put("notes", ids); } catch (JSONException ignored) { }
        return asArray(call("notesInfo", p));
    }

    public long addNote(JSONObject note) throws AnkiException {
        JSONObject p = new JSONObject();
        try { p.put("note", note); } catch (JSONException ignored) { }
        Object r = call("addNote", p);
        return r instanceof Number ? ((Number) r).longValue() : -1;
    }

    public JSONArray deleteNotes(JSONArray ids) throws AnkiException {
        JSONObject p = new JSONObject();
        try { p.put("notes", ids); } catch (JSONException ignored) { }
        return asArray(call("deleteNotes", p));
    }

    /**
     * 建牌组的参数。新版 AnkiConnect（2025 起的 master）把参数名从 name 改成了 deck，
     * 旧版仍是 name —— 真机上实测：新版传 name 会返回
     * "createDeck() got an unexpected keyword argument 'name'"。
     */
    public static JSONObject createDeckParams(String deck, boolean modern) {
        JSONObject p = new JSONObject();
        try { p.put(modern ? "deck" : "name", deck); } catch (JSONException ignored) { }
        return p;
    }

    public void createDeck(String deck) throws AnkiException {
        try {
            call("createDeck", createDeckParams(deck, true));
        } catch (AnkiException e) {
            String m = e.getMessage() == null ? "" : e.getMessage();
            if (m.contains("unexpected keyword argument") || m.contains("'name'")) {
                call("createDeck", createDeckParams(deck, false));   // 退回旧版参数名
            } else {
                throw e;
            }
        }
    }

    public void guiEditNote(long id) throws AnkiException {
        JSONObject p = new JSONObject();
        try { p.put("note", id); } catch (JSONException ignored) { }
        call("guiEditNote", p);
    }

    /** 同步到 AnkiWeb 云端（这一步才是"保存到我的 Anki 云端账号"） */
    public void sync() throws AnkiException {
        call("sync", null, 120000);
    }

    /** 牌组不存在就创建（createDeck 幂等） */
    public void ensureDeck(String deck) throws AnkiException {
        if (deck != null && deck.trim().length() > 0) createDeck(deck.trim());
    }

    /** 笔记类型不存在就按 CardFormat 的定义创建 */
    public void ensureModel() throws AnkiException {
        JSONArray names = modelNames();
        for (int i = 0; i < names.length(); i++) {
            if (CardFormat.MODEL_NAME.equals(names.optString(i, ""))) return;
        }
        JSONObject p = new JSONObject();
        try {
            p.put("modelName", CardFormat.MODEL_NAME);
            JSONArray inOrder = new JSONArray();
            for (int i = 0; i < CardFormat.FIELDS.length; i++) inOrder.put(CardFormat.FIELDS[i]);
            p.put("inOrderFields", inOrder);
            p.put("css", CardFormat.CARD_CSS);
            JSONArray tpl = new JSONArray();
            JSONObject t = new JSONObject();
            t.put("Name", "卡片1");
            t.put("Front", CardFormat.CARD_FRONT);
            t.put("Back", CardFormat.CARD_BACK);
            tpl.put(t);
            p.put("cardTemplates", tpl);
        } catch (JSONException ignored) { }
        call("createModel", p);
    }

    /** 保存一张卡片 + 可选同步到云端，返回 noteId */
    public long saveNote(String deck, JSONObject fields, JSONArray tags, boolean doSync) throws AnkiException {
        ensureDeck(deck);
        ensureModel();
        JSONObject note = new JSONObject();
        try {
            note.put("deckName", deck == null || deck.trim().length() == 0 ? "Default" : deck.trim());
            note.put("modelName", CardFormat.MODEL_NAME);
            note.put("fields", fields);
            note.put("tags", tags == null ? new JSONArray() : tags);
            JSONObject opt = new JSONObject();
            opt.put("allowDuplicate", false);
            note.put("options", opt);
        } catch (JSONException ignored) { }
        long id = addNote(note);
        if (id > 0) {
            // 真机实测：部分 AnkiConnect 版本（含 2025 的 master）在 Anki 26 上会把新卡片
            // 放进「系统默认」牌组，所以这里主动搬一次；已经是目标牌组时是空操作。
            try {
                moveNoteToDeck(id, deck);
            } catch (AnkiException ignored) { }
        }
        if (doSync) {
            try { sync(); } catch (AnkiException e) {
                // 卡片已经写进本地库了，同步失败不算保存失败 —— 把原因带给调用方
                throw new AnkiException("卡片已保存到本地 Anki，但同步到云端失败：" + e.getMessage(), e);
            }
        }
        return id;
    }

    /** 按 noteId 找卡片的查询语句 */
    public static String noteCardsQuery(long noteId) {
        return "nid:" + noteId;
    }

    /** 把某张笔记产生的卡片移到指定牌组 */
    public void moveNoteToDeck(long noteId, String deck) throws AnkiException {
        JSONObject q = new JSONObject();
        try { q.put("query", noteCardsQuery(noteId)); } catch (JSONException ignored) { }
        JSONArray cards = asArray(call("findCards", q));
        if (cards.length() == 0) return;
        JSONObject p = new JSONObject();
        try {
            p.put("cards", cards);
            p.put("deck", deck);
        } catch (JSONException ignored) { }
        call("changeDeck", p);
    }

    private static JSONArray asArray(Object o) {
        return o instanceof JSONArray ? (JSONArray) o : new JSONArray();
    }
}
