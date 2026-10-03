"""头像跨设备同步：存进收藏库的媒体文件夹，文件名记在收藏库配置里。
   AnkiWeb 同步会带上媒体与配置 → 别的设备同步后自然就有了，不需要任何第三方服务。
"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")

# ---------------- 1) AnkiEngine：配置读写 + 加媒体文件 ----------------
p = APP + r"\src\com\ankiassistant\AnkiEngine.java"
s = io.open(p, encoding="utf-8").read()
if "addMediaFile" not in s:
    s = s.replace("    private static final int M_GET_NOTETYPE_NAMES = 8;",
                  "    private static final int M_GET_NOTETYPE_NAMES = 8;\n\n"
                  "    private static final int S_CONFIG = 9;\n"
                  "    private static final int M_GET_CONFIG_JSON = 0;\n"
                  "    private static final int M_SET_CONFIG_JSON = 1;\n\n"
                  "    private static final int S_MEDIA = 41;\n"
                  "    private static final int M_ADD_MEDIA_FILE = 2;")

    METHODS = '''
    // ------------------------------------------------------------ 收藏库配置 / 媒体
    // 这两样都会跟着 AnkiWeb 同步走，所以可以用它们把"跟账号相关的小东西"带到所有设备。

    /** 读收藏库配置里的 JSON 字符串（没有就返回 null） */
    public String getConfigJson(String key) throws EngineException {
        try {
            anki.generic.Json json = parse(anki.generic.Json.parser(),
                    call(S_CONFIG, M_GET_CONFIG_JSON,
                            anki.generic.String.newBuilder().setVal(key).build()), "读取配置");
            String v = json.getJson().toStringUtf8();
            return v == null || v.length() == 0 ? null : v;
        } catch (EngineException e) {
            return null;   // 没有这个键时后端可能直接报错，按"没设置"处理
        }
    }

    /** 写收藏库配置（会跟着同步走） */
    public void setConfigJson(String key, String jsonValue) throws EngineException {
        call(S_CONFIG, M_SET_CONFIG_JSON, anki.config.SetConfigJsonRequest.newBuilder()
                .setKey(key)
                .setValueJson(com.google.protobuf.ByteString.copyFromUtf8(jsonValue))
                .setUndoable(false)
                .build());
    }

    /** 把文件放进收藏库的媒体目录（返回实际文件名；媒体会随同步上传） */
    public String addMediaFile(String desiredName, byte[] data) throws EngineException {
        anki.generic.String res = parse(anki.generic.String.parser(),
                call(S_MEDIA, M_ADD_MEDIA_FILE, anki.media.AddMediaFileRequest.newBuilder()
                        .setDesiredName(desiredName)
                        .setData(com.google.protobuf.ByteString.copyFrom(data))
                        .build()), "写入媒体文件");
        return res.getVal();
    }
'''
    s = s.replace("    // ------------------------------------------------------------ 底层收发", METHODS + "\n    // ------------------------------------------------------------ 底层收发")
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("AnkiEngine 已加配置与媒体接口")

# ---------------- 2) Avatar：本地 + 云端 ----------------
p2 = APP + r"\src\com\ankiassistant\Avatar.java"
s2 = io.open(p2, encoding="utf-8").read()
if "pushToCloud" not in s2:
    CLOUD = '''
    // ------------------------------------------------------------ 跨设备：走收藏库同步

    /** 媒体文件名（按邮箱固定，方便其它设备按同一个名字找） */
    public static String mediaName(String email) {
        return "ankiassistant-avatar-" + md5(email) + ".png";
    }

    private static String configKey(String email) {
        return "ankiassistant.avatar." + md5(email);
    }

    /** 收藏库媒体目录（引擎初始化时用的就是这个路径） */
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
            e.setConfigJson(configKey(email), "{\\"file\\":\\"" + name + "\\"}");
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
            save(c, email, style, square(b, 256));
            return cached(c, email, style);
        } catch (Throwable t) {
            return null;
        }
    }
'''
    s2 = s2.replace("    /** 从网址下载并保存为该邮箱的头像", CLOUD + "\n    /** 从网址下载并保存为该邮箱的头像")
    io.open(p2, "w", encoding="utf-8", newline="\n").write(s2)
    print("Avatar 已支持跨设备同步")
