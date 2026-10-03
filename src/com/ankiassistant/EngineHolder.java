package com.ankiassistant;

import android.content.Context;
import android.util.Log;

/**
 * 内置引擎（Anki 官方 Rust 后端）的进程内单例。
 *
 * 起一个后端 + 开收藏库只要几十毫秒，但没必要每次保存都重开，所以常驻一个实例；
 * 应用退出/被杀时由系统回收，进程结束前 {@link #close()} 会正常关库。
 *
 * 原生库缺失（比如某个 ABI 没编进去）时一律返回"不可用"，让上层回退到别的方式，绝不崩。
 */
public class EngineHolder {

    private static final String TAG = "AnkiAssistant";
    private static AnkiEngine engine;
    private static Boolean nativeOk;      // 原生库能不能加载（缓存一次结论）
    private static String failReason = "";

    /** 原生库是否可用（不碰收藏库，也不起后端） */
    public static synchronized boolean available(Context c) {
        if (nativeOk != null) return nativeOk;
        try {
            Class.forName("net.ankiweb.rsdroid.NativeMethods");
            System.loadLibrary("rsdroid");
            nativeOk = Boolean.TRUE;
            Log.i(TAG, "内置引擎原生库可用");
        } catch (Throwable t) {
            nativeOk = Boolean.FALSE;
            failReason = String.valueOf(t);
            Log.w(TAG, "内置引擎原生库不可用：" + t);
        }
        return nativeOk;
    }

    /** 取（必要时建）常驻引擎，收藏库放在应用私有目录 */
    public static synchronized AnkiEngine get(Context c) throws Exception {
        if (engine != null) return engine;
        AnkiEngine e = new AnkiEngine();
        e.openCollection(c);
        engine = e;
        return engine;
    }

    /** 已经起过就返回，不主动创建（用于状态展示） */
    public static synchronized AnkiEngine peek() { return engine; }

    public static synchronized void close() {
        if (engine != null) {
            engine.close();
            engine = null;
        }
    }

    /** 设置页用的一句话状态 */
    public static String status(Context c) {
        if (!available(c)) return "不可用（本机 ABI 未包含原生库：" + failReason + "）";
        try {
            AnkiEngine e = get(c);
            int decks = e.deckNames().size();
            return "已就绪（本机收藏库，牌组 " + decks + " 个）";
        } catch (Exception ex) {
            return "原生库可用，但打开收藏库失败：" + ex.getMessage();
        }
    }
}
