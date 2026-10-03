package com.ankiassistant;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.res.Configuration;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.util.DisplayMetrics;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * 入口 + 导航框架。
 *
 * 手机（最短边 < 600dp）：底部横栏三个页签。
 * 平板（最短边 >= 600dp）：左边一个可收起的选择栏（需求里的"左边拉一个选择框"）。
 * 旋转屏幕不重建 Activity（manifest 里声明了 configChanges），只重排容器，
 * 因此编辑到一半的卡片不会因为转屏而丢失。
 */
public class MainActivity extends Activity {

    public static final int TAB_CREATE = 0;
    public static final int TAB_BROWSE = 1;
    public static final int TAB_SETTINGS = 2;

    public Store store;
    private AssetServer assetServer;
    private CreateView createView;
    private BrowseView browseView;
    private SettingsView settingsView;

    private LinearLayout root, topBar, rail, bottomBar;
    private FrameLayout content;
    private NavItem[] railItems = new NavItem[3];
    private NavItem[] barItems = new NavItem[3];
    private TextView accountLabel;
    private android.widget.ImageView accountAvatar;
    private android.widget.ImageView topAvatar;
    private View ankiDot;
    private TextView ankiLampText;
    private boolean ankiChecking;
    private boolean tablet;
    private int current = TAB_CREATE;
    private boolean built;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        store = new Store(this);
        // 调试入口（放在 store 初始化之后，否则自检拿不到设置）
        if (getIntent() != null && getIntent().getBooleanExtra("engineTest", false)) runEngineSelfTest();
        if (getIntent() != null && getIntent().getBooleanExtra("backendSave", false)) runBackendSaveTest();
        if (getIntent() != null && getIntent().getBooleanExtra("syncProbe", false)) runSyncProbe();
        if (getIntent() != null && getIntent().getBooleanExtra("browseProbe", false)) runBrowseProbe();
        if (getIntent() != null && getIntent().getBooleanExtra("configProbe", false)) runConfigProbe();
        if (getIntent() != null && getIntent().getBooleanExtra("syncReal", false)) {
            runSyncRealProbe(getIntent().getStringExtra("user"), getIntent().getStringExtra("pass"));
        }
        if (getIntent() != null && getIntent().getBooleanExtra("wipeCollection", false)) runWipeCollection();
        if (getIntent() != null && getIntent().getBooleanExtra("avatarProbe", false)) runAvatarProbe();
        if (getIntent() != null && getIntent().getBooleanExtra("restoreBackup", false)) runRestoreBackup();
        if (getIntent() != null && getIntent().getBooleanExtra("assetProbe", false)) runAssetProbe();
        if (getIntent() != null && getIntent().getBooleanExtra("migrateLegacy", false)) runMigrateProbe(false);
        if (getIntent() != null && getIntent().getBooleanExtra("migrateLegacyApply", false)) runMigrateProbe(true);
        CrashHandler.install(this);
        Theme.apply(this, store);   // 主题必须在建界面之前套用
        android.util.Log.i("AnkiAssistant", "ACTIVE config = " + store.activeConfig().name
                + " id=" + store.activeConfig().id);
        // WebView 读不了 assets 里 1MB 以上的文件（MathJax 就超了），所以起个本机小服务器
        assetServer = new AssetServer(getAssets());
        int port = assetServer.start();
        android.util.Log.i("AnkiAssistant", "asset server port = " + port);
        applySystemBars();
        buildUi();
        show(current);
        maybeIntro();
        // 启动就测一次 Anki 连接，侧栏底部那盏灯直接反映真实状态
        if (rail != null) updateSyncLamp();
        // 一次性清掉旧头像缓存：以前存的是方形，改成圆形后要重新生成
        if (!store.avatarRoundMigrated()) {
            store.setAvatarRoundMigrated(true);
            try {
                java.io.File dir = new java.io.File(getFilesDir(), "avatars");
                java.io.File[] fs = dir.listFiles();
                if (fs != null) for (java.io.File file : fs) file.delete();
            } catch (Exception ignored) { }
        }
        loadAvatarAsync();   // 布局建好后统一加载一次（手机没有侧栏，必须放在这里）
    }

    /**
     * 调试用：`adb shell am start -n com.ankiassistant/.MainActivity --ez engineTest true`
     * 会在后台跑一遍内置引擎自检，结果写进 logcat（tag=AnkiAssistant），方便在真机上确认引擎可用。
     */
    private void runEngineSelfTest() {
        new Thread(new Runnable() {
            @Override public void run() {
                AnkiEngine e = null;
                try {
                    e = new AnkiEngine();
                    android.util.Log.i("AnkiAssistant", "ENGINE backend started, ptr=" + e.ptrValue());
                    e.openCollection(MainActivity.this);
                    android.util.Log.i("AnkiAssistant", "ENGINE collection at " + e.collectionPath());
                    java.util.Map<String, Long> decks = e.deckNames();
                    android.util.Log.i("AnkiAssistant", "ENGINE decks=" + decks.size() + " " + decks.keySet());
                    long did = e.ensureDeck(store.defaultDeck());
                    android.util.Log.i("AnkiAssistant", "ENGINE ensureDeck(" + store.defaultDeck() + ")=" + did);
                    long mid = e.ensureCardNotetype();
                    android.util.Log.i("AnkiAssistant", "ENGINE ensureCardNotetype=" + mid);
                    String[] flds = new String[CardFormat.FIELDS.length];
                    for (int i = 0; i < flds.length; i++) flds[i] = i == 0 ? "engine-selftest" : "v" + i;
                    long nid = e.addNote(did, mid, flds, new String[]{"ALevel::Maths"});
                    android.util.Log.i("AnkiAssistant", "ENGINE addNote ok noteId=" + nid);
                    android.util.Log.i("AnkiAssistant", "ENGINE SELFTEST PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "ENGINE SELFTEST FAIL: " + t, t);
                } finally {
                    if (e != null) e.close();
                }
            }
        }).start();
    }

    /**
     * 调试用：`--ez syncReal true --es user xxx --es pass yyy`
     * 逐 RPC 打印 AnkiWeb 同步过程，便于定位同步失败。**永不选择"上传"**，只会下载。
     * 凭据从 intent 读入，不写进代码。
     */
    private void runSyncRealProbe(final String user, final String pass) {
        new Thread(new Runnable() {
            @Override public void run() {
                AnkiEngine e;
                try {
                    e = EngineHolder.get(MainActivity.this);
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "SYNCREAL open engine fail: " + t, t);
                    return;
                }
                try {
                    e.setEndpoint(store.syncEndpoint());
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 login user=" + user);
                    String hkey = e.syncLogin(user, pass, null);
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step1 ok hkey.len=" + hkey.length());

                    int need = e.syncRequired(hkey);
                    android.util.Log.i("AnkiAssistant", "SYNCREAL step2 syncRequired=" + need
                            + (need == 0 ? " (NO_CHANGES)" : need == 1 ? " (NORMAL)" : " (FULL)"));

                    if (need == 2) {
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 先跑普通同步拿 serverUsn");
                        AnkiEngine.SyncInfo info = e.syncCollectionInfo(hkey, false);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 syncCollection required="
                                + info.required + " serverUsn=" + info.serverUsn
                                + " msg=" + info.serverMessage);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step4 需要全量同步 → 只下载，绝不上传");
                        e.fullUploadOrDownload(hkey, false, info.serverUsn);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step4 full download ok");
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step5 media sync…");
                        e.syncMedia(hkey);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step5 media ok");
                    } else if (need == 1) {
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 普通同步（media=false 先试）");
                        int after = e.syncCollection(hkey, false);
                        android.util.Log.i("AnkiAssistant", "SYNCREAL step3 syncCollection -> " + after);
                    }
                    store.setSyncEndpoint(e.endpoint());
                    android.util.Log.i("AnkiAssistant", "SYNCREAL endpoint=" + e.endpoint());
                    android.util.Log.i("AnkiAssistant", "SYNCREAL PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "SYNCREAL FAIL: " + t, t);
                } finally {
                    store.setLastSyncAt(System.currentTimeMillis());
                    Th.ui(new Runnable() {
                        @Override public void run() { updateSyncLamp(); }
                    });
                }
            }
        }).start();
    }

    /** 调试用：删掉本机收藏库（连带 media 目录），便于测试"首次同步" */
    private void runWipeCollection() {
        try {
            java.io.File dir = getFilesDir();
            String[] names = dir.list();
            int n = 0;
            if (names != null) {
                for (String name : names) {
                    if (name.startsWith("collection.anki2") || name.startsWith("collection.media")) {
                        if (new java.io.File(dir, name).delete()) n++;
                    }
                }
            }
            EngineHolder.close();
            android.util.Log.i("AnkiAssistant", "WIPE removed " + n + " collection files");
        } catch (Throwable t) {
            android.util.Log.e("AnkiAssistant", "WIPE FAIL: " + t, t);
        }
    }


    /** 调试用：`--ez avatarProbe true` 验证头像写入收藏库（媒体+配置）再读回 */
    private void runAvatarProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    String mail = store.ankiWebUser();
                    if (mail.length() == 0) mail = "probe@example.com";
                    android.graphics.Bitmap bmp = Avatar.letterBitmap(mail, 256);
                    boolean ok = Avatar.pushToCloud(MainActivity.this, mail, bmp);
                    android.util.Log.i("AnkiAssistant", "AVATAR push=" + ok
                            + " mediaName=" + Avatar.mediaName(mail));
                    java.io.File f = new java.io.File(Avatar.mediaDir(MainActivity.this),
                            Avatar.mediaName(mail));
                    android.util.Log.i("AnkiAssistant", "AVATAR mediaFile exists=" + f.exists()
                            + " size=" + (f.exists() ? f.length() : 0));
                    AnkiEngine e = EngineHolder.get(MainActivity.this);
                    String json = e.getConfigJson("ankiassistant.avatar."
                            + Avatar.mediaName(mail).replace("ankiassistant-avatar-", "").replace(".png", ""));
                    android.util.Log.i("AnkiAssistant", "AVATAR config=" + json);
                    Avatar.clear(MainActivity.this, mail);
                    android.graphics.Bitmap back = Avatar.pullFromCloud(MainActivity.this, mail, "custom");
                    android.util.Log.i("AnkiAssistant", "AVATAR pull=" + (back != null));
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "AVATAR PROBE FAIL: " + t, t);
                }
            }
        }).start();
    }

    /** 调试用：`--ez restoreBackup true` 用 collection.anki2.bak 覆盖当前收藏库（会先关掉引擎） */
    private void runRestoreBackup() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    EngineHolder.close();
                    Thread.sleep(800);
                    java.io.File dir = getFilesDir();
                    java.io.File bak = new java.io.File(dir, "collection.anki2.bak");
                    java.io.File cur = new java.io.File(dir, "collection.anki2");
                    if (!bak.exists()) {
                        android.util.Log.e("AnkiAssistant", "RESTORE 没有备份文件");
                        return;
                    }
                    for (String extra : new String[]{"collection.anki2-wal", "collection.anki2-shm"}) {
                        java.io.File f = new java.io.File(dir, extra);
                        if (f.exists() && f.delete()) {
                            android.util.Log.i("AnkiAssistant", "RESTORE 删除 " + extra);
                        }
                    }
                    java.io.FileInputStream in = new java.io.FileInputStream(bak);
                    java.io.FileOutputStream out = new java.io.FileOutputStream(cur);
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                    in.close();
                    out.close();
                    android.util.Log.i("AnkiAssistant", "RESTORE 完成，" + (cur.length() / 1024) + " KB");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "RESTORE FAIL: " + t, t);
                }
            }
        }).start();
    }

    /** 调试用：`--ez assetProbe true` 直接抓资源服务器上的 MathJax 脚本与字体，看能不能取到 */
    private void runAssetProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try { Thread.sleep(4000); } catch (InterruptedException ignored) { }
                String[] paths = {
                        "mathjax/tex-mml-chtml.js",
                        "mathjax/output/chtml/fonts/woff-v2/MathJax_Main-Regular.woff",
                        "mathjax/output/chtml/fonts/woff-v2/MathJax_Math-Italic.woff",
                        "mj-woff-MathJax_Main-Regular.woff",
                };
                String base = assetServerUrl();
                android.util.Log.i("AnkiAssistant", "ASSET base=" + base);
                for (String path : paths) {
                    try {
                        java.net.HttpURLConnection conn = (java.net.HttpURLConnection)
                                new java.net.URL(base + path).openConnection();
                        conn.setConnectTimeout(5000);
                        conn.setReadTimeout(8000);
                        int code = conn.getResponseCode();
                        int len = conn.getContentLength();
                        java.io.InputStream in = code == 200 ? conn.getInputStream() : conn.getErrorStream();
                        int n = 0;
                        if (in != null) {
                            byte[] buf = new byte[8192];
                            int r;
                            while ((r = in.read(buf)) > 0) n += r;
                            in.close();
                        }
                        android.util.Log.i("AnkiAssistant", "ASSET " + code + " " + path
                                + " len=" + len + " read=" + n);
                    } catch (Throwable t) {
                        android.util.Log.e("AnkiAssistant", "ASSET FAIL " + path + " : " + t);
                    }
                }
            }
        }).start();
    }

    /** 调试用：`--ez migrateLegacy true` 只统计；`--ez migrateLegacyApply true` 真的转换 */
    private void runMigrateProbe(final boolean apply) {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    NoteMigrator.Result r = NoteMigrator.convert(MainActivity.this, store, !apply);
                    android.util.Log.i("AnkiAssistant", "MIGRATE " + (apply ? "APPLY" : "DRYRUN")
                            + " scanned=" + r.scanned + " converted=" + r.converted
                            + " skipped=" + r.skipped + " failed=" + r.failed
                            + " | " + r.summary().replace("\n", " / "));
                    for (String f : r.failures) {
                        android.util.Log.w("AnkiAssistant", "MIGRATE fail " + f);
                    }
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "MIGRATE FAIL: " + t, t);
                }
            }
        }).start();
    }
    /** 调试用：`--ez configProbe true` 自建 config → 设为当前 → 用它写一张卡 */
    private void runConfigProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    CardConfig c = new CardConfig();
                    c.id = "cfgprobe";
                    c.name = "测试格式";
                    c.noteType = "测试格式";
                    c.prompt = "给 {word} 出一张词卡，学科 {subject}，只输出 JSON：\n"
                            + "term：词条\nmeaning：中文释义\nsentence：例句";
                    c.fields.add(new CardConfig.Field("词条", "", "", false));
                    c.fields.add(new CardConfig.Field("中文释义", "meaning", "中文", false));
                    c.fields.add(new CardConfig.Field("例句", "sentence", "英文例句", false));
                    store.saveConfig(c);
                    store.setActiveConfigId(c.id);
                    android.util.Log.i("AnkiAssistant", "CONFIG active = " + store.activeConfig().name
                            + " fields=" + java.util.Arrays.toString(store.activeConfig().fieldNames()));

                    org.json.JSONObject note = new org.json.JSONObject();
                    note.put("词条", "probe-word");
                    note.put("中文释义", "探针释义");
                    note.put("例句", "This is a probe sentence.");
                    String msg = AnkiBackend.save(MainActivity.this, store, "测试牌组", note,
                            new String[]{"Probe"});
                    android.util.Log.i("AnkiAssistant", "CONFIG save -> " + msg);
                    android.util.Log.i("AnkiAssistant", "CONFIG SELFTEST PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "CONFIG SELFTEST FAIL: " + t, t);
                }
            }
        }).start();
    }

    /** 调试用：`--ez browseProbe true` 走一遍 AnkiBackend 的搜索，验证浏览也由内置引擎提供 */
    private void runBrowseProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    android.util.Log.i("AnkiAssistant", "BROWSE backend = "
                            + AnkiBackend.describe(MainActivity.this, store));
                    String[] decks = AnkiBackend.deckNames(MainActivity.this, store);
                    android.util.Log.i("AnkiAssistant", "BROWSE decks = "
                            + java.util.Arrays.toString(decks));
                    int total = AnkiBackend.searchTotal(MainActivity.this, store, "deck:*");
                    org.json.JSONArray notes = AnkiBackend.searchNotes(MainActivity.this, store, "deck:*", 5);
                    android.util.Log.i("AnkiAssistant", "BROWSE total=" + total
                            + " returned=" + notes.length());
                    if (notes.length() > 0) {
                        org.json.JSONObject n = notes.optJSONObject(0);
                        android.util.Log.i("AnkiAssistant", "BROWSE first=" + n.toString());
                    }
                    // 直接问引擎要一遍，便于定位字段名/数量问题
                    AnkiEngine e = EngineHolder.get(MainActivity.this);
                    long[] ids = e.searchIds("deck:*", 10);
                    android.util.Log.i("AnkiAssistant", "BROWSE raw ids=" + java.util.Arrays.toString(ids));
                    if (ids.length > 0) {
                        long mid = e.noteNotetypeId(ids[0]);
                        android.util.Log.i("AnkiAssistant", "BROWSE notetypeId=" + mid
                                + " fieldNames=" + java.util.Arrays.toString(e.fieldNames(mid)));
                    }
                    android.util.Log.i("AnkiAssistant", "BROWSE SELFTEST PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "BROWSE SELFTEST FAIL: " + t, t);
                }
            }
        }).start();
    }

    /**
     * 调试用：`--ez syncProbe true` 用一个假账号试一次 AnkiWeb 登录，
     * 验证「网络 + 同步协议 + 错误处理」这条链路是通的（预期得到认证失败，而不是崩溃或超时）。
     */
    private void runSyncProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    AnkiEngine e = EngineHolder.get(MainActivity.this);
                    android.util.Log.i("AnkiAssistant", "SYNC probe: 尝试用假账号登录 AnkiWeb…");
                    String hkey = e.syncLogin("anki-assistant-selftest@example.invalid", "wrong-password", null);
                    android.util.Log.i("AnkiAssistant", "SYNC probe: 竟然登录成功了？hkey=" + hkey);
                } catch (Throwable t) {
                    // 认证失败是预期结果 —— 说明网络与协议都通了
                    android.util.Log.i("AnkiAssistant", "SYNC probe: 如期失败 -> " + t.getMessage());
                    android.util.Log.i("AnkiAssistant", "SYNC SELFTEST PASS (error path verified)");
                }
            }
        }).start();
    }
    /** 调试用：`--ez backendSave true` 会走一遍 AnkiBackend.save，并把选中的后端与结果写进 logcat */
    private void runBackendSaveTest() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    android.util.Log.i("AnkiAssistant", "BACKEND chosen = "
                            + AnkiBackend.describe(MainActivity.this, store));
                    org.json.JSONObject note = new org.json.JSONObject();
                    for (String f : CardFormat.FIELDS) note.put(f, "backend-test:" + f);
                    note.put("单词", "backend-selftest");
                    String msg = AnkiBackend.save(MainActivity.this, store, store.defaultDeck(),
                            note, new String[]{"ALevel::Maths"});
                    android.util.Log.i("AnkiAssistant", "BACKEND save -> " + msg);
                    android.util.Log.i("AnkiAssistant", "BACKEND SELFTEST PASS");
                } catch (Throwable t) {
                    android.util.Log.e("AnkiAssistant", "BACKEND SELFTEST FAIL: " + t, t);
                }
            }
        }).start();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == AnkiDroidClient.PERM_REQUEST) {

            if (rail != null) updateSyncLamp();
        // 一次性清掉旧头像缓存：以前存的是方形，改成圆形后要重新生成
        if (!store.avatarRoundMigrated()) {
            store.setAvatarRoundMigrated(true);
            try {
                java.io.File dir = new java.io.File(getFilesDir(), "avatars");
                java.io.File[] fs = dir.listFiles();
                if (fs != null) for (java.io.File file : fs) file.delete();
            } catch (Exception ignored) { }
        }
        loadAvatarAsync();   // 布局建好后统一加载一次（手机没有侧栏，必须放在这里）
            if (grantResults != null && grantResults.length > 0
                    && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                android.widget.Toast.makeText(this, "已授权：本机 AnkiDroid 兜底可用",
                        android.widget.Toast.LENGTH_LONG).show();
            } else {
                android.widget.Toast.makeText(this, "没有授权，本机 AnkiDroid 兜底不可用",
                        android.widget.Toast.LENGTH_LONG).show();
            }
        }
    }
    /** 编辑器页面地址：优先走本机 http，失败则退回 file:// */
    public String editorUrl() {
        String base = assetServerUrl();
        return base != null ? base + "editor.html" : "file:///android_asset/editor.html";
    }

    /** 本机资源服务器的基地址（浏览页渲染字段时也要用 MathJax） */
    public String assetBaseUrl() { return assetServerUrl(); }

    /** 浏览页详情：把动态 HTML 交给本机资源服务器输出（避免匿名来源导致字体/公式加载失败） */
    public void setNoteHtml(String html) {
        if (assetServer != null) assetServer.setNoteHtml(html);
    }

    /** 让子视图拿到设置 */
    public Store store() { return store; }

    private String assetServerUrl() {
        if (assetServer == null) return null;
        int port = assetServer.port();
        if (port <= 0) return null;
        return "http://127.0.0.1:" + port + "/";
    }

    @Override
    protected void onDestroy() {
        if (assetServer != null) assetServer.stop();
        super.onDestroy();
    }

    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        buildUi();
        show(current);
    }

    /** 状态栏/导航栏配色（浅色背景 + 深色图标） */
    private void applySystemBars() {
        if (Build.VERSION.SDK_INT >= 21) {
            getWindow().setStatusBarColor(Ui.BG);
            getWindow().setNavigationBarColor(Ui.BG);
        }
        if (Build.VERSION.SDK_INT >= 23) {
            View dec = getWindow().getDecorView();
            dec.setSystemUiVisibility(dec.getSystemUiVisibility() | View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR
                    | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        }
    }

    // ------------------------------------------------------------------ 布局

    private void buildUi() {
        DisplayMetrics dm = getResources().getDisplayMetrics();
        tablet = Math.min(dm.widthPixels, dm.heightPixels) / dm.density >= 600f;

        if (createView == null) {
            createView = new CreateView(this);
            browseView = new BrowseView(this);
            settingsView = new SettingsView(this);
        }

        root = new LinearLayout(this);
        root.setOrientation(tablet ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        root.setBackgroundColor(Ui.BG);
        setContentView(root);

        // ---- 左侧选择栏（仅平板）----
        if (tablet) {
            rail = buildRail();
            root.addView(rail, new LinearLayout.LayoutParams(
                    Ui.dp(RAIL_W_DP), ViewGroup.LayoutParams.MATCH_PARENT));
        } else {
            rail = null;
        }

        // ---- 右/下侧：顶栏 + 内容（顶栏跟着内容走，标题才会和卡片左边缘对齐）----
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        if (tablet) {
            root.addView(column, new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        } else {
            root.addView(column, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        }

        topBar = new LinearLayout(this);
        topBar.setOrientation(LinearLayout.HORIZONTAL);
        // 顶栏不再是"纯白一片"：淡渐变 + 阴影 + 底部一条淡蓝渐变线
        GradientDrawable barBg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[]{Ui.CARD, Ui.PANEL});
        topBar.setBackground(barBg);
        // 不要阴影：手机上那层灰边看着很脏
        // 左右留白与各页面内容的 12dp 对齐，标题才会和下面的卡片左边缘齐平
        topBar.setPadding(Ui.dp(12), 0, Ui.dp(12), 0);
        topBar.setGravity(android.view.Gravity.CENTER_VERTICAL);
        column.addView(topBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(isPhone() ? 48 : 56)));

        TextView title = new TextView(this);
        Ui.title(title, "Anki 助手");
        title.setTextSize(isPhone() ? 16.5f : 18);
        // 高度是撑满的，必须显式垂直居中，否则文字会贴在顶栏上沿
        title.setGravity(android.view.Gravity.CENTER_VERTICAL);
        topBar.addView(title, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));

        // 手机布局没有侧栏，顶栏也给一个账号入口
        if (isPhone()) {
            // 顶栏也用真实头像（和侧栏、账号弹窗一致），没有头像时才显示字母底
            topAvatar = new android.widget.ImageView(this);
            topAvatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
            topAvatar.setBackground(Ui.round(0x00000000, 13));
            topAvatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAccountDialog(); }
            });
            LinearLayout.LayoutParams avlp = new LinearLayout.LayoutParams(Ui.dp(26), Ui.dp(26));
            avlp.rightMargin = Ui.dp(10);
            avlp.gravity = android.view.Gravity.CENTER_VERTICAL;
            topBar.addView(topAvatar, avlp);
        }

        TextView ver = new TextView(this);
        ver.setText(Version.VERSION_TAG);
        ver.setTextColor(Ui.TEXT_DIM);
        ver.setTextSize(11);
        ver.setGravity(android.view.Gravity.CENTER);
        ver.setBackground(Ui.round(Ui.PANEL, 8));
        ver.setPadding(Ui.dp(7), Ui.dp(2), Ui.dp(7), Ui.dp(2));
        topBar.addView(ver, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        View accentLine = new View(this);
        accentLine.setBackground(new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,
                new int[]{Ui.alpha(Ui.ACCENT, 0x66), Ui.alpha(Ui.ACCENT, 0x00)}));
        column.addView(accentLine, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(2)));

        // ---- 内容区 ----
        content = new FrameLayout(this);
        column.addView(content, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        contentPad = -1;
        // 超宽屏限制最大宽度并居中；同时把顶栏按同样内缩，标题才会跟卡片左边缘对齐
        content.addOnLayoutChangeListener(new View.OnLayoutChangeListener() {
            @Override
            public void onLayoutChange(View v, int left, int top, int right, int bottom,
                                       int oldLeft, int oldTop, int oldRight, int oldBottom) {
                int w = right - left;
                int max = Ui.dp(MAX_CONTENT_DP);
                int pad = w > max ? (w - max) / 2 : 0;
                int min = Ui.dp(16);      // 再窄也留点边距，别贴着侧栏
                if (pad < min) pad = min;
                if (pad == contentPad) return;
                contentPad = pad;
                v.setPadding(pad, v.getPaddingTop(), pad, v.getPaddingBottom());
                if (topBar != null) {
                    topBar.setPadding(pad + Ui.dp(12), 0, pad + Ui.dp(12), 0);
                }
            }
        });

        if (!tablet) {
            bottomBar = buildBottomBar();
            column.addView(bottomBar, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(60)));
        } else {
            bottomBar = null;
        }
        built = true;
    }

    /** 内容最大宽度（dp）。平板横屏下再宽就不好看了 */
    private static final int MAX_CONTENT_DP = 1020;
    /** 内容区当前的水平内缩，用于让顶栏标题跟着对齐 */
    private int contentPad = -1;

    /** 左侧选择栏宽度（平板常驻，不再有收起功能） */
    private static final int RAIL_W_DP = 100;

    private LinearLayout buildRail() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.VERTICAL);
        // 淡色渐变底（不再是死白一整条），右上角由 accentLine 收边
        r.setBackground(new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,
                new int[]{Ui.CARD, Ui.PANEL}));
        r.setPadding(Ui.dp(8), Ui.dp(16), Ui.dp(8), Ui.dp(14));

        // ---- 顶部：Anki 账号按钮（图标是应用标识；点开登录/注册/同步） ----
        LinearLayout accountBtn = new LinearLayout(this);
        accountBtn.setOrientation(LinearLayout.VERTICAL);
        accountBtn.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
        accountBtn.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        accountBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { showAccountDialog(); }
        });
        accountAvatar = new android.widget.ImageView(this);
        accountAvatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
        accountAvatar.setBackground(Ui.round(0x00000000, 18));   // 头像是圆的，底下不要垫颜色
        accountAvatar.setClipToOutline(true);
        android.graphics.drawable.GradientDrawable clip = Ui.round(Ui.ACCENT, 11);
        accountAvatar.setBackground(clip);
        accountBtn.addView(accountAvatar, new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36)));
        accountLabel = new TextView(this);
        accountLabel.setText("登录");
        accountLabel.setTextColor(Ui.SUB);
        accountLabel.setTextSize(10.5f);
        accountLabel.setGravity(android.view.Gravity.CENTER);
        LinearLayout.LayoutParams alp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        alp.topMargin = Ui.dp(4);
        accountBtn.addView(accountLabel, alp);
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        llp.gravity = android.view.Gravity.CENTER_HORIZONTAL;
        llp.bottomMargin = Ui.dp(4);
        r.addView(accountBtn, llp);

        View topSpace = new View(this);
        r.addView(topSpace, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        String[] labels = {"制卡", "浏览", "设置"};
        int[] icons = {IconDrawable.ADD, IconDrawable.CARDS, IconDrawable.SLIDERS};
        for (int i = 0; i < 3; i++) {
            final int idx = i;
            NavItem item = new NavItem(this, false, labels[i], icons[i]);
            item.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { show(idx); }
            });
            railItems[i] = item;
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(54));
            lp.bottomMargin = Ui.dp(10);
            r.addView(item, lp);
        }

        View bottomSpace = new View(this);
        r.addView(bottomSpace, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        // ---- 底部：Anki 连接状态灯（点一下重新检测，填掉下方空白） ----
        LinearLayout lamp = new LinearLayout(this);
        lamp.setOrientation(LinearLayout.VERTICAL);
        lamp.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
        lamp.setPadding(0, 0, 0, Ui.dp(2));
        lamp.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { onLampClick(); }
        });

        ankiDot = new View(this);
        ankiDot.setBackground(Ui.round(Ui.TEXT_DIM, 5));
        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(Ui.dp(10), Ui.dp(10));
        dlp.gravity = android.view.Gravity.CENTER_HORIZONTAL;
        dlp.bottomMargin = Ui.dp(5);
        lamp.addView(ankiDot, dlp);

        ankiLampText = new TextView(this);
        ankiLampText.setText("Anki");
        ankiLampText.setTextSize(11);
        ankiLampText.setTextColor(Ui.TEXT_DIM);
        ankiLampText.setGravity(android.view.Gravity.CENTER);
        lamp.addView(ankiLampText, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        r.addView(lamp, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return r;
    }

    // ------------------------------------------------------------------ Anki 账号（登录 / 注册 / 同步）

    private boolean isPhone() {
        return getResources().getConfiguration().smallestScreenWidthDp < 600;
    }

    /** 左上角头像：Anki 账号弹窗（未登录=登录/注册；已登录=账号信息 + 立即同步 + 登出） */
    public void showAccountDialog() {
        final boolean loggedIn = store.ankiWebHkey().length() > 0;

        // ---------------- 已登录：只显示账号身份与登出，不再出现密码框 ----------------
        if (loggedIn) {
            LinearLayout box = new LinearLayout(this);
            box.setOrientation(LinearLayout.VERTICAL);

            LinearLayout card = new LinearLayout(this);
            card.setOrientation(LinearLayout.HORIZONTAL);
            card.setGravity(android.view.Gravity.CENTER_VERTICAL);
            card.setBackground(Ui.round(Ui.PANEL, 16));
            card.setPadding(Ui.dp(14), Ui.dp(14), Ui.dp(14), Ui.dp(14));
            final String mail = store.ankiWebUser();
            final android.widget.ImageView avatar = new android.widget.ImageView(this);
            avatar.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
            avatar.setBackground(Ui.round(0x00000000, 23));
            avatar.setClipToOutline(true);
            android.graphics.Bitmap av = Avatar.cached(this, mail, store.avatarStyle(mail));
            if (av != null) avatar.setImageBitmap(av);
            avatar.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { showAvatarPicker(); }
            });
            card.addView(avatar, new LinearLayout.LayoutParams(Ui.dp(46), Ui.dp(46)));

            LinearLayout info = new LinearLayout(this);
            info.setOrientation(LinearLayout.VERTICAL);
            info.setPadding(Ui.dp(12), 0, 0, 0);
            TextView name = new TextView(this);
            name.setText(mail.length() > 0 ? mail : "已登录 AnkiWeb");
            name.setTextColor(Ui.INK);
            name.setTextSize(15);
            name.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
            info.addView(name);
            TextView sub = new TextView(this);
            sub.setText("AnkiWeb 账号"
                    + (store.syncEndpoint().length() > 0 ? "　·　" + hostOf(store.syncEndpoint()) : ""));
            sub.setTextColor(Ui.TEXT_DIM);
            sub.setTextSize(12);
            sub.setPadding(0, Ui.dp(3), 0, 0);
            info.addView(sub);
            card.addView(info, new LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            box.addView(card);

            TextView state = new TextView(this);
            state.setText(AnkiSync.describe(store));
            state.setTextColor(Ui.SUB);
            state.setTextSize(12.5f);
            state.setLineSpacing(Ui.dp(3), 1f);
            state.setPadding(Ui.dp(2), Ui.dp(12), 0, 0);
            box.addView(state);

            new DialogUi.Builder(this)
                    .title("Anki 账号")
                    .content(box)
                    .neutral("换头像", new Runnable() {
                        @Override public void run() { showAvatarPicker(); }
                    })
                    .negative("登出", new Runnable() {
                        @Override public void run() { confirmLogout(); }
                    })
                    .positive("立即同步", new Runnable() {
                        @Override public void run() { syncWithAccount(null, null); }
                    })
                    .show();
            return;
        }

        // ---------------- 未登录：邮箱 + 密码 + 注册 ----------------
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);

        TextView tip = new TextView(this);
        tip.setText("登录后卡片会同步到 AnkiWeb。密码只用于登录，不会保存在设备上。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12.5f);
        tip.setLineSpacing(Ui.dp(3), 1f);
        box.addView(tip);

        final android.widget.EditText user = new android.widget.EditText(this);
        user.setHint("AnkiWeb 邮箱");
        user.setText(store.ankiWebUser());
        user.setSingleLine(true);
        Ui.field(user);
        box.addView(DialogUi.label(this, "邮箱"));
        box.addView(DialogUi.inputWrap(this, user));

        final android.widget.EditText pass = new android.widget.EditText(this);
        pass.setHint("密码");
        pass.setSingleLine(true);
        pass.setInputType(android.text.InputType.TYPE_CLASS_TEXT
                | android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);
        Ui.field(pass);
        box.addView(DialogUi.label(this, "密码"));
        box.addView(DialogUi.inputWrap(this, pass));

        new DialogUi.Builder(this)
                .title("登录 AnkiWeb")
                .content(box)
                .neutral("注册账号", new Runnable() {
                    @Override public void run() { openAnkiWebRegister(); }
                })
                .negative("取消", null)
                .positive("登录并同步", new Runnable() {
                    @Override public void run() {
                        syncWithAccount(user.getText().toString().trim(),
                                pass.getText().toString());
                    }
                })
                .show();
    }


    // ------------------------------------------------------------------ 头像

    /** 侧栏按钮上的头像：先显示缓存，再后台联网取一次 */
    private void loadAvatarAsync() {
        final String mail = store.ankiWebUser();
        final String style = store.avatarStyle(mail);
        android.graphics.Bitmap cached = Avatar.cached(this, mail, style);
        if (cached != null) {
            if (accountAvatar != null) accountAvatar.setImageBitmap(cached);
            if (topAvatar != null) topAvatar.setImageBitmap(cached);
        }
        Th.bg(new Runnable() {
            @Override public void run() {
                // 先看收藏库：这是"另一台设备上传的那张"，优先级高于本地缓存与 Gravatar
                android.graphics.Bitmap fromCloud =
                        Avatar.pullFromCloud(MainActivity.this, mail, "custom");
                android.graphics.Bitmap loaded = (fromCloud != null)
                        ? fromCloud
                        : Avatar.load(MainActivity.this, mail, style);
                if (loaded == null) return;
                final android.graphics.Bitmap b = loaded;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                        if (topAvatar != null) topAvatar.setImageBitmap(b);
                    }
                });
            }
        });
    }

    /** 换头像：选一个风格（邮箱相同则各设备一致） */
    private void showAvatarPicker() {
        final String mail = store.ankiWebUser();
        if (mail.length() == 0) {
            android.widget.Toast.makeText(this, "登录后才能设置头像", android.widget.Toast.LENGTH_SHORT).show();
            return;
        }
        String[] names = new String[Avatar.STYLES.length];
        int[] colors = new int[Avatar.STYLES.length];
        int checked = 0;
        String cur = store.avatarStyle(mail);
        for (int i = 0; i < Avatar.STYLES.length; i++) {
            names[i] = Avatar.STYLES[i][1];
            colors[i] = Ui.ACCENT;
            if (Avatar.STYLES[i][0].equals(cur)) checked = i;
        }
        new DialogUi.Builder(this)
                .title("头像")
                .message("默认用 Gravatar（按邮箱取，换设备也是同一张）；"
                        + "也可以从相册选一张，裁好后会写进收藏库跟着同步到别的设备。")
                .choiceColors(colors)
                .choices(names, checked, new DialogUi.Picker() {
                    @Override public void onPick(int which) {
                        String style = Avatar.STYLES[which][0];
                        if ("custom".equals(style)) { pickAvatarImage(); return; }
                        if ("url".equals(style)) { askAvatarUrl(); return; }
                        store.setAvatarStyle(mail, style);
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                        android.widget.Toast.makeText(MainActivity.this,
                                "头像已切换为「" + Avatar.STYLES[which][1] + "」",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                })
                .neutral("重新获取", new Runnable() {
                    @Override public void run() {
                        Avatar.clear(MainActivity.this, mail);
                        loadAvatarAsync();
                    }
                })
                .negative("关闭", null)
                .show();
    }


    private static final int REQ_AVATAR = 0x9A71;

    /** 从相册/文件里选一张图当头像 */
    private void pickAvatarImage() {
        try {
            android.content.Intent i = new android.content.Intent(
                    android.content.Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(android.content.Intent.CATEGORY_OPENABLE);
            i.setType("image/*");
            startActivityForResult(i, REQ_AVATAR);
        } catch (Exception e) {
            android.widget.Toast.makeText(this, "打不开图片选择器：" + e.getMessage(),
                    android.widget.Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onActivityResult(int req, int result, android.content.Intent data) {
        super.onActivityResult(req, result, data);
        if (req != REQ_AVATAR || result != RESULT_OK || data == null || data.getData() == null) return;
        final android.net.Uri uri = data.getData();
        long sizeBytes = -1;
        try {
            android.database.Cursor cur = getContentResolver().query(uri, null, null, null, null);
            if (cur != null) {
                int idx = cur.getColumnIndex(android.provider.OpenableColumns.SIZE);
                if (idx >= 0 && cur.moveToFirst()) sizeBytes = cur.getLong(idx);
                cur.close();
            }
        } catch (Exception ignored) { }
        // 上限 10MB；超了也不直接拒绝——反正会压缩到 256px，这里只是提示一下
        final boolean big = sizeBytes > 10L * 1024 * 1024;
        final String mail = store.ankiWebUser();
        android.widget.Toast.makeText(this, "正在处理图片…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                final android.graphics.Bitmap src = decodeSampled(uri, 1600);
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (src == null) {
                            android.widget.Toast.makeText(MainActivity.this, "这张图读不出来，换一张试试",
                                    android.widget.Toast.LENGTH_LONG).show();
                            return;
                        }
                        showCropper(mail, src);
                    }
                });
            }
        });
    }

    /** 按显示需要解码（先量尺寸再采样，避免大图 OOM；最终头像固定压到 256px，通常不到 50KB） */
    private android.graphics.Bitmap decodeSampled(android.net.Uri uri, int max) {
        try {
            android.graphics.BitmapFactory.Options o = new android.graphics.BitmapFactory.Options();
            o.inJustDecodeBounds = true;
            java.io.InputStream in1 = getContentResolver().openInputStream(uri);
            android.graphics.BitmapFactory.decodeStream(in1, null, o);
            if (in1 != null) in1.close();
            int sample = 1;
            while (o.outWidth / sample > max || o.outHeight / sample > max) sample *= 2;
            android.graphics.BitmapFactory.Options o2 = new android.graphics.BitmapFactory.Options();
            o2.inSampleSize = sample;
            java.io.InputStream in2 = getContentResolver().openInputStream(uri);
            android.graphics.Bitmap b = android.graphics.BitmapFactory.decodeStream(in2, null, o2);
            if (in2 != null) in2.close();
            return b;
        } catch (Throwable t) {
            return null;
        }
    }


    /** 裁剪弹窗：拖动图片、双指缩放，框内就是头像 */
    private void showCropper(final String mail, final android.graphics.Bitmap src) {
        final CropView crop = new CropView(this, src);
        int h = (int) (getResources().getDisplayMetrics().heightPixels * 0.46f);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        TextView tip = new TextView(this);
        tip.setText("拖动调整位置，双指缩放。方框内的部分会成为头像。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12.5f);
        tip.setPadding(0, 0, 0, Ui.dp(8));
        box.addView(tip);
        box.addView(crop, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, h));

        new DialogUi.Builder(this)
                .title("裁剪头像")
                .content(box)
                .negative("取消", null)
                .positive("就用这张", new Runnable() {
                    @Override public void run() {
                        try {
                            android.graphics.Bitmap cut = crop.cropped(256);
                            android.graphics.Bitmap saved = Avatar.saveCustom(MainActivity.this, mail, cut);
                            store.setAvatarStyle(mail, "custom");
                            if (saved != null) {
                                // 写进收藏库，随同步带到别的设备
                                Avatar.pushToCloud(MainActivity.this, mail, saved);
                            }
                            loadAvatarAsync();
                            android.widget.Toast.makeText(MainActivity.this, "头像已更新",
                                    android.widget.Toast.LENGTH_SHORT).show();
                        } catch (Throwable t) {
                            android.widget.Toast.makeText(MainActivity.this,
                                    "裁剪失败：" + t.getMessage(),
                                    android.widget.Toast.LENGTH_LONG).show();
                        }
                    }
                })
                .show();
    }

    /** 让用户填一个图片网址（放到 GitHub 仓库就能多设备共用） */
    private void askAvatarUrl() {
        final String mail = store.ankiWebUser();
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        TextView tip = new TextView(this);
        tip.setText("填一个能直接打开图片的网址（GitHub raw 链接也行）。"
                + "下载后会缓存在本机，之后离线也能显示。");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12.5f);
        tip.setLineSpacing(Ui.dp(3), 1f);
        box.addView(tip);
        final android.widget.EditText input = new android.widget.EditText(this);
        input.setSingleLine(true);
        input.setHint("https://raw.githubusercontent.com/…/avatar.png");
        input.setText(store.avatarUrl(mail));
        Ui.field(input);
        box.addView(DialogUi.label(this, "图片网址"));
        box.addView(DialogUi.inputWrap(this, input));

        new DialogUi.Builder(this)
                .title("从网址设置头像")
                .content(box)
                .negative("取消", null)
                .positive("下载", new Runnable() {
                    @Override public void run() {
                        final String url = input.getText().toString().trim();
                        if (url.length() == 0) return;
                        store.setAvatarUrl(mail, url);
                        store.setAvatarStyle(mail, "url");
                        Avatar.clear(MainActivity.this, mail);
                        android.widget.Toast.makeText(MainActivity.this, "正在下载…",
                                android.widget.Toast.LENGTH_SHORT).show();
                        Th.bg(new Runnable() {
                            @Override public void run() {
                                final android.graphics.Bitmap b =
                                        Avatar.downloadInto(MainActivity.this, mail, "url", url);
                                Th.ui(new Runnable() {
                                    @Override public void run() {
                                        if (b == null) {
                                            android.widget.Toast.makeText(MainActivity.this,
                                                    "下载失败，检查网址或网络", android.widget.Toast.LENGTH_LONG).show();
                                        } else {
                                            loadAvatarAsync();
                                            android.widget.Toast.makeText(MainActivity.this, "头像已更新",
                                                    android.widget.Toast.LENGTH_SHORT).show();
                                        }
                                    }
                                });
                            }
                        });
                    }
                })
                .show();
    }

    /** 从同步端点里取出主机名，给账号卡做副标题用 */
    private static String hostOf(String url) {
        try {
            java.net.URL u = new java.net.URL(url);
            return u.getHost();
        } catch (Exception e) {
            return url;
        }
    }

    /** 登出前确认 */
    private void confirmLogout() {
        new DialogUi.Builder(this)
                .title("登出 AnkiWeb")
                .message("登出后本机卡片仍在，只是不再与云端同步；设备上保存的登录密钥会被删除。")
                .negative("取消", null)
                .positive("登出", new Runnable() {
                    @Override public void run() {
                        AnkiSync.logout(store);
                        updateSyncLamp();
                        android.widget.Toast.makeText(MainActivity.this, "已登出 AnkiWeb",
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                })
                .show();
    }

    /** 用 AnkiWeb 官网的注册页（后端 API 不提供注册） */
    private void openAnkiWebRegister() {
        try {
            startActivity(new android.content.Intent(android.content.Intent.ACTION_VIEW,
                    android.net.Uri.parse("https://ankiweb.net/account/register")));
        } catch (Exception e) {
            android.widget.Toast.makeText(this, "打不开浏览器：" + e.getMessage(),
                    android.widget.Toast.LENGTH_LONG).show();
        }
    }

    /** 设置页 / 其它页面的「立即同步」入口（未登录会先弹登录） */
    public void syncNow() { onLampClick(); }

    /** 换皮肤后重建界面（主题是在建视图时读的，所以必须重建） */
    public void onThemeChanged() { recreate(); }

    /** 已有 hkey 时点灯同步；没有就弹登录 */
    private void onLampClick() {
        if (store.ankiWebHkey().length() == 0) {
            showAccountDialog();
        } else {
            syncWithAccount(null, null);
        }
    }

    private void syncWithAccount(final String user, final String pass) {
        android.widget.Toast.makeText(this, "正在同步…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, null);
                    toastUi(out.message);
                    updateSyncLamp();
                    loadAvatarAsync();   // 另一端刚上传的头像，同步完就能显示
                } catch (final AnkiSync.FullSyncRequired f) {
                    Th.ui(new Runnable() {
                        @Override public void run() { askFullSync(user, pass, f.reason); }
                    });
                } catch (final Exception e) {
                    toastUi("同步失败：" + e.getMessage());
                    updateSyncLamp();
                }
            }
        });
    }

    /** 需要全量同步时让用户选方向 */
    private void askFullSync(final String user, final String pass, String reason) {
        new DialogUi.Builder(this)
                .title("需要全量同步")
                .message(reason + "\n\n上传：用本机的卡片覆盖云端\n下载：用云端覆盖本机"
                        + "\n\n本机刚装好、云端才有卡片时，选「下载云端」。")
                .negative("取消", null)
                .neutral("上传本机", new Runnable() {
                    @Override public void run() { runFullSync(user, pass, Boolean.TRUE); }
                })
                .positive("下载云端", new Runnable() {
                    @Override public void run() { runFullSync(user, pass, Boolean.FALSE); }
                })
                .show();
    }

    private void runFullSync(final String user, final String pass, final Boolean upload) {
        android.widget.Toast.makeText(this, upload.booleanValue() ? "正在上传本机收藏库…"
                : "正在下载云端收藏库…", android.widget.Toast.LENGTH_SHORT).show();
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    AnkiSync.Outcome out = AnkiSync.sync(MainActivity.this, store, user, pass, upload);
                    toastUi(out.message);
                    loadAvatarAsync();
                } catch (final Exception e) {
                    toastUi("全量同步失败：" + e.getMessage());
                }
                Th.ui(new Runnable() {
                    @Override public void run() { updateSyncLamp(); }
                });
            }
        });
    }

    private void toastUi(final String msg) {
        Th.ui(new Runnable() {
            @Override public void run() {
                android.widget.Toast.makeText(MainActivity.this, msg,
                        android.widget.Toast.LENGTH_LONG).show();
            }
        });
    }

    /** 左下角指示灯：反映 AnkiWeb 登录/同步状态（顺带更新头像下面的小字） */
    public void updateSyncLamp() {
        boolean loggedIn = store.ankiWebHkey().length() > 0;
        long last = store.lastSyncAt();
        if (accountLabel != null) {
            accountLabel.setText(loggedIn ? "已登录" : "登录");
        }
        if (ankiDot == null) return;
        if (!loggedIn) {
            setSyncLamp(Ui.TEXT_DIM, "未登录");
        } else if (last <= 0) {
            setSyncLamp(Ui.AMBER, "待同步");
        } else {
            long min = (System.currentTimeMillis() - last) / 60000L;
            String when = min < 1 ? "刚刚" : (min < 60 ? (min + " 分钟前")
                    : (min / 60 + " 小时前"));
            setSyncLamp(Ui.GREEN, when);
        }
    }

    private void setSyncLamp(int color, String label) {
        if (ankiDot != null) ankiDot.setBackground(Ui.round(color, 5));
        if (ankiLampText != null) {
            ankiLampText.setText(label);
            ankiLampText.setTextColor(color == Ui.GREEN ? color : Ui.TEXT_DIM);
        }
    }

    private void setAnkiLamp(int color, String label) {
        if (ankiDot != null) ankiDot.setBackground(Ui.round(color, 5));
        if (ankiLampText != null) {
            ankiLampText.setText(label);
            ankiLampText.setTextColor(color == Ui.GREEN ? color : Ui.TEXT_DIM);
        }
    }

    private LinearLayout buildBottomBar() {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        bar.setBackground(Ui.roundStroke(Ui.CARD, Ui.LINE, 0));
        String[] labels = {"制卡", "浏览", "设置"};
        int[] icons = {IconDrawable.ADD, IconDrawable.CARDS, IconDrawable.SLIDERS};
        for (int i = 0; i < 3; i++) {
            final int idx = i;
            NavItem item = new NavItem(this, true, labels[i], icons[i]);
            item.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { show(idx); }
            });
            barItems[i] = item;
            bar.addView(item, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        }
        return bar;
    }

    // ------------------------------------------------------------------ 切页

    public void show(int index) {
        if (content == null) return;
        current = index;
        View v = index == TAB_BROWSE ? browseView
                : index == TAB_SETTINGS ? settingsView : createView;
        if (v.getParent() instanceof ViewGroup) {
            ((ViewGroup) v.getParent()).removeView(v);
        }
        content.removeAllViews();
        content.addView(v, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        for (int i = 0; i < 3; i++) {
            boolean on = (i == index);
            if (railItems[i] != null) railItems[i].setActive(on);
            if (barItems[i] != null) barItems[i].setActive(on);
        }
        if (index == TAB_CREATE) createView.onShown();
        if (index == TAB_BROWSE) browseView.onShown();
        if (index == TAB_SETTINGS) settingsView.onShown();
    }

    @Override
    public void onBackPressed() {
        // 不退出应用：任何页面按返回都先回到制卡页（防误触丢卡片）
        if (current != TAB_CREATE) show(TAB_CREATE);
    }

    // ------------------------------------------------------------------ 首次引导

    /** 设置页切换/编辑 config 之后，让制卡页按新字段重建输入框 */
    public void onCardConfigChanged() {
        if (createView != null) createView.refreshConfig();
    }

    private void maybeIntro() {
        if (store.introShown()) return;
        new DialogUi.Builder(this)
                .title("三步开始使用")
                .message("1. 点左上角头像登录 AnkiWeb（云端已有卡片就选「下载云端」）\n\n"
                        + "2. 制卡页输入单词 → 点「AI 填充」→ 逐项检查后「保存到 Anki」\n\n"
                        + "3. 卡片进本机收藏库，之后点头像里的「立即同步」推到云端")
                .negative("直接开始", new Runnable() {
                    @Override public void run() { store.setIntroShown(true); }
                })
                .positive("去登录", new Runnable() {
                    @Override public void run() {
                        store.setIntroShown(true);
                        showAccountDialog();
                    }
                })
                .show();
    }

    // ------------------------------------------------------------------ 导航项

    /** 底部栏（竖向）或左侧栏（横向）里的一个页签 */
    static class NavItem extends LinearLayout {
        private final ImageView icon;
        private final TextView label;
        private final boolean horizontal;
        private final int iconType;
        private boolean active;

        NavItem(android.content.Context ctx, boolean horizontalMode, String text, int type) {
            super(ctx);
            horizontal = horizontalMode;
            iconType = type;
            setOrientation(horizontal ? HORIZONTAL : VERTICAL);
            setGravity(android.view.Gravity.CENTER);

            icon = new ImageView(ctx);
            int sz = Ui.dp(horizontal ? 22 : 21);
            LayoutParams ilp = horizontal
                    ? new LayoutParams(sz, sz)
                    : new LayoutParams(sz, sz);
            if (horizontal) {
                ilp = new LayoutParams(sz, sz);
                ilp.rightMargin = Ui.dp(10);
            }
            icon.setLayoutParams(ilp);
            addView(icon);

            label = new TextView(ctx);
            label.setText(text);
            label.setTextSize(horizontal ? 15 : 11);
            label.setGravity(android.view.Gravity.CENTER);
            addView(label, horizontal
                    ? new LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f)
                    : new LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
                            ViewGroup.LayoutParams.WRAP_CONTENT));
            if (!horizontal) {
                LayoutParams lp = (LayoutParams) label.getLayoutParams();
                lp.topMargin = Ui.dp(3);
            }
            setActive(false);
        }

        void setActive(boolean on) {
            active = on;
            int color = on ? Ui.ACCENT : Ui.SUB;
            icon.setImageDrawable(new IconDrawable(iconType, color));
            label.setTextColor(color);
            setBackground(on ? Ui.round(Ui.ACCENT_SOFT, horizontal ? 10 : 12)
                            : Ui.round(0x00000000, horizontal ? 10 : 12));
            if (horizontal) setPadding(Ui.dp(10), 0, Ui.dp(6), 0);
        }

        @Override
        public void setSelected(boolean selected) {
            super.setSelected(selected);
        }

        boolean isActive() { return active; }
    }
}
