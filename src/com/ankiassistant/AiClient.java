package com.ankiassistant;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.Charset;

/**
 * AI 自动填充。全部走 OpenAI 兼容的 /chat/completions 协议，所以换服务商只是换地址和模型名。
 * 内置四个预设 + 一个自定义，用户在设置里自己挑（需求原文："可内置调用一个免费AI，如deepseek免费版或豆包，最好可以自己选择"）。
 *
 * 注意：不在 APK 里内置任何 API Key —— 打包进 APK 的密钥任何人都能提取盗用。
 * 免费档提供「智谱 GLM-4-Flash（免费）」与「Pollinations（免密钥）」两个预设。
 *
 * buildRequestBody / parseContent 不依赖安卓，可在电脑 JVM 上自检。
 */
public class AiClient {

    public static class AiException extends Exception {
        public AiException(String msg) { super(msg); }
        public AiException(String msg, Throwable t) { super(msg, t); }
    }

    /** 一次调用的结果：正文 + 思考过程（思考型模型会返回 reasoning_content，普通模型为空） */
    public static class Reply {
        public final String content;
        public final String reasoning;
        Reply(String content, String reasoning) {
            this.content = content == null ? "" : content;
            this.reasoning = reasoning == null ? "" : reasoning;
        }
    }

    // ------------------------------------------------------------------ 预设

    public static final String P_DEEPSEEK = "deepseek";
    public static final String P_DOUBAO = "doubao";
    public static final String P_ZHIPU = "zhipu";
    public static final String P_SILICON = "siliconflow";
    public static final String P_CUSTOM = "custom";

    public static String presetLabel(String id) {
        if (P_DEEPSEEK.equals(id)) return "DeepSeek";
        if (P_DOUBAO.equals(id)) return "豆包（火山方舟）";
        if (P_ZHIPU.equals(id)) return "智谱 GLM-4.5-Flash（免费）";
        if (P_SILICON.equals(id)) return "硅基流动 SiliconFlow";
        if (P_CUSTOM.equals(id)) return "自定义（OpenAI 兼容）";
        return id;
    }

    public static String presetBaseUrl(String id) {
        if (P_DEEPSEEK.equals(id)) return "https://api.deepseek.com/chat/completions";
        if (P_DOUBAO.equals(id)) return "https://ark.cn-beijing.volces.com/api/v3/chat/completions";
        if (P_ZHIPU.equals(id)) return "https://open.bigmodel.cn/api/paas/v4/chat/completions";
        if (P_SILICON.equals(id)) return "https://api.siliconflow.cn/v1/chat/completions";
        return "";
    }

    public static String presetModel(String id) {
        if (P_DEEPSEEK.equals(id)) return "deepseek-chat";
        if (P_DOUBAO.equals(id)) return "doubao-seed-1-6-250615";
        if (P_ZHIPU.equals(id)) return "glm-4.5-flash";
        if (P_SILICON.equals(id)) return "Qwen/Qwen2.5-7B-Instruct";
        return "";
    }

    /** 全部预设都需要 API Key（免费的只是额度免费，注册后仍要 Key） */
    public static boolean needsKey(String id) {
        return true;
    }

    // ------------------------------------------------------------------ 请求/响应（可自检）

    public static String buildRequestBody(String model, String system, String user) {
        return buildRequestBody(model, system, user, false);
    }

    /**
     * @param disableThinking true 时带上 thinking={"type":"disabled"}（智谱思考型模型专用）。
     *   实测：glm-4.5-flash 开思考 23 秒、思考内容 1279 字还把 JSON 输出挤到截断；
     *   关掉之后 4.5 秒、一次成型 —— 制卡这种任务没必要让它"想"。
     */
    public static String buildRequestBody(String model, String system, String user,
                                          boolean disableThinking) {
        try {
            JSONObject o = new JSONObject();
            o.put("model", model == null || model.trim().length() == 0 ? "deepseek-chat" : model.trim());
            JSONArray msgs = new JSONArray();
            if (system != null && system.length() > 0) {
                JSONObject s = new JSONObject();
                s.put("role", "system");
                s.put("content", system);
                msgs.put(s);
            }
            JSONObject u = new JSONObject();
            u.put("role", "user");
            u.put("content", user);
            msgs.put(u);
            o.put("messages", msgs);
            o.put("temperature", 0.4);
            if (disableThinking) {
                JSONObject t = new JSONObject();
                t.put("type", "disabled");
                o.put("thinking", t);
            }
            return o.toString();
        } catch (JSONException e) {
            return "{}";
        }
    }

