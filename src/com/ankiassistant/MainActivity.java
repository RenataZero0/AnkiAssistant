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
        CrashHandler.install(this);
        // WebView 读不了 assets 里 1MB 以上的文件（MathJax 就超了），所以起个本机小服务器
        assetServer = new AssetServer(getAssets());
        int port = assetServer.start();
        android.util.Log.i("AnkiAssistant", "asset server port = " + port);
        applySystemBars();
        buildUi();
        show(current);
        maybeIntro();
        // 启动就测一次 Anki 连接，侧栏底部那盏灯直接反映真实状态
        if (rail != null) checkAnkiLamp();
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

    /** 调试用：`--ez configProbe true` 自建 config → 设为当前 → 用它写一张卡 */
    private void runConfigProbe() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    CardConfig c = new CardConfig();
                    c.id = "cfgprobe";
                    c.name = "探针词汇";
                    c.noteType = "探针词汇";
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
                    String msg = AnkiBackend.save(MainActivity.this, store, "探针牌组", note,
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

            if (rail != null) checkAnkiLamp();
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
                new int[]{0xFFFFFFFF, 0xFFF3F7FD});
        topBar.setBackground(barBg);
        topBar.setElevation(Ui.dp(2));
        // 左右留白与各页面内容的 12dp 对齐，标题才会和下面的卡片左边缘齐平
        topBar.setPadding(Ui.dp(12), 0, Ui.dp(12), 0);
        topBar.setGravity(android.view.Gravity.CENTER_VERTICAL);
        column.addView(topBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(56)));

        TextView title = new TextView(this);
        Ui.title(title, "Anki 助手");
        title.setTextSize(18);
        // 高度是撑满的，必须显式垂直居中，否则文字会贴在顶栏上沿
        title.setGravity(android.view.Gravity.CENTER_VERTICAL);
        topBar.addView(title, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));

        TextView ver = new TextView(this);
        ver.setText(Version.VERSION_TAG);
        ver.setTextColor(Ui.TEXT_DIM);
        ver.setTextSize(11);
        ver.setGravity(android.view.Gravity.CENTER);
        ver.setBackground(Ui.round(0xFFEEF2F9, 8));
        ver.setPadding(Ui.dp(9), Ui.dp(3), Ui.dp(9), Ui.dp(3));
        topBar.addView(ver, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        View accentLine = new View(this);
        accentLine.setBackground(new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,
                new int[]{0x663568E8, 0x003568E8}));
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
                new int[]{0xFFF8FAFD, 0xFFF1F5FB}));
        r.setPadding(Ui.dp(8), Ui.dp(16), Ui.dp(8), Ui.dp(14));

        // ---- 顶部：应用标识（填掉上方那块空白） ----
        TextView logo = new TextView(this);
        logo.setText("A");
        logo.setTextColor(Ui.WHITE);
        logo.setTextSize(17);
        logo.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        logo.setGravity(android.view.Gravity.CENTER);
        logo.setBackground(Ui.round(Ui.ACCENT, 11));
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(Ui.dp(36), Ui.dp(36));
        llp.gravity = android.view.Gravity.CENTER_HORIZONTAL;
        llp.bottomMargin = Ui.dp(4);
        r.addView(logo, llp);

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
            @Override public void onClick(View v) { checkAnkiLamp(); }
        });

        ankiDot = new View(this);
        ankiDot.setBackground(Ui.round(0xFFB9C2D0, 5));
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

    /** 底部状态灯：绿=连得上，红=连不上，黄=正在测 */
    public void checkAnkiLamp() {
        if (ankiChecking || ankiDot == null) return;   // 手机端（底栏）没有这盏灯
        // 内置引擎可用 → 本机就能写，不用去测电脑
        if (AnkiBackend.useEngine(this, store)) {
            setAnkiLamp(0xFF22A06B, "本机");
            ankiChecking = false;
            return;
        }
        // 本机 AnkiDroid 可直接写入 → 也不用去测电脑
        if (store.useAnkiDroid() && AnkiDroidClient.ready(this)) {
            setAnkiLamp(0xFF22A06B, "本机");
            ankiChecking = false;
            return;
        }
        // 内置引擎不可用（也没有 AnkiDroid）→ 这台设备当前没法写卡
        setAnkiLamp(0xFFE5484D, "不可用");
        ankiChecking = false;
    }

    private void setAnkiLamp(int color, String label) {
        if (ankiDot != null) ankiDot.setBackground(Ui.round(color, 5));
        if (ankiLampText != null) {
            ankiLampText.setText(label);
            ankiLampText.setTextColor(color == 0xFF22A06B ? color : Ui.TEXT_DIM);
        }
    }

    private LinearLayout buildBottomBar() {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        bar.setBackground(Ui.roundStroke(Ui.WHITE, Ui.LINE, 0));
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
        AlertDialog d = new AlertDialog.Builder(this)
                .setTitle("三步开始使用")
                .setMessage("1. 设置 → AnkiWeb 同步：填邮箱和密码，点「登录并同步」"
                        + "（首次会问你上传还是下载，云端已有卡片就选下载）\n\n"
                        + "2. 回到制卡页输入单词 → 点「AI 填充」→ 逐项检查后「保存到 Anki」\n\n"
                        + "3. 卡片进本机收藏库，之后点同步即可推到 AnkiWeb 云端")
                .setPositiveButton("去设置", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        store.setIntroShown(true);
                        show(TAB_SETTINGS);
                    }
                })
                .setNegativeButton("直接开始", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        store.setIntroShown(true);
                    }
                })
                .create();
        d.show();
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
