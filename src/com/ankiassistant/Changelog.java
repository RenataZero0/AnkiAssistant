package com.ankiassistant;

import android.content.Context;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 更新日志读取。照 StudyCompanion 的做法，但**只读打包进 APK 的那份**（assets\CHANGELOG.md），
 * 不联网：构建时 build.ps1 会把工程根目录的 CHANGELOG.md 拷进 assets。
 */
public class Changelog {

    private static String cached;

    /** 打包在 APK 里的更新日志全文；读不到返回空串 */
    public static String text(Context c) {
        if (cached != null) return cached;
        try {
            cached = read(c.getAssets().open("CHANGELOG.md"));
        } catch (Exception e) {
            cached = "";
        }
        return cached;
    }

    /** 日志里第一条 "## vX.Y.Z" 的版本号（不带 v） */
    public static String latestVersion(String md) {
        if (md == null) return "";
        Matcher m = Pattern.compile("^##\\s*v([0-9][0-9.]*)", Pattern.MULTILINE).matcher(md);
        return m.find() ? m.group(1) : "";
    }

    /** 打包的日志版本是否比当前运行的版本更新（构建后忘了改版本号时能看出来） */
    public static boolean bundledNewer(Context c) {
        String latest = latestVersion(text(c));
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
