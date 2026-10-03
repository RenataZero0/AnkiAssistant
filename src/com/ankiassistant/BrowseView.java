package com.ankiassistant;

import android.content.DialogInterface;
import android.graphics.Typeface;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
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
 * 浏览页。
 *
 * 设计要点：**不强加任何字段假设** —— 卡片可能来自任意笔记类型
 * （自己建的「专业术语卡」「英语格式卡」，或别人/别的客户端建的任何类型），
 * 所以：
 *   · 列表里用「第一个有内容的字段」当标题，后面跟笔记类型名与标签
 *   · 详情把该笔记的**所有字段**按名字列出来渲染（HTML + MathJax），不猜字段含义
 *   · 不再有"在电脑上编辑"（早就没有电脑端了）与"同步到云端"（同步在左上角头像里）
 */
public class BrowseView extends LinearLayout {

    private final MainActivity act;
    private final Store store;

    private Button segCards, segDraft;
    private LinearLayout cardsPane, draftPane, detailPane;
    private LinearLayout cardList, draftList;
    private TextView cardsStatus, draftStatus;
    private EditText searchInput;
    private Button deckBtn;

    private String[] decks = new String[0];
    private String selectedDeck = "";
    private JSONArray notes = new JSONArray();

    private WebView detailWeb;
    private long currentNoteId = -1;

    public BrowseView(MainActivity context) {
        super(context);
        this.act = context;
        this.store = context.store();
        setOrientation(VERTICAL);
        build();
    }

    // ------------------------------------------------------------------ 骨架

