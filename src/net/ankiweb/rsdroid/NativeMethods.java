package net.ankiweb.rsdroid;

/**
 * Anki 官方 Rust 后端（rslib）的 JNI 入口。
 *
 * ⚠️ 这个类名、包名、方法签名**必须**与 librsdroid.so 里导出的符号一致
 * （`Java_net_ankiweb_rsdroid_NativeMethods_*`），否则 `System.loadLibrary` 之后
 * 调用原生方法会抛 UnsatisfiedLinkError。所以这里一个字母都不能改。
 *
 * 返回值约定（见 rslib-bridge/src/lib.rs 的 pack_result）：
 *   `[结果字节, null]` = 成功；`[null, 错误字节]` = 失败（错误是 BackendError 的 protobuf）。
 *   极端情况（内存不足）返回 null。
 */
public final class NativeMethods {

    static {
        System.loadLibrary("rsdroid");
    }

    private NativeMethods() { }

    /** 初始化后端；入参是 anki.backend.BackendInit 的 protobuf 字节 */
    public static native byte[][] openBackend(byte[] data);

    /** 释放后端（指针来自 openBackend 返回的 Int64） */
    public static native void closeBackend(long backendPointer);

    /**
     * 调用一个后端 RPC。
     *
     * @param backendPointer openBackend 返回的指针
     * @param service        服务编号（见 RPC_SERVICE_*）
     * @param method         方法编号（见 RPC_METHOD_*）
     * @param args           请求的 protobuf 字节
     */
    public static native byte[][] runMethodRaw(long backendPointer, int service, int method, byte[] args);
}
