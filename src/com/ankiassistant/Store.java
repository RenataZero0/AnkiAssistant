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

    public String ankiHost() { return sp.getString("ankiHost", ""); }
    public void setAnkiHost(String v) { put("ankiHost", v); }

    public int ankiPort() {
        try { return Integer.parseInt(sp.getString("ankiPort", "8765").trim()); }
        catch (Exception e) { return 8765; }
    }
    public void setAnkiPort(String v) { put("ankiPort", v); }

    public String ankiApiKey() { return sp.getString("ankiApiKey", ""); }
    public void setAnkiApiKey(String v) { put("ankiApiKey", v); }

    public String aiProvider() { return sp.getString("aiProvider", AiClient.P_ZHIPU); }
    public void setAiProvider(String v) { put("aiProvider", v); }

    /** 当前服务商的 API Key */
    public String aiApiKey() { return aiApiKeyFor(aiProvider()); }

    /** 指定服务商的 API Key —— 每个服务商各存一把，切换时自动带出，不用重复粘贴 */
    public String aiApiKeyFor(String provider) {
        return sp.getString("aiKey_" + provider, "");
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
