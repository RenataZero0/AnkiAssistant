package com.ankiassistant;

import android.content.Context;

/**
 * 内置引擎与 AnkiWeb 之间的同步。
 *
 * 后端把同步协议整个包了（登录、比对、分块传输、媒体），这里只负责按正确的顺序调用，
 * 以及把"需要全量同步"这种决定交回给用户（和 Anki 桌面版的行为一致）。
 *
 * 流程：
 *   1. 登录（拿 hkey）—— 只存 hkey，**不存密码**
 *   2. syncStatus：0 无需同步 / 1 普通同步 / 2 需要全量同步
 *   3. 普通同步：syncCollection（顺带同步媒体）
 *   4. 全量同步：必须由用户决定 上传本机 还是 下载云端 → fullUploadOrDownload
 */
public class AnkiSync {

    /** 需要用户决定全量同步方向时抛这个，界面弹"上传/下载"对话框 */
    public static class FullSyncRequired extends Exception {
        public final String reason;
        public FullSyncRequired(String reason) {
            super(reason);
            this.reason = reason;
        }
    }

    public static class Outcome {
        public final String message;
        public final boolean changed;
        public Outcome(String message, boolean changed) {
            this.message = message;
            this.changed = changed;
        }
    }

    /** 登录并把 hkey 存下来（密码不会落盘） */
    public static String login(Context c, Store store, String user, String pass)
            throws Exception {
        if (user == null || user.trim().length() == 0) throw new Exception("请填写 AnkiWeb 邮箱");
        if (pass == null || pass.length() == 0) throw new Exception("请填写 AnkiWeb 密码");
        AnkiEngine e = EngineHolder.get(c);
        String hkey = e.syncLogin(user.trim(), pass, null);
        if (hkey == null || hkey.length() == 0) throw new Exception("登录没有返回凭证");
        store.setAnkiWebUser(user);
        store.setAnkiWebHkey(hkey);
        return hkey;
    }

    /**
     * 同步一次。
     *
     * @param user/pass 非空则先登录（用户主动点了"登录并同步"）；为空则用已存的 hkey
     * @param fullMode  需要全量同步时的选择：null=抛出 {@link FullSyncRequired} 让界面问用户，
     *                  TRUE=上传本机，FALSE=下载云端
     */
    public static Outcome sync(Context c, Store store, String user, String pass, Boolean fullMode)
            throws Exception {
        AnkiEngine e = EngineHolder.get(c);
        e.setEndpoint(store.syncEndpoint());   // 上次记下的同步节点，直接用

        String hkey = store.ankiWebHkey();
        if (user != null && user.trim().length() > 0 && pass != null && pass.length() > 0) {
            hkey = login(c, store, user, pass);
        }
        if (hkey == null || hkey.length() == 0) {
            throw new Exception("还没有登录 AnkiWeb（请填邮箱和密码后点「登录并同步」）");
        }

        int need = e.syncRequired(hkey);
        if (need == 0) {
            store.setLastSyncAt(System.currentTimeMillis());
            return new Outcome("已是最新，无需同步 ✓", false);
        }

        // 先跑一次普通同步：既能让后端判断要不要全量，也能拿到全量同步必须带的 serverUsn
        // （AnkiDroid 的做法：serverUsn = 上一次 syncCollection 响应里的 serverMediaUsn）
        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, true);
        store.setSyncEndpoint(e.endpoint());
        Integer serverUsn = info.serverUsn;
        if (!info.fullSyncNeeded) {
            store.setLastSyncAt(System.currentTimeMillis());
            return new Outcome(info.serverMessage.length() > 0
                    ? ("同步完成 ✓ " + info.serverMessage)
                    : "同步完成 ✓ 本机与 AnkiWeb 已一致（含媒体）", true);
        }

        // 需要全量同步：方向必须由用户决定
        if (fullMode == null) {
            throw new FullSyncRequired(info.downloadOnly
                    ? "云端和本机差异较大，需要全量同步（本机卡片很少，建议下载云端）"
                    : "AnkiWeb 要求全量同步");
        }
        e.fullUploadOrDownload(hkey, fullMode.booleanValue(), serverUsn);
        safeMediaSync(e, hkey);
        store.setSyncEndpoint(e.endpoint());
        store.setLastSyncAt(System.currentTimeMillis());
        return new Outcome("全量同步完成（" + (fullMode.booleanValue() ? "上传本机" : "下载云端") + "）✓",
                true);
    }

    private static void safeMediaSync(AnkiEngine e, String hkey) {
        try {
            e.syncMedia(hkey);
        } catch (Exception ignored) {
            // 全量同步后媒体没同步上不算致命，下一次普通同步会补
        }
    }

    /** 退出登录：清掉 hkey（密码本来就没存） */
    public static void logout(Store store) {
        store.setAnkiWebHkey("");
    }

    /** 状态一句话 */
    public static String describe(Store store) {
        String user = store.ankiWebUser();
        if (store.ankiWebHkey().length() == 0) {
            return user.length() > 0
                    ? "账号 " + user + "　·　已退出登录（需要重新输密码）"
                    : "尚未登录 AnkiWeb";
        }
        long t = store.lastSyncAt();
        String when = t <= 0 ? "还没同步过"
                : "上次同步 " + new java.text.SimpleDateFormat("MM-dd HH:mm", java.util.Locale.US)
                        .format(new java.util.Date(t));
        return "账号 " + (user.length() > 0 ? user : "(未记录)") + "　·　已登录　·　" + when;
    }
}