    private void build() {
        // 页面自身留白：顶栏下面本来太空，加上内边距才不显得贴在一起
        setPadding(Ui.dp(12), Ui.dp(14), Ui.dp(12), Ui.dp(12));

        // 分段控件：一条圆角灰底 + 两个等宽扁平标签（选中的是白底蓝字）
        LinearLayout seg = new LinearLayout(getContext());
        seg.setOrientation(HORIZONTAL);
        seg.setBackground(Ui.round(0xFFEDF1F8, 11));
        seg.setPadding(Ui.dp(4), Ui.dp(4), Ui.dp(4), Ui.dp(4));
        LinearLayout.LayoutParams segLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        segLp.bottomMargin = Ui.dp(14);
        addView(seg, segLp);

        segCards = new Button(getContext());
        segDraft = new Button(getContext());
        segCards.setText("本机卡片");
        segDraft.setText("本地草稿");
        seg.addView(segCards, new LinearLayout.LayoutParams(0, Ui.dp(40), 1f));
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0, Ui.dp(40), 1f);
        slp.leftMargin = Ui.dp(4);
        segDraft.setLayoutParams(slp);
        seg.addView(segDraft);
        segCards.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(0); }
        });
        segDraft.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { selectTab(1); }
        });

        android.widget.FrameLayout flip = new android.widget.FrameLayout(getContext());
        addView(flip, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        cardsPane = buildCardsPane();
        draftPane = buildDraftPane();
        detailPane = buildDetailPane();
        flip.addView(cardsPane);
        flip.addView(draftPane);
        flip.addView(detailPane);
        draftPane.setVisibility(GONE);
        detailPane.setVisibility(GONE);
        selectTab(0);
    }

    private void selectTab(int which) {
        cardsPane.setVisibility(which == 0 ? VISIBLE : GONE);
        draftPane.setVisibility(which == 1 ? VISIBLE : GONE);
        detailPane.setVisibility(GONE);
        styleSeg(segCards, which == 0);
        styleSeg(segDraft, which == 1);
        if (which == 0 && decks.length == 0) loadDecks();
        if (which == 1) renderDrafts();
    }


    /** 扁平分段标签：选中=白底蓝字，未选中=透明灰字 */
    private void styleSeg(Button b, boolean on) {
        b.setTextColor(on ? Ui.ACCENT : Ui.SUB);
        b.setTextSize(14);
        b.setTypeface(on ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        b.setAllCaps(false);
        b.setStateListAnimator(null);
        b.setElevation(0);
        b.setBackground(on ? Ui.round(0xFFFFFFFF, 9) : Ui.round(0x00000000, 9));
        b.setPadding(0, 0, 0, 0);
    }

    private LinearLayout col() {
        LinearLayout l = new LinearLayout(getContext());
        l.setOrientation(VERTICAL);
        return l;
    }

    private ScrollView scrollWith(LinearLayout inner) {
        ScrollView sv = new ScrollView(getContext());
        sv.setVerticalScrollBarEnabled(false);
        sv.addView(inner, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return sv;
    }

    // ------------------------------------------------------------------ 本机卡片面板

    private LinearLayout buildCardsPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(HORIZONTAL);
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
        Ui.hint(searchInput, "搜索（支持 deck: tag: 等 Anki 语法）");
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        slp.leftMargin = Ui.dp(10);
        slp.rightMargin = Ui.dp(10);
        row.addView(searchInput, slp);

        Button searchBtn = new Button(getContext());
        searchBtn.setText("查询");
        Ui.primary(searchBtn);
        searchBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { query(); }
        });
        row.addView(searchBtn, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        pane.addView(row);

        cardsStatus = new TextView(getContext());
        cardsStatus.setTextSize(12.5f);
        cardsStatus.setTextColor(Ui.TEXT_DIM);
        cardsStatus.setPadding(0, Ui.dp(9), 0, Ui.dp(5));
        pane.addView(cardsStatus);

        cardList = col();
        pane.addView(scrollWith(cardList), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return pane;
    }

    private void loadDecks() {
        cardsStatus.setText("正在读取牌组…");
        cardsStatus.setTextColor(Ui.TEXT_DIM);
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    final String[] ds = AnkiBackend.deckNames(getContext(), store);
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            decks = ds;
                            cardsStatus.setText("共 " + ds.length + " 个牌组，点「查询」列出卡片");
                            cardsStatus.setTextColor(Ui.GREEN);
                        }
                    });
                } catch (final Exception e) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            cardsStatus.setText("读取牌组失败：" + e.getMessage());
                            cardsStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void showDeckPicker() {
        if (decks.length == 0) { loadDecks(); return; }
        final String[] items = new String[decks.length + 1];
        items[0] = "全部牌组";
        for (int i = 0; i < decks.length; i++) items[i + 1] = decks[i];
        int checked = 0;
        for (int i = 0; i < decks.length; i++) if (decks[i].equals(selectedDeck)) checked = i + 1;
        new android.app.AlertDialog.Builder(act)
                .setTitle("选择牌组")
                .setSingleChoiceItems(items, checked, new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int which) {
                        selectedDeck = which == 0 ? "" : items[which];
                        deckBtn.setText(which == 0 ? "全部牌组 ▾" : selectedDeck + " ▾");
                        d.dismiss();
                        query();
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    /** 组装 Anki 搜索语句 */
    private String buildQuery(String user) {
        String q = "";
        if (selectedDeck.length() > 0) q = "deck:\"" + selectedDeck + "\"";
        if (user != null && user.length() > 0) q = (q.length() > 0 ? q + " " : "") + user;
        if (q.length() == 0) q = "deck:*";
        return q;
    }

    private void query() {
        cardsStatus.setText("正在查询…");
        cardsStatus.setTextColor(Ui.TEXT_DIM);
        final String q = buildQuery(searchInput.getText().toString().trim());
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    final JSONArray found = AnkiBackend.searchNotes(getContext(), store, q, 100);
                    int t = AnkiBackend.searchTotal(getContext(), store, q);
                    final int total = t >= 0 ? t : found.length();
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            notes = found;
                            renderCards(total);
                        }
                    });
                } catch (final Exception e) {
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            cardsStatus.setText("查询失败：" + e.getMessage());
                            cardsStatus.setTextColor(Ui.RED);
                        }
                    });
                }
            }
        });
    }

    private void renderCards(int total) {
        cardList.removeAllViews();
        if (notes.length() == 0) {
            cardsStatus.setText("没有找到卡片");
            cardsStatus.setTextColor(Ui.AMBER);
            return;
        }
        cardsStatus.setText("共 " + total + " 张，显示前 " + notes.length() + " 张"
                + (total > notes.length() ? "（用搜索缩小范围）" : ""));
        cardsStatus.setTextColor(Ui.TEXT_DIM);
        for (int i = 0; i < notes.length(); i++) {
            JSONObject n = notes.optJSONObject(i);
            if (n != null) cardList.addView(cardRow(n));
            if (i < notes.length() - 1) cardList.addView(hairline());
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

    // ------------------------------------------------------- 与字段名无关的取值工具

    /** 取「第一个有内容的字段」的值（按 order 排序；任何笔记类型都适用） */
    static String firstFieldValue(JSONObject note) {
        JSONObject fs = note == null ? null : note.optJSONObject("fields");
        if (fs == null) return "";
        String best = "";
        int bestOrder = Integer.MAX_VALUE;
        java.util.Iterator<String> it = fs.keys();
        while (it.hasNext()) {
            String k = it.next();
            JSONObject f = fs.optJSONObject(k);
            if (f == null) continue;
            String v = f.optString("value", "");
            if (v.trim().length() == 0) continue;
            int ord = f.optInt("order", 999);
            if (ord < bestOrder) { bestOrder = ord; best = v; }
        }
        return best;
    }

    static String tagString(JSONObject note) {
        JSONArray arr = note == null ? null : note.optJSONArray("tags");
        if (arr == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < arr.length(); i++) {
            String t = arr.optString(i, "");
            if (t.length() == 0) continue;
            if (sb.length() > 0) sb.append("  ");
            sb.append("# ").append(t);
        }
        return sb.toString();
    }

    /** 去掉 HTML 标签，做列表标题用 */
    static String plainText(String html) {
        if (html == null) return "";
        String s = html.replaceAll("(?is)<(br|/p|/div)[^>]*>", " ");
        s = s.replaceAll("(?s)<[^>]*>", "");
        s = s.replace("&nbsp;", " ").replace("&amp;", "&")
             .replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", "\"");
        s = s.replaceAll("\\s+", " ").trim();
        return s;
    }

    private View cardRow(final JSONObject note) {
        final long id = note.optLong("noteId", -1);
        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(VERTICAL);
        row.setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(10));
        row.setBackground(Ui.press(Ui.WHITE, 0));

        TextView t = new TextView(getContext());
        String title = plainText(firstFieldValue(note));
        if (title.length() == 0) title = "(空卡片)";
        if (title.length() > 80) title = title.substring(0, 80) + "…";
        t.setText(title);
        t.setTextColor(Ui.INK);
        t.setTextSize(16);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        row.addView(t);

        String model = note.optString("modelName", "");
        String tags = tagString(note);
        TextView m = new TextView(getContext());
        m.setText((model.length() > 0 ? model : "笔记") + (tags.length() > 0 ? "　·　" + tags : ""));
        m.setTextColor(Ui.TEXT_DIM);
        m.setTextSize(11.5f);
        m.setPadding(0, Ui.dp(3), 0, 0);
        row.addView(m);

        row.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { openDetail(note); }
        });
        return row;
    }

    // ------------------------------------------------------------------ 详情面板

    private LinearLayout buildDetailPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        Button back = new Button(getContext());
        back.setText("← 返回列表");
        Ui.secondary(back);
        back.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { closeDetail(); }
        });
        row.addView(back, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        Button del = new Button(getContext());
        del.setText("删除本机卡片");
        Ui.danger(del);
        del.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { confirmDelete(); }
        });
        LinearLayout.LayoutParams dlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        dlp.leftMargin = Ui.dp(10);
        row.addView(del, dlp);
        pane.addView(row);

        TextView tip = new TextView(getContext());
        tip.setText("按笔记类型原样列出所有字段（HTML 与公式都会渲染）");
        tip.setTextColor(Ui.TEXT_DIM);
        tip.setTextSize(11.5f);
        tip.setPadding(0, Ui.dp(9), 0, Ui.dp(6));
        pane.addView(tip);

        detailWeb = new WebView(getContext());
        detailWeb.getSettings().setJavaScriptEnabled(true);
        detailWeb.setBackgroundColor(0xFFFFFFFF);
        // 详情直接复用制卡页的编辑器页面：字段样式、字号、行距与公式渲染都跟预览一模一样。
        // 载入完成后才允许注入内容（否则 JS 还没就绪）。
        detailWeb.setWebViewClient(new WebViewClient() {
            @Override public void onPageFinished(WebView view, String url) {
                detailWebReady = true;
                if (pendingNoteJson != null) pushNoteToWeb();
            }
        });
        detailWeb.loadUrl(act.editorUrl());
        pane.addView(detailWeb, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return pane;
    }

    private String pendingNoteJson;
    private boolean detailWebReady;

    private void openDetail(JSONObject note) {
        currentNoteId = note.optLong("noteId", -1);
        pendingNoteJson = buildNoteJson(note);
        cardsPane.setVisibility(GONE);
        draftPane.setVisibility(GONE);
        detailPane.setVisibility(VISIBLE);
        pushNoteToWeb();
    }

    /** 页面就绪后把内容推进去（没就绪就等 onPageFinished 回调） */
    private void pushNoteToWeb() {
        if (detailWeb == null || pendingNoteJson == null || !detailWebReady) return;
        detailWeb.evaluateJavascript("showNoteFields(" + pendingNoteJson + ")", null);
    }

    private void closeDetail() {
        detailPane.setVisibility(GONE);
        cardsPane.setVisibility(VISIBLE);
        currentNoteId = -1;
    }

    /**
     * 组装给 editor.html 的 JSON：{model, tags, fields:[{name,value}]}
     * 字段值会先清掉老版本的自定义包壳、并把被切断的 LaTeX 接回去。
     */
    private String buildNoteJson(JSONObject note) {
        JSONObject out = new JSONObject();
        try {
            out.put("model", note.optString("modelName", ""));
            out.put("tags", tagString(note).replace("# ", ""));
            JSONArray list = new JSONArray();
            JSONObject fs = note.optJSONObject("fields");
            if (fs != null) {
                java.util.List<String> keys = new java.util.ArrayList<String>();
                java.util.Iterator<String> it = fs.keys();
                while (it.hasNext()) keys.add(it.next());
                final JSONObject fso = fs;
                java.util.Collections.sort(keys, new java.util.Comparator<String>() {
                    @Override public int compare(String a, String b) {
                        JSONObject oa = fso.optJSONObject(a);
                        JSONObject ob = fso.optJSONObject(b);
                        return (oa == null ? 999 : oa.optInt("order", 999))
                                - (ob == null ? 999 : ob.optInt("order", 999));
                    }
                });
                for (String k : keys) {
                    JSONObject f = fs.optJSONObject(k);
                    String v = f == null ? "" : f.optString("value", "");
                    v = LaTeX.clean(v);
                    if (v.replaceAll("(?s)<[^>]*>", "").trim().length() == 0) continue;
                    JSONObject one = new JSONObject();
                    one.put("name", k);
                    one.put("value", v);
                    list.put(one);
                }
            }
            out.put("fields", list);
        } catch (Exception ignored) { }
        return out.toString();
    }

    private void confirmDelete() {
        if (currentNoteId < 0) return;
        final long id = currentNoteId;
        new android.app.AlertDialog.Builder(act)
                .setTitle("删除卡片")
                .setMessage("从本机收藏库删除这张卡片？（下次同步会同步到 AnkiWeb）")
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        Th.bg(new Runnable() {
                            @Override public void run() {
                                try {
                                    JSONArray ids = new JSONArray();
                                    ids.put(id);
                                    AnkiBackend.deleteNotes(getContext(), store, ids);
                                    Th.ui(new Runnable() {
                                        @Override public void run() {
                                            closeDetail();
                                            query();
                                        }
                                    });
                                } catch (final Exception e) {
                                    Th.ui(new Runnable() {
                                        @Override public void run() {
                                            cardsStatus.setText("删除失败：" + e.getMessage());
                                            cardsStatus.setTextColor(Ui.RED);
                                        }
                                    });
                                }
                            }
                        });
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    // ------------------------------------------------------------------ 草稿面板

    private LinearLayout buildDraftPane() {
        LinearLayout pane = col();

        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(HORIZONTAL);
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
        draftStatus.setPadding(0, Ui.dp(9), 0, Ui.dp(5));
        pane.addView(draftStatus);

        draftList = col();
        pane.addView(scrollWith(draftList), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return pane;
    }

    private void renderDrafts() {
        JSONArray arr = store.drafts();
        draftList.removeAllViews();
        if (arr.length() == 0) {
            draftStatus.setText("没有草稿。写入失败时保存的卡片会自动进这里。");
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
        row.setOrientation(HORIZONTAL);
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
        m.setText(d.optString("deck", "") + "　·　"
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
        send.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { sendDraft(index); }
        });
        row.addView(send, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return row;
    }

    private void sendDraft(final int index) {
        JSONObject d = store.drafts().optJSONObject(index);
        if (d == null) return;
        draftStatus.setText("正在发送…");
        draftStatus.setTextColor(Ui.SUB);
        final JSONObject draft = d;
        Th.bg(new Runnable() {
            @Override public void run() {
                try {
                    JSONObject fields = draft.optJSONObject("fields");
                    AnkiBackend.save(getContext(), store,
                            draft.optString("deck", store.defaultDeck()), fields,
                            CreateView.parseTagsToArray(draft.optString("tags", "")));
                    store.removeDraft(index);
                    Th.ui(new Runnable() {
                        @Override public void run() {
                            draftStatus.setText("发送成功 ✓");
                            draftStatus.setTextColor(Ui.GREEN);
                            renderDrafts();
                        }
                    });
                } catch (final Exception e) {
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
        if (arr.length() == 0) return;
        draftStatus.setText("正在逐条发送…");
        draftStatus.setTextColor(Ui.SUB);
        Th.bg(new Runnable() {
            @Override public void run() {
                int ok = 0;
                String lastErr = "";
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject d = arr.optJSONObject(i);
                    if (d == null) continue;
                    try {
                        AnkiBackend.save(getContext(), store,
                                d.optString("deck", store.defaultDeck()),
                                d.optJSONObject("fields"),
                                CreateView.parseTagsToArray(d.optString("tags", "")));
                        store.removeDraft(0);
                        ok++;
                    } catch (Exception e) {
                        lastErr = e.getMessage();
                        break;
                    }
                }
                final int done = ok;
                final String err = lastErr;
                final int total = arr.length();
                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (done == total) {
                            draftStatus.setText("全部发送成功 ✓ 共 " + done + " 条");
                            draftStatus.setTextColor(Ui.GREEN);
                        } else {
                            draftStatus.setText("已发送 " + done + "/" + total + " 条"
                                    + (err.length() > 0 ? "，后续失败：" + err : ""));
                            draftStatus.setTextColor(done > 0 ? Ui.AMBER : Ui.RED);
                        }
                        renderDrafts();
                    }
                });
            }
        });
    }

    private void confirmClearDrafts() {
        if (store.drafts().length() == 0) return;
        new android.app.AlertDialog.Builder(act)
                .setTitle("清空草稿")
                .setMessage("确定清空全部本地草稿？清空后无法恢复。")
                .setPositiveButton("清空", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        store.clearDrafts();
                        renderDrafts();
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    /** 切到浏览页时调用：刷新草稿列表 */
    public void onShown() {
        if (draftPane != null && draftPane.getVisibility() == VISIBLE) renderDrafts();
    }

    /** 判断一段文本是不是空的（草稿/详情里用得到，保留给以后扩展） */
    static boolean isBlank(String s) {
        return s == null || TextUtils.isEmpty(s.trim());
    }
}
