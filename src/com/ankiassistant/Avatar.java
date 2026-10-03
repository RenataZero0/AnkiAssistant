package com.ankiassistant;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.graphics.Typeface;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;

/**
 * 每个 AnkiWeb 账号（按邮箱）一个头像。
 *
 * AnkiWeb 本身没有头像功能，所以这里不依赖任何自建服务器：
 *   1. 先看本地缓存（filesDir/avatars/<邮箱哈希>-<风格>.png）
 *   2. 没有再按邮箱去 **Gravatar** 取（邮箱哈希是 Gravatar 的公开约定）
 *   3. Gravatar 没有该邮箱时，退回 **DiceBear**（按邮箱种子生成，风格可选）
 *   4. 都取不到（离线等）→ 本地画一个"首字母 + 主色圆"的头像
 *
 * 全程只用邮箱的哈希 / 种子，不向任何服务发送密码；换设备也能得到同一个头像。
 */
public class Avatar {

    /** 可选风格：id 用于存储，label 给界面，dicebear 为空表示走 Gravatar 优先 */
    /** 界面上只给两种选择：自己传一张，或换回默认（Gravatar）。 */
    public static final String[][] STYLES = {
            {"custom", "从相册选择（可裁剪）", "custom"},
            {"auto", "换回默认头像（Gravatar）", ""},
    };

    private static String md5(String s) {
        try {
            MessageDigest md = MessageDigest.getInstance("MD5");
            byte[] d = md.digest((s == null ? "" : s.trim().toLowerCase()).getBytes("UTF-8"));
            StringBuilder sb = new StringBuilder();
            for (byte b : d) sb.append(String.format("%02x", b));
            return sb.toString();
        } catch (Exception e) {
            return Integer.toHexString((s == null ? "" : s).hashCode());
        }
    }

    /** 缓存文件（风格变了就是另一个文件） */
    public static File cacheFile(Context c, String email, String style) {
        File dir = new File(c.getFilesDir(), "avatars");
        if (!dir.exists()) dir.mkdirs();
        return new File(dir, md5(email) + "-" + (style == null ? "auto" : style) + ".png");
    }

    /** 只读缓存，没有就返回 null（供界面同步使用） */
    public static Bitmap cached(Context c, String email, String style) {
        File f = cacheFile(c, email, style);
        if (!f.exists()) return null;
        try {
            return BitmapFactory.decodeFile(f.getAbsolutePath());
        } catch (Exception e) {
            return null;
        }
    }

    /**
     * 取头像：先缓存，再联网，最后本地生成。**必须在后台线程调用**。
     */
    public static Bitmap load(Context c, String email, String style) {
        Bitmap b = cached(c, email, style);
        if (b != null) return b;
        if ("custom".equals(style) || "url".equals(style)) {
            // 这两种风格只能靠用户提供；没有缓存就直接用字母头像顶上
            b = letterBitmap(email, 0);
            return b;
        }
        if (email == null || email.trim().length() == 0) {
            return save(c, email, style, letterBitmap(email, 0));
        }
        b = fetch(email, style);
        if (b == null) b = letterBitmap(email, 0);
        return save(c, email, style, b);
    }

    /** 写入缓存；统一在这里做圆形遮罩，返回"圆的那张"给界面直接使用 */
    private static Bitmap save(Context c, String email, String style, Bitmap b) {
        if (b == null) return null;
        Bitmap round = circular(b);
        try {
            File f = cacheFile(c, email, style);
            FileOutputStream out = new FileOutputStream(f);
            round.compress(Bitmap.CompressFormat.PNG, 100, out);
            out.close();
        } catch (Exception ignored) { }
        return round;
    }

    /** 把方形头像裁成圆形（透明背景的四角） */
    public static Bitmap circular(Bitmap src) {
        if (src == null) return null;
        int size = Math.min(src.getWidth(), src.getHeight());
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
        // 用着色器把原图画进圆里，边缘自带抗锯齿
        android.graphics.BitmapShader shader = new android.graphics.BitmapShader(
                src, android.graphics.Shader.TileMode.CLAMP, android.graphics.Shader.TileMode.CLAMP);
        android.graphics.Matrix m = new android.graphics.Matrix();
        float k = (float) size / Math.min(src.getWidth(), src.getHeight());
        m.postScale(k, k);
        shader.setLocalMatrix(m);
        p.setShader(shader);
        cv.drawCircle(size / 2f, size / 2f, size / 2f, p);
        if (src != out) src.recycle();
        return out;
    }

    /** 清掉某个邮箱所有风格缓存（换头像时用） */
    public static void clear(Context c, String email) {
        File dir = new File(c.getFilesDir(), "avatars");
        String prefix = md5(email);
        File[] files = dir.listFiles();
        if (files == null) return;
        for (File f : files) {
            if (f.getName().startsWith(prefix)) f.delete();
        }
    }

    private static Bitmap fetch(String email, String style) {
        String hash = md5(email);
        String dicebear = null;
        for (String[] row : STYLES) {
            if (row[0].equals(style)) dicebear = row[2];
        }
        if (dicebear == null) dicebear = "";

        // 0) 自定义：本地文件（上面缓存没命中就说明还没设置过）
        if ("custom".equals(dicebear) || "url".equals(dicebear)) return null;

        // 1) 本地字母：不联网
        if ("local".equals(dicebear)) return null;

        // 2) Gravatar（自动风格时才试；指定了生成风格就直接用 DiceBear）
        if (dicebear.length() == 0) {
            Bitmap b = http("https://www.gravatar.com/avatar/" + hash + "?d=404&s=200");
            if (b != null) return b;
            dicebear = "identicon";
        }

        // 3) DiceBear
        Bitmap b = http("https://api.dicebear.com/9.x/" + dicebear
                + "/png?size=200&seed=" + hash);
        if (b != null) return b;
        return null;
    }

