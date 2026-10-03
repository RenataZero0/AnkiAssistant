package com.ankiassistant;

import android.content.res.AssetManager;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;

/**
 * 极小的本机静态资源服务器：只监听 127.0.0.1，把 APK 里的 assets 暴露给 WebView。
 *
 * 为什么要这么绕：WebView 通过 file:///android_asset/ 读取「约 1MB 以上」的资源会失败
 * （MathJax 的 tex-mml-chtml.js 有 1.17MB，实测 script 直接触发 error 事件，
 * 而几十 KB 的探测文件正常，且把 assets 改成不压缩也无效）。走 http://127.0.0.1
 * 就没有这个问题，字体、脚本、样式都是普通 HTTP 响应，和 android.webkit.WebViewAssetLoader 一个思路。
 *
 * 安全性：只绑定回环地址，外部设备连不上；进程退出即关闭。
 */
public class AssetServer {

    private static final String TAG = "AssetServer";
    private static final int MAX_REQUEST = 8192;

    private final AssetManager assets;
    private ServerSocket server;
    private Thread thread;
    private volatile boolean stopped;

    public AssetServer(AssetManager assets) {
        this.assets = assets;
    }

    /** 返回监听端口；失败返回 -1（调用方自行回退到 file://） */
    public int start() {
        try {
            server = new ServerSocket(0, 16, InetAddress.getByName("127.0.0.1"));
            final ServerSocket s = server;
            thread = new Thread(new Runnable() {
                @Override
                public void run() {
                    while (!stopped && !s.isClosed()) {
                        try {
                            final Socket conn = s.accept();
                            handle(conn);
                        } catch (IOException e) {
                            if (!stopped) Log.w(TAG, "accept failed: " + e.getMessage());
                        }
                    }
                }
            }, "asset-server");
            thread.setDaemon(true);
            thread.start();
            dumpAssets();
            return server.getLocalPort();
        } catch (Exception e) {
            Log.e(TAG, "cannot start local asset server", e);
            return -1;
        }
    }

    public void stop() {
        stopped = true;
        try { if (server != null) server.close(); } catch (IOException ignored) { }
    }

    /** 监听端口；未启动或启动失败返回 -1 */
    public int port() {
        return server == null ? -1 : server.getLocalPort();
    }

    /**
     * URL 路径 -> 扁平资源名。
     * MathJax 只按固定路径请求脚本和字体，而 APK 里的资源必须平铺
     * （原因见 tools/prepare_assets.py：Windows 上 aapt2 会把嵌套 assets 写成反斜杠路径），
     * 所以映射放在这里。
     */
    static String mapAsset(String path) {
        if ("mathjax/tex-mml-chtml.js".equals(path)) return "mj-tex-mml-chtml.js";
        String fontDir = "mathjax/output/chtml/fonts/woff-v2/";
        if (path.startsWith(fontDir)) return "mj-woff-" + path.substring(fontDir.length());
        return path;
    }

    /** 浏览页详情用的动态页面内容（见 note.html 路由） */
    private volatile String noteHtml = "<html><body></body></html>";

    public void setNoteHtml(String html) {
        noteHtml = html == null ? "" : html;
    }

    /** 启动时探一下关键资源在不在。Windows 上 aapt2 曾把嵌套 assets 写成反斜杠路径，
     *  导致 AssetManager 找不到；这里只在失败时告警，正常情况不刷日志。 */
    private void dumpAssets() {
        String[] probe = {"editor.html", "mj-tex-mml-chtml.js", "mj-woff-MathJax_Main-Regular.woff"};
        for (int i = 0; i < probe.length; i++) {
            try {
                InputStream in = assets.open(probe[i]);
                int n = 0, r;
                byte[] b = new byte[8192];
                while ((r = in.read(b)) > 0) n += r;
                in.close();
                Log.i(TAG, "asset ok " + probe[i] + " (" + n + " bytes)");
            } catch (IOException e) {
                Log.e(TAG, "asset MISSING " + probe[i] + " : " + e.getMessage());
            }
        }
    }

