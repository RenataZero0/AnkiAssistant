package com.ankiassistant;

import android.content.Context;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 更新日志的读取与自动更新（与 StudyCompanion 完全一致的做法）。
 *
 * 内容优先级：
 *   1. 从 GitHub 拉下来的缓存（应用私有目录里的 CHANGELOG.md）
 *   2. 打包进 APK 的 assets/CHANGELOG.md
 *
 * 这样即使装的是旧版本，也能看到新版本改了什么；断网时也有东西可看。
 * raw.githubusercontent.com 是匿名可读的，不需要令牌。
 */
public class Changelog {

    /** 仓库里 CHANGELOG.md 的原始地址（仓库公开，不需要登录） */
    public static final String URL = "https://raw.githubusercontent.com/"
            + Updater.OWNER + "/" + Updater.REPO + "/main/CHANGELOG.md";

    private static final int TIMEOUT = 15000;

    static File cacheFile(Context c) { return new File(c.getFilesDir(), "CHANGELOG.md"); }

    public static boolean hasCache(Context c) {
        try { return cacheFile(c).exists(); } catch (Exception e) { return false; }
    }

    /** 当前能读到的最新内容：缓存优先，其次打包进 APK 的那份 */
    public static String local(Context c) {
        try {
            File f = cacheFile(c);
            if (f.exists()) return read(new FileInputStream(f));
        } catch (Exception ignored) { }
        try {
            return read(c.getAssets().open("CHANGELOG.md"));
        } catch (Exception e) {
            return "";
        }
    }

    /** 兼容旧调用：当前可用的日志全文 */
    public static String text(Context c) { return local(c); }

    /** 缓存时间（没有缓存时返回空串） */
    public static String cacheTime(Context c) {
        try {
            File f = cacheFile(c);
            if (!f.exists()) return "";
            return new java.text.SimpleDateFormat("MM-dd HH:mm", java.util.Locale.getDefault())
                    .format(new java.util.Date(f.lastModified()));
        } catch (Exception e) { return ""; }
    }

    /** 内容来源的一句话说明，显示在阅读器标题下面 */
    public static String sourceLabel(Context c) {
        if (hasCache(c)) return "来自 GitHub 最新版（缓存于 " + cacheTime(c) + "）";
        return "来自应用内置副本（离线）";
    }

    /**
     * 从 GitHub 拉最新的一份并写入缓存。
     * 成功返回 true；失败返回 false 并把原因写进 err[0]（不抛异常，拉不到不该打扰用户）。
     */
    public static boolean fetch(Context c, String[] err) {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(URL).openConnection();
            conn.setConnectTimeout(TIMEOUT);
            conn.setReadTimeout(TIMEOUT);
            conn.setRequestProperty("User-Agent", "AnkiAssistant-Android");
            conn.setRequestProperty("Cache-Control", "no-cache");
            int code = conn.getResponseCode();
            if (code < 200 || code >= 300) {
                if (err != null) err[0] = "HTTP " + code;
                return false;
            }
            String text = read(conn.getInputStream());
            if (text == null || text.length() < 50) {
                if (err != null) err[0] = "内容为空";
                return false;
            }
            FileOutputStream fos = new FileOutputStream(cacheFile(c));
            fos.write(text.getBytes("UTF-8"));
            fos.close();
            return true;
        } catch (Exception e) {
            if (err != null) err[0] = String.valueOf(e.getMessage());
            return false;
        } finally {
            if (conn != null) try { conn.disconnect(); } catch (Exception ignored) { }
        }
    }

    /** 日志里第一条 "## vX.Y.Z" 的版本号（不带 v） */
    public static String latestVersion(String md) {
        if (md == null) return "";
        Matcher m = Pattern.compile("^##\\s*v([0-9][0-9.]*)", Pattern.MULTILINE).matcher(md);
        return m.find() ? m.group(1) : "";
    }

    /** **只看打包进 APK 的那份**（不含从 GitHub 拉来的缓存） */
    public static String bundled(Context c) {
        try {
            return read(c.getAssets().open("CHANGELOG.md"));
        } catch (Exception e) {
            return "";
        }
    }

    /**
     * 打包进 APK 的日志版本与当前运行版本不一致时给个提醒（构建时忘了改版本号）。
     * 注意必须看 **assets 里那份**，不能用 local()（那是缓存优先）—— 否则日志一更新就会误报。
     */
    public static boolean bundledNewer(Context c) {
        String latest = latestVersion(bundled(c));
        return latest.length() > 0 && !latest.equals(Version.VERSION_NUMBER);
    }

    static String read(InputStream in) throws Exception {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        in.close();
        return new String(bos.toByteArray(), "UTF-8");
    }
}
