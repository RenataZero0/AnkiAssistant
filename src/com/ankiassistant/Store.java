package com.ankiassistant;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/**
 * 设置与本地草稿。全部存 SharedPreferences（键值都是字符串，草稿是 JSON 数组字符串）。
 * 断网时保存的卡片先进草稿箱，联网后可一键补发。
 */
public class Store {

    private final SharedPreferences sp;

    public Store(Context ctx) {
        sp = ctx.getApplicationContext().getSharedPreferences("anki_assistant", Context.MODE_PRIVATE);
        // 旧版本只有一把 aiApiKey，这里迁移到"当前服务商"名下（之后每个服务商各存一把）
        String legacy = sp.getString("aiApiKey", "");
        if (legacy != null && legacy.length() > 0) {
            String key = "aiKey_" + aiProvider();
            if (!sp.contains(key)) sp.edit().putString(key, legacy).apply();
        }
        // 默认牌组迁移：旧默认（已被删除的 NCUK::专业术语）换成收藏库里已有的牌组
        String deck = sp.getString("defaultDeck", "");
        if (deck.length() == 0 || OLD_DEFAULT_DECK.equals(deck) || deck.startsWith("NCUK")) {
            sp.edit().putString("defaultDeck", DEFAULT_DECK).apply();
        }
    }

    // ------------------------------------------------------------------ 设置

    /**
     * 优先用**内置引擎**（APK 里自带的 Anki 官方 Rust 后端）。默认开启：
     * 可用就用它（不需要电脑、也不需要 AnkiDroid）；不可用时回退到 AnkiDroid。
     */
    public boolean useEngine() { return sp.getBoolean("useEngine", true); }
    public void setUseEngine(boolean v) { sp.edit().putBoolean("useEngine", v).apply(); }

    // ------------------------------------------------------------ 输出格式 config

    /** 所有 config（永远包含内置默认那条） */
    /** 所有格式：内置三个（不可改）+ 用户自建的 */
    public java.util.List<CardConfig> configs() {
        java.util.List<CardConfig> out = new java.util.ArrayList<CardConfig>();
        out.addAll(CardConfig.builtins());
        try {
            org.json.JSONArray arr = new org.json.JSONArray(sp.getString("configs", "[]"));
            for (int i = 0; i < arr.length(); i++) {
                org.json.JSONObject o = arr.optJSONObject(i);
                if (o == null) continue;
                CardConfig c = CardConfig.fromJson(o);
                if (c.id == null || c.id.length() == 0) continue;
                if (isBuiltinId(c.id)) continue;   // 内置的那几个不从这里来
                out.add(c);
            }
        } catch (Exception ignored) { }
        return out;
    }

    private static boolean isBuiltinId(String id) {
        for (CardConfig c : CardConfig.builtins()) {
            if (c.id.equals(id)) return true;
        }
        return false;
    }

    /** 当前选中的 config（找不到就回落内置默认） */
    public CardConfig activeConfig() {
        String id = sp.getString("activeConfigId", CardConfig.ID_VOCAB_DEFAULT);
        for (CardConfig c : configs()) {
            if (c.id.equals(id)) return c;
        }
        return CardConfig.defaultConfig();
    }

    public void setActiveConfigId(String id) {
        put("activeConfigId", id == null ? CardConfig.ID_VOCAB_DEFAULT : id);
    }

    /** 新增或更新一条自定义格式（内置三个不允许改） */
    public void saveConfig(CardConfig c) {
        if (c == null || c.id == null || c.id.length() == 0) return;
        if (isBuiltinId(c.id)) return;
        org.json.JSONArray arr = new org.json.JSONArray();
        boolean replaced = false;
        for (CardConfig x : configs()) {
            if (isBuiltinId(x.id)) continue;
            if (x.id.equals(c.id)) { arr.put(c.toJson()); replaced = true; }
            else arr.put(x.toJson());
        }
        if (!replaced) arr.put(c.toJson());
        put("configs", arr.toString());
    }

    /** 删除自定义格式（内置三个不能删） */
    public void deleteConfig(String id) {
        if (id == null || isBuiltinId(id)) return;
        org.json.JSONArray arr = new org.json.JSONArray();
        for (CardConfig x : configs()) {
            if (isBuiltinId(x.id) || x.id.equals(id)) continue;
            arr.put(x.toJson());
        }
        put("configs", arr.toString());
        if (id.equals(sp.getString("activeConfigId", ""))) setActiveConfigId(CardConfig.ID_VOCAB_DEFAULT);
    }
    /** AnkiWeb 账号（只存邮箱；密码不落盘，登录后只保留后端签发的 hkey） */
    public String ankiWebUser() { return sp.getString("ankiWebUser", ""); }
    public void setAnkiWebUser(String v) { put("ankiWebUser", v == null ? "" : v.trim()); }

    /** AnkiWeb 登录凭证（hkey，由后端签发；过期后需要重新输密码登录） */
    public String ankiWebHkey() { return sp.getString("ankiWebHkey", ""); }
    public void setAnkiWebHkey(String v) { put("ankiWebHkey", v == null ? "" : v.trim()); }

    /**
     * 同步端点。AnkiWeb 会把部分账号迁到别的同步节点（例如 sync2.ankiweb.net），
     * 记下来就不用每次从老节点再跳一次。
     */
    public String syncEndpoint() { return sp.getString("syncEndpoint", ""); }
    public void setSyncEndpoint(String v) { put("syncEndpoint", v == null ? "" : v.trim()); }

