package com.ankiassistant;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.graphics.Typeface;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/**
 * 浏览页：
 *   · 云端：读 Anki 的牌组/笔记（deckNames / findNotes / notesInfo），点开看渲染好的卡片
 *   · 本地草稿：断网时保存的卡片，联网后一键补发
 *   · 同步：手动触发 AnkiConnect 的 sync()，把本地改动推到 AnkiWeb 云端
 */
public class BrowseView extends LinearLayout {

    private final MainActivity act;
    private final Store store;

    private Button segCloud, segDraft;
    private LinearLayout cloudPane, draftPane, detailPane;
    private LinearLayout cloudList, draftList;
    private TextView cloudStatus, draftStatus;
    private EditText searchInput;
    private Button deckBtn, searchBtn, syncBtn;

    private String[] decks = new String[0];
    private String selectedDeck = "";
    private boolean decksLoaded;
    private JSONArray notes = new JSONArray();
    private WebView detailWeb;
    private long currentNoteId = -1;

    public BrowseView(MainActivity context) {
        super(context);
        act = context;
        store = context.store;
        setOrientation(VERTICAL);
        setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(8));
        build();
    }

    // ------------------------------------------------------------------ 部件

    private TextView smallLabel(String text) {
        TextView t = new TextView(getContext());
        t.setText(text);
        t.setTextColor(Ui.SUB);
        t.setTextSize(12);
        return t;
    }

    private void build() {
        // ---- 分段切换 ----
        LinearLayout seg = new LinearLayout(getContext());
        seg.setOrientation(LinearLayout.HORIZONTAL);
        seg.setPadding(0, 0, 0, Ui.dp(12));

        segCloud = new Button(getContext());
        segDraft = new Button(getContext());
        segCloud.setText("云端卡片");
        segDraft.setText("本地草稿");
        // 两个按钮各占一半，不然一个撑满、一个很小，看着不平衡
        seg.addView(segCloud, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        LinearLayout.LayoutParams segLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        segLp.leftMargin = Ui.dp(8);
        segDraft.setLayoutParams(segLp);
        seg.addView(segDraft);
        addView(seg);

        segCloud.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(0); }
        });
        segDraft.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(1); }
        });

        // ---- 内容区（三个面板叠在一起，靠 visibility 切换） ----
        FrameFlip flip = new FrameFlip(getContext());
        addView(flip, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        cloudPane = buildCloudPane();
        draftPane = buildDraftPane();
        detailPane = buildDetailPane();
        flip.addView(cloudPane, 0, new FrameParams());
        flip.addView(draftPane, 1, new FrameParams());
        flip.addView(detailPane, 2, new FrameParams());
        detailPane.setVisibility(GONE);
        draftPane.setVisibility(GONE);

        selectTab(0);
    }

    /** 简单的层叠容器（不用 FrameLayout 是为了少一层 import 混淆，其实就是 FrameLayout） */
    private static class FrameFlip extends android.widget.FrameLayout {
        FrameFlip(android.content.Context c) { super(c); }
    }

    private static class FrameParams extends android.widget.FrameLayout.LayoutParams {
        FrameParams() { super(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT); }
    }

    private ScrollView scrollWith(LinearLayout inner) {
        ScrollView sv = new ScrollView(getContext());
        sv.setVerticalScrollBarEnabled(false);
        sv.addView(inner, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return sv;
    }

    private LinearLayout col() {
        LinearLayout l = new LinearLayout(getContext());
        l.setOrientation(LinearLayout.VERTICAL);
        return l;
    }

    // ------------------------------------------------------------------ 云端面板

    private LinearLayout buildCloudPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        deckBtn = new Button(getContext());
        deckBtn.setText("全部牌组 ▾");
        Ui.secondary(deckBtn);
        deckBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { showDeckPicker(); }
        });
        row.addView(deckBtn, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        searchInput = new EditText(getContext());
        searchInput.setSingleLine(true);
        searchInput.setTextSize(14);
        Ui.field(searchInput);
        Ui.hint(searchInput, "搜索（支持 Anki 语法）");
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        slp.leftMargin = Ui.dp(10);
        slp.rightMargin = Ui.dp(10);
        row.addView(searchInput, slp);

        searchBtn = new Button(getContext());
        searchBtn.setText("查询");
        Ui.primary(searchBtn);
        searchBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { query(); }
        });
        row.addView(searchBtn, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        pane.addView(row);

        LinearLayout row2 = new LinearLayout(getContext());
        row2.setOrientation(LinearLayout.HORIZONTAL);
        row2.setGravity(Gravity.CENTER_VERTICAL);
        row2.setPadding(0, Ui.dp(12), 0, 0);
        syncBtn = new Button(getContext());
        syncBtn.setText("↻ 同步到云端");
        Ui.secondary(syncBtn);
        syncBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { doSync(); }
        });
        row2.addView(syncBtn, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        TextView tip = new TextView(getContext());
        tip.setText("  点条目看卡片详情");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(12);
        row2.addView(tip, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        pane.addView(row2);

        cloudStatus = new TextView(getContext());
        cloudStatus.setTextSize(12.5f);
        cloudStatus.setTextColor(Ui.TEXT_DIM);
        cloudStatus.setPadding(0, Ui.dp(7), 0, Ui.dp(5));
        pane.addView(cloudStatus);

        cloudList = col();
        pane.addView(scrollWith(cloudList), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return pane;
    }

    // ------------------------------------------------------------------ 草稿面板

    private LinearLayout buildDraftPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        Button sendAll = new Button(getContext());
        sendAll.setText("全部发送到 Anki");
        Ui.primary(sendAll);
        sendAll.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { sendAllDrafts(); }
        });
        row.addView(sendAll, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        Button clear = new Button(getContext());
        clear.setText("清空");
        Ui.secondary(clear);
        clear.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { confirmClearDrafts(); }
        });
        LinearLayout.LayoutParams clp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        clp.leftMargin = Ui.dp(8);
        row.addView(clear, clp);
        pane.addView(row);

        draftStatus = new TextView(getContext());
        draftStatus.setTextSize(12.5f);
        draftStatus.setTextColor(Ui.TEXT_DIM);
        draftStatus.setPadding(0, Ui.dp(7), 0, Ui.dp(5));
        pane.addView(draftStatus);

        draftList = col();
        pane.addView(scrollWith(draftList), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return pane;
    }

    // ------------------------------------------------------------------ 详情面板

    private LinearLayout buildDetailPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        Button back = new Button(getContext());
        back.setText("← 返回列表");
        Ui.secondary(back);
        back.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { closeDetail(); }
        });
        row.addView(back, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        Button edit = new Button(getContext());
        edit.setText("在电脑上编辑");
        Ui.secondary(edit);
        edit.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { openInDesktop(); }
        });
        LinearLayout.LayoutParams elp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        elp.leftMargin = Ui.dp(6);
        row.addView(edit, elp);

        Button del = new Button(getContext());
        del.setText("删除");
        Ui.danger(del);
        del.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { confirmDelete(); }
        });
        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        dlp.leftMargin = Ui.dp(6);
        row.addView(del, dlp);
        pane.addView(row);

        LinearLayout webWrap = new LinearLayout(getContext());
        webWrap.setOrientation(LinearLayout.VERTICAL);
        Ui.card(webWrap);
        webWrap.setPadding(0, 0, 0, 0);
        webWrap.setClipToOutline(true);
        LinearLayout.LayoutParams wlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        wlp.topMargin = Ui.dp(8);
        pane.addView(webWrap, wlp);

        detailWeb = new WebView(getContext());
        WebSettings s = detailWeb.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        boolean fileMode = act.editorUrl().startsWith("file:");
        s.setAllowFileAccess(fileMode);
        s.setAllowFileAccessFromFileURLs(fileMode);
        s.setAllowUniversalAccessFromFileURLs(fileMode);
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(false);
        detailWeb.setBackgroundColor(0x00000000);
        detailWeb.loadUrl(act.editorUrl());
        webWrap.addView(detailWeb, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        detailWebReady = false;
        detailWeb.setWebViewClient(new WebViewClient() {
            @Override
            public void onPageFinished(WebView view, String url) {
                detailWebReady = true;
                if (pendingFields != null) showCardInWeb(pendingFields);
            }
        });
        return pane;
    }

    private boolean detailWebReady;
    private JSONObject pendingFields;

    // ------------------------------------------------------------------ 切换

    private void selectTab(int i) {
        segCloud.setBackground(i == 0 ? Ui.press(Ui.ACCENT, 10) : Ui.press(Ui.WHITE, 10));
        segCloud.setTextColor(i == 0 ? Ui.WHITE : Ui.ACCENT);
        segDraft.setBackground(i == 1 ? Ui.press(Ui.ACCENT, 10) : Ui.press(Ui.WHITE, 10));
        segDraft.setTextColor(i == 1 ? Ui.WHITE : Ui.ACCENT);

        detailPane.setVisibility(GONE);
        cloudPane.setVisibility(i == 0 ? VISIBLE : GONE);
        draftPane.setVisibility(i == 1 ? VISIBLE : GONE);
        if (i == 1) renderDrafts();
        if (i == 0 && !decksLoaded) loadDecks();
    }

    public void onShown() {
        if (!decksLoaded) loadDecks();
    }

    // ------------------------------------------------------------------ 数据

    private void loadDecks() {
        cloudStatus.setText("正在连接 Anki…");
        cloudStatus.setTextColor(Ui.TEXT_DIM);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    AnkiClient anki = client();
                    final JSONArray names = anki.deckNames();
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            decksLoaded = true;
                            decks = new String[names.length()];
                            for (int i = 0; i < decks.length; i++) decks[i] = names.optString(i, "");
                            cloudStatus.setText("已连接 Anki，共 " + decks.length + " 个牌组"
                                    + (notes.length() > 0 ? "" : "，点「查询」列出卡片"));
                            cloudStatus.setTextColor(Ui.GREEN);
                        }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            cloudStatus.setText("连不上 Anki：" + e.getMessage());
                            cloudStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private AnkiClient client() {
        return new AnkiClient(store.ankiHost(), store.ankiPort(), store.ankiApiKey());
    }

    /** 组装 Anki 搜索语句：牌组 + 用户输入（支持 Anki 搜索语法） */
    private String buildQuery(String user) {
        String q = "";
        if (selectedDeck.length() > 0) q = "deck:\"" + selectedDeck + "\"";
        if (user != null && user.length() > 0) q = (q.length() > 0 ? q + " " : "") + user;
        if (q.length() == 0) q = "deck:*";
        return q;
    }

    private void query() {
        cloudStatus.setText("正在查询…");
        cloudStatus.setTextColor(Ui.TEXT_DIM);
        final String user = searchInput.getText().toString().trim();
        final String query = buildQuery(user);

        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    AnkiClient anki = client();
                    JSONArray ids = anki.findNotes(query);
                    JSONArray take = new JSONArray();
                    for (int i = 0; i < ids.length() && i < 100; i++) take.put(ids.opt(i));
                    final JSONArray info = take.length() == 0
                            ? new JSONArray() : anki.notesInfo(take);
                    final int total = ids.length();
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            notes = info;
                            renderNotes(total);
                        }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            cloudStatus.setText("查询失败：" + e.getMessage());
                            cloudStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void renderNotes(int total) {
        cloudList.removeAllViews();
        if (notes.length() == 0) {
            cloudStatus.setText("没有找到卡片");
            cloudStatus.setTextColor(Ui.AMBER);
            return;
        }
        cloudStatus.setText("共 " + total + " 张，显示前 " + notes.length() + " 张"
                + (total > notes.length() ? "（用搜索缩小范围）" : ""));
        cloudStatus.setTextColor(Ui.TEXT_DIM);
        for (int i = 0; i < notes.length(); i++) {
            JSONObject n = notes.optJSONObject(i);
            if (n != null) cloudList.addView(noteRow(n));
            if (i < notes.length() - 1) cloudList.addView(hairline());
        }
    }

    private View hairline() {
        View v = new View(getContext());
        v.setBackgroundColor(Ui.LINE);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(1));
        lp.leftMargin = Ui.dp(12);
        lp.rightMargin = Ui.dp(12);
        v.setLayoutParams(lp);
        return v;
    }

    /** 标签：AnkiConnect 返回的是数组，拼成 "# a  # b" */
    static String tagString(JSONObject note) {
        JSONArray arr = note.optJSONArray("tags");
        if (arr == null) return note.optString("tags", "");
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < arr.length(); i++) {
            String t = arr.optString(i, "");
            if (t.length() == 0) continue;
            if (sb.length() > 0) sb.append("   ");
            sb.append("# ").append(t);
        }
        return sb.toString();
    }

    private JSONObject fieldOf(JSONObject note, String name) {
        JSONObject fs = note.optJSONObject("fields");
        if (fs == null) return null;
        return fs.optJSONObject(name);
    }

    private String fieldValue(JSONObject note, String name) {
        JSONObject f = fieldOf(note, name);
        return f == null ? "" : f.optString("value", "");
    }

    private View noteRow(final JSONObject note) {
        long id = note.optLong("noteId", -1);
        String word = fieldValue(note, "单词");
        if (word.trim().length() == 0) {
            // 用户可能用了别的笔记类型：取第一个字段当标题
            JSONObject fs = note.optJSONObject("fields");
            if (fs != null) {
                java.util.Iterator<String> it = fs.keys();
                if (it.hasNext()) word = fieldValue(note, it.next());
            }
        }
        String def = fieldValue(note, "定义");
        if (def.trim().length() == 0) def = note.optString("modelName", "");
        String tags = tagString(note);

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.VERTICAL);
        row.setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(10));

        TextView t = new TextView(getContext());
        t.setText(stripHtml(word));
        t.setTextColor(Ui.INK);
        t.setTextSize(17);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        row.addView(t);

        TextView s = new TextView(getContext());
        s.setText(ellipsize(stripHtml(def), 90));
        s.setTextColor(Ui.TEXT_BODY);
        s.setTextSize(13.5f);
        s.setPadding(0, Ui.dp(3), 0, 0);
        row.addView(s);

        if (tags.trim().length() > 0) {
            TextView g = new TextView(getContext());
            g.setText(tags);
            g.setTextColor(Ui.TEXT_DIM);
            g.setTextSize(11.5f);
            g.setPadding(0, Ui.dp(3), 0, 0);
            row.addView(g);
        }

        final long nid = id;
        row.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { openDetail(note, nid); }
        });
        return row;
    }

    // ------------------------------------------------------------------ 详情

    private void openDetail(JSONObject note, long id) {
        currentNoteId = id;
        JSONObject fields = new JSONObject();
        JSONObject fs = note.optJSONObject("fields");
        if (fs != null) {
            java.util.Iterator<String> it = fs.keys();
            while (it.hasNext()) {
                String k = it.next();
                try { fields.put(k, fieldValue(note, k)); }
                catch (org.json.JSONException ignored) { }
            }
        }
        cloudPane.setVisibility(GONE);
        draftPane.setVisibility(GONE);
        detailPane.setVisibility(VISIBLE);
        showCardInWeb(fields);
    }

    private void showCardInWeb(JSONObject fields) {
        if (fields == null) return;
        if (!detailWebReady) {
            pendingFields = fields;
            return;
        }
        pendingFields = fields;
        String f = fields.toString();
        try {
            detailWeb.evaluateJavascript("setTemplates(" + templateJson() + ")", null);
            detailWeb.evaluateJavascript("showCard(" + f + ")", null);
        } catch (Throwable ignored) { }
    }

    private String templateJson() {
        try {
            JSONObject t = new JSONObject();
            t.put("front", CardFormat.CARD_FRONT);
            t.put("back", CardFormat.CARD_BACK);
            t.put("css", CardFormat.CARD_CSS);
            return t.toString();
        } catch (Exception e) {
            return "{}";
        }
    }

    private void closeDetail() {
        detailPane.setVisibility(GONE);
        cloudPane.setVisibility(VISIBLE);
    }

    private void openInDesktop() {
        if (currentNoteId < 0) return;
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    client().guiEditNote(currentNoteId);
                    Th.ui(new Runnable() {
                        @Override public void run() { toastStatus("已在电脑上打开编辑器"); }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override public void run() { toastStatus("打开失败：" + e.getMessage()); }
                    });
                }
            }
        });
    }

    private void toastStatus(String msg) {
        cloudStatus.setText(msg);
        cloudStatus.setTextColor(Ui.AMBER);
    }

    private void confirmDelete() {
        if (currentNoteId < 0) return;
        final long id = currentNoteId;
        new AlertDialog.Builder(act)
                .setTitle("删除这张卡片？")
                .setMessage("删除后同步会从 AnkiWeb 云端一起删掉，无法撤销。")
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        Th.bg(new Runnable() {
                            @Override
                            public void run() {
                                try {
                                    JSONArray ids = new JSONArray();
                                    ids.put(id);
                                    client().deleteNotes(ids);
                                    Th.ui(new Runnable() {
                                        @Override public void run() {
                                            closeDetail();
                                            query();
                                        }
                                    });
                                } catch (final AnkiClient.AnkiException e) {
                                    Th.ui(new Runnable() {
                                        @Override public void run() { toastStatus("删除失败：" + e.getMessage()); }
                                    });
                                }
                            }
                        });
                    }
                })
                .setNegativeButton("取消", null)
                .create().show();
    }

    private void doSync() {
        cloudStatus.setText("正在同步到 AnkiWeb…");
        cloudStatus.setTextColor(Ui.SUB);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    client().sync();
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            cloudStatus.setText("同步完成 ✓");
                            cloudStatus.setTextColor(Ui.GREEN);
                        }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            cloudStatus.setText("同步失败：" + e.getMessage());
                            cloudStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void showDeckPicker() {
        if (decks.length == 0) {
            cloudStatus.setText("还没读到牌组（Anki 连上了吗？），也可以直接搜索");
            cloudStatus.setTextColor(Ui.AMBER);
            loadDecks();
            return;
        }
        String[] items = new String[decks.length + 1];
        items[0] = "全部牌组";
        System.arraycopy(decks, 0, items, 1, decks.length);
        new AlertDialog.Builder(act)
                .setTitle("选择牌组")
                .setItems(items, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        selectedDeck = which == 0 ? "" : decks[which - 1];
                        deckBtn.setText((selectedDeck.length() == 0 ? "全部牌组" : selectedDeck) + " ▾");
                        query();
                    }
                })
                .setNegativeButton("取消", null)
                .create().show();
    }

    // ------------------------------------------------------------------ 草稿

    private void renderDrafts() {
        JSONArray arr = store.drafts();
        draftList.removeAllViews();
        if (arr.length() == 0) {
            draftStatus.setText("没有草稿。Anki 连不上时保存的卡片会自动进这里。");
            draftStatus.setTextColor(Ui.TEXT_DIM);
            return;
        }
        draftStatus.setText("共 " + arr.length() + " 条草稿");
        draftStatus.setTextColor(Ui.TEXT_DIM);
        for (int i = 0; i < arr.length(); i++) {
            JSONObject d = arr.optJSONObject(i);
            if (d != null) draftList.addView(draftRow(d, i));
            if (i < arr.length() - 1) draftList.addView(hairline());
        }
    }

    private View draftRow(final JSONObject d, final int index) {
        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(Ui.dp(12), Ui.dp(9), Ui.dp(12), Ui.dp(9));

        LinearLayout info = col();
        TextView t = new TextView(getContext());
        t.setText(d.optString("word", "(无词)"));
        t.setTextColor(Ui.INK);
        t.setTextSize(16);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        info.addView(t);

        TextView m = new TextView(getContext());
        long time = d.optLong("time", 0);
        m.setText(d.optString("deck", "") + "  ·  "
                + new SimpleDateFormat("MM-dd HH:mm", Locale.US).format(new Date(time)));
        m.setTextColor(Ui.TEXT_DIM);
        m.setTextSize(11.5f);
        m.setPadding(0, Ui.dp(2), 0, 0);
        info.addView(m);
        row.addView(info, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        Button send = new Button(getContext());
        send.setText("发送");
        Ui.primary(send);
        send.setTextSize(13);
        send.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { sendDraft(index); }
        });
        row.addView(send, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        Button del = new Button(getContext());
        del.setText("删");
        Ui.danger(del);
        del.setTextSize(13);
        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        dlp.leftMargin = Ui.dp(6);
        del.setLayoutParams(dlp);
        del.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                store.removeDraft(index);
                renderDrafts();
            }
        });
        row.addView(del);
        return row;
    }

    private void sendDraft(final int index) {
        JSONObject d = store.drafts().optJSONObject(index);
        if (d == null) return;
        draftStatus.setText("正在发送…");
        draftStatus.setTextColor(Ui.SUB);
        final JSONObject draft = d;
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    JSONObject fields = draft.optJSONObject("fields");
                    client().saveNote(draft.optString("deck", store.defaultDeck()), fields,
                            CreateView.parseTags(draft.optString("tags", "")), store.autoSync());
                    store.removeDraft(index);
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            draftStatus.setText("发送成功 ✓ 已同步云端");
                            draftStatus.setTextColor(Ui.GREEN);
                            renderDrafts();
                        }
                    });
                } catch (final AnkiClient.AnkiException e) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            draftStatus.setText("发送失败：" + e.getMessage());
                            draftStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void sendAllDrafts() {
        final JSONArray arr = store.drafts();
        if (arr.length() == 0) {
            draftStatus.setText("没有草稿");
            draftStatus.setTextColor(Ui.TEXT_DIM);
            return;
        }
        draftStatus.setText("正在逐条发送 0/" + arr.length() + "…");
        draftStatus.setTextColor(Ui.SUB);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                int ok = 0;
                String lastErr = "";
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject d = arr.optJSONObject(i);
                    if (d == null) continue;
                    try {
                        client().saveNote(d.optString("deck", store.defaultDeck()),
                                d.optJSONObject("fields"),
                                CreateView.parseTags(d.optString("tags", "")), store.autoSync());
                        store.removeDraft(0);   // 成功一条删一条（始终删第一条，索引不漂移）
                        ok++;
                        final int done = ok;
                        final int total = arr.length();
                        Th.ui(new Runnable() {
                            @Override public void run() {
                                draftStatus.setText("正在逐条发送 " + done + "/" + total + "…");
                            }
                        });
                    } catch (AnkiClient.AnkiException e) {
                        lastErr = e.getMessage();
                        break;
                    }
                }
                final String err = lastErr;
                final int done2 = ok;
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (done2 == arr.length()) {
                            draftStatus.setText("全部发送成功 ✓ 共 " + done2 + " 条");
                            draftStatus.setTextColor(Ui.GREEN);
                        } else {
                            draftStatus.setText("已发送 " + done2 + "/" + arr.length()
                                    + " 条" + (err.length() > 0 ? "，后续失败：" + err : ""));
                            draftStatus.setTextColor(done2 > 0 ? Ui.AMBER : Ui.RED);
                        }
                        renderDrafts();
                    }
                });
            }
        });
    }

    private void confirmClearDrafts() {
        if (store.draftCount() == 0) return;
        new AlertDialog.Builder(act)
                .setTitle("清空全部草稿？")
                .setMessage("草稿删除后无法恢复。")
                .setPositiveButton("清空", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        store.clearDrafts();
                        renderDrafts();
                    }
                })
                .setNegativeButton("取消", null)
                .create().show();
    }

    // ------------------------------------------------------------------ 小工具

    static String stripHtml(String s) {
        if (s == null) return "";
        String t = s.replaceAll("<br\\s*/?>", " ");
        t = t.replaceAll("<[^>]+>", "");
        t = t.replace("&nbsp;", " ").replace("&amp;", "&")
                .replace("&lt;", "<").replace("&gt;", ">")
                .replace("&quot;", "\"").replace("&#39;", "'");
        return t.replace('\n', ' ').trim();
    }

    static String ellipsize(String s, int max) {
        if (s == null) return "";
        return s.length() <= max ? s : s.substring(0, max) + "…";
    }
}
