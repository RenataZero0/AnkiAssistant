package com.ankiassistant;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * 自动更新：查 GitHub 上的最新 Release → 跟本机版本比对 → 下载 APK → 交给系统安装器。
 *
 * 做法与 StudyCompanion 相同（同一个 GitHub 账号的独立仓库），但简化了两处：
 *   * 仓库是公开的，查 Release 与下载都走浏览器直链，**不需要登录、不需要令牌**
 *   * 不做增量、不做静默安装：下载完弹出系统安装界面，由用户确认
 */
public class Updater {

    /** 仓库信息（换仓库只改这两行） */
    public static final String OWNER = "RenataZero0";
    public static final String REPO = "AnkiAssistant";

    public static final String API = "https://api.github.com";
    /** 仓库主页，查不到 Release 时让用户自己去看看 */
    public static final String REPO_PAGE = "https://github.com/" + OWNER + "/" + REPO;

    private static final int TIMEOUT = 20000;

    public static class Release {
        public String tag = "";
        public String name = "";
        public String notes = "";
        public String pageUrl = "";
        /** 浏览器直链（公开仓库无需令牌） */
        public String apkUrl = "";
        public long apkSize = 0;

        /** 远端是否比本机新 */
        public boolean isNewer() {
            return compareVersion(tag, Version.VERSION_TAG) > 0;
        }
    }

    public static class UpException extends Exception {
        public UpException(String m) { super(m); }
    }

    /** 最新 Release；没有 Release 时抛异常（提示用户去仓库看看） */
    public static Release latest() throws UpException {
        String body = http("GET", API + "/repos/" + OWNER + "/" + REPO + "/releases/latest", null, false);
        Release r = new Release();
        try {
            JSONObject j = new JSONObject(body);
            r.tag = j.optString("tag_name", "");
            r.name = j.optString("name", "");
            r.notes = j.optString("body", "");
            r.pageUrl = j.optString("html_url", REPO_PAGE);
            JSONArray assets = j.optJSONArray("assets");
            if (assets != null) {
                for (int i = 0; i < assets.length(); i++) {
                    JSONObject a = assets.optJSONObject(i);
                    if (a == null) continue;
                    String n = a.optString("name", "");
                    if (n.toLowerCase().endsWith(".apk")) {
                        r.apkUrl = a.optString("browser_download_url", "");
                        r.apkSize = a.optLong("size", 0);
                        break;
                    }
                }
            }
        } catch (Exception e) {
            throw new UpException("Release 信息解析失败：" + e.getMessage());
        }
        if (r.tag.length() == 0) throw new UpException("远端没有 tag");
        return r;
    }

    /** 下载 APK 到 cache/update/，返回文件 */
    public static File download(Context c, Release rel, Progress cb) throws UpException {
        if (rel.apkUrl == null || rel.apkUrl.length() == 0) {
            throw new UpException("这个 Release 里没有 APK 附件");
        }
        File out = ApkProvider.fileFor(c, "AnkiAssistant-" + rel.tag + ".apk");
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(rel.apkUrl).openConnection();
            conn.setInstanceFollowRedirects(true);
            conn.setConnectTimeout(TIMEOUT);
            conn.setReadTimeout(120000);
            conn.setRequestProperty("User-Agent", "AnkiAssistant-Android");
            int code = conn.getResponseCode();
            if (code < 200 || code >= 300) throw new UpException("下载失败：HTTP " + code);
            long total = conn.getContentLength() > 0 ? conn.getContentLength() : rel.apkSize;
            InputStream in = conn.getInputStream();
            FileOutputStream fos = new FileOutputStream(out);
            byte[] buf = new byte[16384];
            long done = 0;
            int n;
            int lastPct = -1;
            while ((n = in.read(buf)) > 0) {
                fos.write(buf, 0, n);
                done += n;
                if (cb != null && total > 0) {
                    int pct = (int) (done * 100 / total);
                    if (pct != lastPct) {
                        lastPct = pct;
                        cb.onProgress(pct);
                    }
                }
            }
            fos.close();
            in.close();
            return out;
        } catch (UpException e) {
            throw e;
        } catch (Exception e) {
            throw new UpException("下载失败：" + e.getMessage());
        } finally {
            if (conn != null) try { conn.disconnect(); } catch (Exception ignored) { }
        }
    }

    public interface Progress { void onProgress(int percent); }

    /** 数值比较版本号：a > b 返回 1（不能只比相等，否则远端更旧也会被当成有新版本） */
    public static int compareVersion(String a, String b) {
        int[] pa = parseVer(a), pb = parseVer(b);
        for (int i = 0; i < 3; i++) if (pa[i] != pb[i]) return pa[i] > pb[i] ? 1 : -1;
        return 0;
    }

    static int[] parseVer(String v) {
        int[] r = new int[3];
        if (v == null) return r;
        v = v.trim();
        if (v.startsWith("v") || v.startsWith("V")) v = v.substring(1);
        String[] parts = v.split("\\.");
        for (int i = 0; i < 3 && i < parts.length; i++) {
            String d = "";
            for (int k = 0; k < parts[i].length(); k++) {
                char ch = parts[i].charAt(k);
                if (ch < '0' || ch > '9') break;
                d += ch;
            }
            try { r[i] = Integer.parseInt(d); } catch (Exception ignored) { }
        }
        return r;
    }

    /** 极简 HTTP：所有请求都是匿名的（公开仓库），带 JSON 头 */
    private static String http(String method, String url, String body, boolean acceptJson) throws UpException {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setRequestMethod(method);
            conn.setConnectTimeout(TIMEOUT);
            conn.setReadTimeout(TIMEOUT);
            conn.setRequestProperty("Accept", acceptJson ? "application/vnd.github+json" : "application/json");
            conn.setRequestProperty("User-Agent", "AnkiAssistant-Android");
            if (body != null) {
                conn.setDoOutput(true);
                conn.setRequestProperty("Content-Type", "application/json");
                conn.getOutputStream().write(body.getBytes("UTF-8"));
            }
            int code = conn.getResponseCode();
            InputStream in = (code >= 200 && code < 300) ? conn.getInputStream() : conn.getErrorStream();
            String text = in == null ? "" : readAll(in);
            if (code == 404) throw new UpException("仓库里还没有任何 Release");
            if (code < 200 || code >= 300) throw new UpException("HTTP " + code + "：" + head(text));
            return text;
        } catch (UpException e) {
            throw e;
        } catch (Exception e) {
            throw new UpException("网络错误：" + e.getMessage());
        } finally {
            if (conn != null) try { conn.disconnect(); } catch (Exception ignored) { }
        }
    }

    private static String readAll(InputStream in) throws Exception {
        java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        in.close();
        return new String(bos.toByteArray(), "UTF-8");
    }

    private static String head(String s) {
        if (s == null) return "";
        s = s.replace('\n', ' ').trim();
        return s.length() > 160 ? s.substring(0, 160) + "…" : s;
    }
}