    /** 上次同步成功的时间（毫秒，0 = 从未） */
    public long lastSyncAt() { return sp.getLong("lastSyncAt", 0L); }
    public void setLastSyncAt(long v) { sp.edit().putLong("lastSyncAt", v).apply(); }

    /**
     * 内置引擎不可用时，退回本机 AnkiDroid（装了才生效）。默认开启。
     */
    public boolean useAnkiDroid() { return sp.getBoolean("useAnkiDroid", true); }
    public void setUseAnkiDroid(boolean v) { sp.edit().putBoolean("useAnkiDroid", v).apply(); }

    public String aiProvider() { return sp.getString("aiProvider", AiClient.P_ZHIPU); }
    public void setAiProvider(String v) { put("aiProvider", v); }

    /** 当前服务商的 API Key */
    public String aiApiKey() { return aiApiKeyFor(aiProvider()); }

    /**
     * 指定服务商的 API Key。
     * 优先用本机存下的（设置里手填/粘贴过）；没存过就用 {@link Secret} 里内置的密文 Key
     * —— 所以新设备装完直接能用，不用手输；发现异常在设置里换一把即可（手填的优先）。
     */
    public String aiApiKeyFor(String provider) {
        String v = sp.getString("aiKey_" + provider, "");
        if (v != null && v.trim().length() > 0) return v.trim();
        return Secret.defaultKey(provider);
    }

    /** 本机是否存过自己的 Key（内置 Key 不算） */
    public boolean hasOwnApiKey(String provider) {
        String v = sp.getString("aiKey_" + provider, "");
        return v != null && v.trim().length() > 0;
    }

    public void setAiApiKey(String v) { setAiApiKeyFor(aiProvider(), v); }

    public void setAiApiKeyFor(String provider, String v) {
        put("aiKey_" + provider, v == null ? "" : v);
    }

    public String aiModel() { return sp.getString("aiModel", ""); }
    public void setAiModel(String v) { put("aiModel", v); }

    public String aiBaseUrl() { return sp.getString("aiBaseUrl", ""); }
    public void setAiBaseUrl(String v) { put("aiBaseUrl", v); }

    /** AI 填充时的学科背景（决定释义风格） */
    public String subject() { return sp.getString("subject", "CIE A-Level / NCUK IFY 数学、物理术语"); }
    public void setSubject(String v) { put("subject", v); }

    public String defaultDeck() { return sp.getString("defaultDeck", DEFAULT_DECK); }
    public void setDefaultDeck(String v) { put("defaultDeck", v); }

    /** 默认牌组：用收藏库里已有的那个（原来那个 NCUK::专业术语 已被删除） */
    public static final String DEFAULT_DECK = "A Level Pure Mathematics";
    private static final String OLD_DEFAULT_DECK = "NCUK::专业术语";

    public String defaultTags() { return sp.getString("defaultTags", "ALevel::Maths"); }
    public void setDefaultTags(String v) { put("defaultTags", v); }

    public boolean autoSync() { return sp.getBoolean("autoSync", true); }
    public void setAutoSync(boolean v) { sp.edit().putBoolean("autoSync", v).apply(); }

    /**
     * 是否让思考型模型"想"（智谱 glm-4.5/4.7 支持 thinking 参数）。
     * 默认关闭：实测 glm-4.5-flash 开思考 23 秒、思考 1279 字还把 JSON 挤到截断，
     * 关掉后 4.5 秒且一次成型。开启时思考内容会显示在制卡页的「AI 思考过程」里。
     */
    public boolean aiThinking() { return sp.getBoolean("aiThinking", false); }
    public void setAiThinking(boolean v) { sp.edit().putBoolean("aiThinking", v).apply(); }

    public boolean introShown() { return sp.getBoolean("introShown", false); }
    public void setIntroShown(boolean v) { sp.edit().putBoolean("introShown", v).apply(); }

    /** 当前生效的 AI 接口地址：自定义档直接读 aiBaseUrl，预设档按 id 推导（允许覆盖） */
    public String aiBaseUrlEffective() {
        String custom = aiBaseUrl().trim();
        if (custom.length() > 0) return custom;
        return AiClient.presetBaseUrl(aiProvider());
    }

    public String aiModelEffective() {
        String m = aiModel().trim();
        if (m.length() > 0) return m;
        return AiClient.presetModel(aiProvider());
    }

    private void put(String k, String v) {
        sp.edit().putString(k, v == null ? "" : v).apply();
    }

    // ------------------------------------------------------------------ 草稿

    public JSONArray drafts() {
        try {
            return new JSONArray(sp.getString("drafts", "[]"));
        } catch (JSONException e) {
            return new JSONArray();
        }
    }

    public int draftCount() { return drafts().length(); }

    public void addDraft(String word, JSONObject fields, String deck, String tags) {
        JSONArray arr = drafts();
        try {
            JSONObject o = new JSONObject();
            o.put("word", word == null ? "" : word);
            o.put("fields", fields == null ? new JSONObject() : fields);
            o.put("deck", deck == null ? "" : deck);
            o.put("tags", tags == null ? "" : tags);
            o.put("time", System.currentTimeMillis());
            arr.put(o);
            put("drafts", arr.toString());
        } catch (JSONException ignored) { }
    }

    public void removeDraft(int index) {
        JSONArray arr = drafts();
        if (index < 0 || index >= arr.length()) return;
        JSONArray next = new JSONArray();
        for (int i = 0; i < arr.length(); i++) {
            if (i != index) next.put(arr.opt(i));
        }
        put("drafts", next.toString());
    }

    public void clearDrafts() { put("drafts", "[]"); }
}