    /** 从 OpenAI 风格响应里取 content；失败抛 AiException（带上服务端给的 error.message） */
    public static String parseContent(String body) throws AiException {
        return parseReply(body).content;
    }

    /** 取 content + reasoning_content */
    public static Reply parseReply(String body) throws AiException {
        if (body == null || body.trim().length() == 0) throw new AiException("AI 返回了空响应");
        JSONObject o;
        try {
            o = new JSONObject(body);
        } catch (JSONException e) {
            throw new AiException("AI 返回了无法解析的内容：" + head(body), e);
        }
        if (o.has("error")) {
            String msg;
            try {
                if (o.get("error") instanceof JSONObject) {
                    msg = o.getJSONObject("error").optString("message", String.valueOf(o.opt("error")));
                } else {
                    msg = o.optString("error", "unknown error");
                }
            } catch (JSONException inner) {
                msg = String.valueOf(o.opt("error"));
            }
            throw new AiException("AI 接口报错：" + msg);
        }
        try {
            JSONArray choices = o.getJSONArray("choices");
            if (choices.length() == 0) throw new AiException("AI 没有返回任何内容");
            JSONObject msg = choices.getJSONObject(0).getJSONObject("message");
            String c = msg.optString("content", "");
            String r = msg.optString("reasoning_content", "");
            if (c.trim().length() == 0 && r.trim().length() == 0) throw new AiException("AI 返回了空内容");
            return new Reply(c, r);
        } catch (JSONException e) {
            throw new AiException("AI 响应格式不认识：" + head(body), e);
        }
    }

    private static String head(String s) {
        return s.length() <= 200 ? s : s.substring(0, 200) + "…";
    }

    // ------------------------------------------------------------------ 网络

    public String chat(String baseUrl, String apiKey, String model,
                       String system, String user) throws AiException {
        return chatDetailed(baseUrl, apiKey, model, system, user, false).content;
    }

    public Reply chatDetailed(String baseUrl, String apiKey, String model,
                              String system, String user, boolean disableThinking) throws AiException {
        String url = baseUrl == null ? "" : baseUrl.trim();
        if (url.length() == 0) throw new AiException("还没填 AI 接口地址，请到「设置」里选择 AI 服务商");
        if (!url.contains("://")) url = "https://" + url;
        byte[] body = buildRequestBody(model, system, user, disableThinking)
                .getBytes(Charset.forName("UTF-8"));

        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setConnectTimeout(8000);
            // 免费档（智谱）思考型模型实测要 50-65 秒，别用 60 秒卡死它
            conn.setReadTimeout(180000);
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            conn.setRequestProperty("Accept", "application/json");
            String k = apiKey == null ? "" : apiKey.trim();
            if (k.length() > 0) conn.setRequestProperty("Authorization", "Bearer " + k);

            OutputStream os = conn.getOutputStream();
            os.write(body);
            os.flush();
            os.close();

            int code = conn.getResponseCode();
            InputStream in = (code >= 200 && code < 400) ? conn.getInputStream() : conn.getErrorStream();
            String resp = readAll(in);
            if (code < 200 || code >= 400) {
                String detail = resp;
                try {
                    JSONObject e = new JSONObject(resp).getJSONObject("error");
                    detail = e.optString("message", resp);
                } catch (JSONException ignored) { }
                throw new AiException("AI 接口 HTTP " + code + "：" + head(detail));
            }
            return parseReply(resp);
        } catch (AiException e) {
            throw e;
        } catch (java.net.SocketTimeoutException e) {
            throw new AiException("AI 响应超时（已等待 3 分钟），请重试", e);
        } catch (Exception e) {
            throw new AiException("调用 AI 失败：" + e.getMessage(), e);
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private static String readAll(InputStream in) throws Exception {
        if (in == null) return "";
        java.io.BufferedReader r = new java.io.BufferedReader(
                new java.io.InputStreamReader(in, Charset.forName("UTF-8")));
        StringBuilder sb = new StringBuilder();
        String line;
        while ((line = r.readLine()) != null) sb.append(line).append('\n');
        r.close();
        return sb.toString();
    }
}