    private void handle(Socket conn) {
        try {
            conn.setSoTimeout(15000);
            String path = readRequestPath(conn.getInputStream());
            if (path == null) { conn.close(); return; }
            int q = path.indexOf('?');
            if (q >= 0) path = path.substring(0, q);
            if (path.startsWith("/")) path = path.substring(1);
            if (path.length() == 0) path = "editor.html";

            // 浏览页详情：内容由 App 动态生成，必须走 http 提供（用 loadDataWithBaseURL 的话
            // 页面是匿名来源，MathJax 的字体请求会被 WebView 挡掉，公式就只有空白）
            if ("note.html".equals(path)) {
                byte[] note = noteHtml.getBytes("UTF-8");
                write(conn.getOutputStream(), 200, "text/html; charset=utf-8", note);
                conn.close();
                return;
            }
            path = mapAsset(path);

            byte[] body;
            try {
                InputStream in = assets.open(path);
                body = readAll(in);
                in.close();
            } catch (IOException e) {
                Log.w(TAG, "404 " + path + " : " + e.getClass().getSimpleName() + " " + e.getMessage());
                write(conn.getOutputStream(), 404, "text/plain; charset=utf-8",
                        ("not found: " + path).getBytes("UTF-8"));
                conn.close();
                return;
            }
            Log.i(TAG, "200 " + path + " (" + body.length + " bytes)");
            write(conn.getOutputStream(), 200, mime(path), body);
            conn.close();
        } catch (Exception e) {
            try { conn.close(); } catch (IOException ignored) { }
        }
    }

    private static String readRequestPath(InputStream in) throws IOException {
        // 必须把整个请求头读完再回包：只读一行就 close()，套接字里还有未读数据，
        // 内核会发 RST，浏览器拿到的是被截断的响应 —— 现象是页面空白、脚本全都不执行。
        StringBuilder sb = new StringBuilder();
        int c;
        while ((c = in.read()) != -1 && sb.length() < MAX_REQUEST) {
            sb.append((char) c);
            int n = sb.length();
            if (n >= 4 && sb.charAt(n - 4) == '\r' && sb.charAt(n - 3) == '\n'
                    && sb.charAt(n - 2) == '\r' && sb.charAt(n - 1) == '\n') {
                break;
            }
        }
        String all = sb.toString();
        int nl = all.indexOf('\n');
        if (nl < 0) return null;
        String[] parts = all.substring(0, nl).trim().split(" ");
        if (parts.length < 2) return null;
        return parts[1];
    }

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream(Math.max(1024, in.available()));
        byte[] buf = new byte[16384];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        return bos.toByteArray();
    }

    private static void write(OutputStream out, int code, String type, byte[] body) throws IOException {
        StringBuilder h = new StringBuilder();
        h.append("HTTP/1.0 ").append(code).append(code == 200 ? " OK" : " Not Found").append("\r\n");
        h.append("Content-Type: ").append(type).append("\r\n");
        h.append("Content-Length: ").append(body.length).append("\r\n");
        h.append("Cache-Control: max-age=600\r\n");
        h.append("Connection: close\r\n\r\n");
        out.write(h.toString().getBytes("UTF-8"));
        out.write(body);
        out.flush();
    }

    private static String mime(String path) {
        String p = path.toLowerCase();
        if (p.endsWith(".html") || p.endsWith(".htm")) return "text/html; charset=utf-8";
        if (p.endsWith(".js")) return "application/javascript; charset=utf-8";
        if (p.endsWith(".css")) return "text/css; charset=utf-8";
        if (p.endsWith(".json")) return "application/json; charset=utf-8";
        if (p.endsWith(".woff2")) return "font/woff2";
        if (p.endsWith(".woff")) return "font/woff";
        if (p.endsWith(".ttf")) return "font/ttf";
        if (p.endsWith(".svg")) return "image/svg+xml";
        if (p.endsWith(".png")) return "image/png";
        if (p.endsWith(".jpg") || p.endsWith(".jpeg")) return "image/jpeg";
        return "application/octet-stream";
    }
}