    private static Bitmap http(String url) {
        InputStream in = null;
        try {
            HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setConnectTimeout(6000);
            conn.setReadTimeout(8000);
            conn.setInstanceFollowRedirects(true);
            conn.setRequestProperty("User-Agent", "AnkiAssistant");
            if (conn.getResponseCode() != 200) return null;
            in = conn.getInputStream();
            Bitmap b = BitmapFactory.decodeStream(in);
            return b;
        } catch (Exception e) {
            return null;
        } finally {
            try { if (in != null) in.close(); } catch (Exception ignored) { }
        }
    }


    // ------------------------------------------------------------ 跨设备：走收藏库同步

    /** 媒体文件名（按邮箱固定，方便其它设备按同一个名字找） */
    public static String mediaName(String email) {
        return "ankiassistant-avatar-" + md5(email) + ".png";
    }

    private static String configKey(String email) {
        return "ankiassistant.avatar." + md5(email);
    }

    /** 收藏库媒体目录：与 AnkiEngine.openCollection 里给后端的路径保持一致 */
    public static File mediaDir(Context c) {
        return new File(c.getFilesDir(), "collection.media");
    }

    /**
     * 把本机这张头像上传到收藏库（媒体 + 配置），随后的同步会带给其它设备。
     * **必须在后台线程调用**，且需要引擎可用。
     */
    public static boolean pushToCloud(Context c, String email, android.graphics.Bitmap bmp) {
        if (email == null || email.trim().length() == 0 || bmp == null) return false;
        try {
            AnkiEngine e = EngineHolder.get(c);
            java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
            bmp.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, bos);
            String name = e.addMediaFile(mediaName(email), bos.toByteArray());
            if (name == null || name.length() == 0) return false;
            e.setConfigJson(configKey(email), "{\"file\":\"" + name + "\"}");
            android.util.Log.i("AnkiAssistant", "头像已写入收藏库：" + name);
            return true;
        } catch (Throwable t) {
            android.util.Log.w("AnkiAssistant", "头像上云失败：" + t.getMessage());
            return false;
        }
    }

    /**
     * 本机没有头像时，看看收藏库里有没有别的设备同步过来的。
     * **必须在后台线程调用**。找到就缓存到本地并返回。
     */
    public static Bitmap pullFromCloud(Context c, String email, String style) {
        if (email == null || email.trim().length() == 0) return null;
        try {
            AnkiEngine e = EngineHolder.get(c);
            String json = e.getConfigJson(configKey(email));
            if (json == null) return null;
            org.json.JSONObject o = new org.json.JSONObject(json);
            String name = o.optString("file", "");
            if (name.length() == 0) return null;
            File f = new File(mediaDir(c), name);
            if (!f.exists()) return null;              // 媒体还没同步下来
            Bitmap b = android.graphics.BitmapFactory.decodeFile(f.getAbsolutePath());
            if (b == null) return null;
            return save(c, email, style, square(b, 256));
        } catch (Throwable t) {
            return null;
        }
    }

    /** 从网址下载并保存为该邮箱的头像（用于"从网址"风格；GitHub raw 之类都能用） */
    public static Bitmap downloadInto(Context c, String email, String style, String url) {
        if (url == null || url.trim().length() == 0) return null;
        Bitmap b = http(url.trim());
        if (b == null) return null;
        return save(c, email, style, square(b, 256));
    }

    /** 直接保存一张用户选的图片（会先裁成正方形并缩到 256） */
    public static Bitmap saveCustom(Context c, String email, Bitmap src) {
        if (src == null) return null;
        return save(c, email, "custom", square(src, 256));
    }

    /** 居中裁成正方形并缩放到指定边长 */
    public static Bitmap square(Bitmap src, int size) {
        if (src == null) return null;
        int w = src.getWidth(), h = src.getHeight();
        int side = Math.min(w, h);
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Paint p = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
        Rect srcR = new Rect((w - side) / 2, (h - side) / 2, (w - side) / 2 + side, (h - side) / 2 + side);
        Rect dstR = new Rect(0, 0, size, size);
        cv.drawBitmap(src, srcR, dstR, p);
        src.recycle();
        return out;
    }

    /** 本地生成的字母头像：主色圆 + 首字母（完全离线） */
    public static Bitmap letterBitmap(String email, int size) {
        int s = size <= 0 ? 200 : size;
        Bitmap b = Bitmap.createBitmap(s, s, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(b);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        p.setColor(Ui.ACCENT);
        cv.drawCircle(s / 2f, s / 2f, s / 2f, p);

        String ch = "A";
        if (email != null && email.trim().length() > 0) {
            ch = email.trim().substring(0, 1).toUpperCase();
        }
        p.setColor(0xFFFFFFFF);
        p.setTypeface(Typeface.DEFAULT_BOLD);
        p.setTextSize(s * 0.46f);
        p.setTextAlign(Paint.Align.CENTER);
        Rect r = new Rect();
        p.getTextBounds(ch, 0, ch.length(), r);
        cv.drawText(ch, s / 2f, s / 2f + r.height() / 2f, p);
        return b;
    }
}
