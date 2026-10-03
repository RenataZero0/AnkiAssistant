package com.ankiassistant;

import android.os.Handler;
import android.os.Looper;
import android.util.Log;

/** 线程小工具：网络请求放后台，结果回主线程。不用 lambda（d8 处理 invokedynamic 不稳）。 */
public class Th {

    private static final Handler UI = new Handler(Looper.getMainLooper());

    /** 回主线程执行 */
    public static void ui(Runnable r) {
        UI.post(r);
    }

    /** 后台线程执行；异常吞掉并打日志，避免未捕获异常直接闪退 */
    public static void bg(final Runnable r) {
        Thread t = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    r.run();
                } catch (Throwable e) {
                    Log.e("AnkiAssistant", "background task failed", e);
                }
            }
        }, "anki-bg");
        t.start();
    }
}
