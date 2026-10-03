package com.ankiassistant;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.ValueCallback;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * 制卡页（主界面）：
 *   单词（正面） → AI 按 PRMOPT 第二节第 3 条的格式自动填充背面 → 富文本微调 → 保存到 Anki。
 *
 * 编辑与预览全部在 assets/editor.html 里完成（contenteditable + MathJax + 真正的卡片模板），
 * 这样输入与预览能力与 Anki 桌面端一致（加粗/斜体/颜色/列表/引用/代码/挖空/公式/HTML 源码）。
 */
public class CreateView extends LinearLayout {

    public interface FieldsCb { void onFields(JSONObject fields); }

    private final MainActivity act;
    private final Store store;

    private EditText wordInput;
    private TextView aiBadge;
    private Button aiBtn, clearBtn;
    /** 牌组/标签现在住在编辑区 HTML 里，Java 侧只留一份当前值（保存时由 JS 传上来） */
    private String deckValue = "", tagValue = "";
    private TextView statusLine;
    private WebView editor;
    private boolean editorReady;
    private ProgressBar aiBusy;
    private LinearLayout thinkingBox;
    private TextView thinkingHeader;
    private ScrollView thinkingScroll;
    private TextView thinkingText;
    private boolean thinkingExpanded;

    public CreateView(MainActivity context) {
        super(context);
        act = context;
        store = context.store;
        setOrientation(VERTICAL);
        setPadding(Ui.dp(12), Ui.dp(10), Ui.dp(12), Ui.dp(8));
        build();
    }

    // ------------------------------------------------------------------ 静态部件

    private LinearLayout card() {
        LinearLayout l = new LinearLayout(getContext());
        l.setOrientation(LinearLayout.VERTICAL);
        Ui.card(l);
        l.setPadding(Ui.dp(15), Ui.dp(13), Ui.dp(15), Ui.dp(13));
        return l;
    }

    private TextView smallLabel(String text) {
        TextView t = new TextView(getContext());
        t.setText(text);
        t.setTextColor(Ui.SUB);
        t.setTextSize(12);
        return t;
    }

    private void build() {
        // ---- 正面：单词 ----
        boolean wide = getResources().getConfiguration().screenWidthDp >= 600;

        LinearLayout wordCard = card();
        addView(wordCard, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // 标题行：宽屏时右边放 AI 徽标；窄屏徽标另起一行，否则标签会被挤断行
        TextView wordLabel = smallLabel("正面 · 单词（你输入的词就是卡片正面）");
        if (wide) {
            LinearLayout headRow = new LinearLayout(getContext());
            headRow.setOrientation(LinearLayout.HORIZONTAL);
            headRow.setGravity(Gravity.CENTER_VERTICAL);
            headRow.addView(wordLabel, new LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

            aiBadge = new TextView(getContext());
            aiBadge.setTextSize(12);
            aiBadge.setTextColor(Ui.ACCENT);
            aiBadge.setSingleLine(true);
            aiBadge.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { act.show(MainActivity.TAB_SETTINGS); }
            });
            headRow.addView(aiBadge, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            wordCard.addView(headRow);
        } else {
            wordCard.addView(wordLabel);
            aiBadge = new TextView(getContext());
            aiBadge.setTextSize(12);
            aiBadge.setTextColor(Ui.ACCENT);
            aiBadge.setPadding(0, Ui.dp(2), 0, 0);
            aiBadge.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { act.show(MainActivity.TAB_SETTINGS); }
            });
        }

        LinearLayout inputRow = new LinearLayout(getContext());
        inputRow.setOrientation(LinearLayout.HORIZONTAL);
        inputRow.setGravity(Gravity.CENTER_VERTICAL);
        inputRow.setPadding(0, Ui.dp(12), 0, 0);

        wordInput = new EditText(getContext());
        wordInput.setSingleLine(true);
        wordInput.setTextSize(21);
        Ui.field(wordInput);
        wordInput.setPadding(Ui.dp(14), Ui.dp(12), Ui.dp(14), Ui.dp(12));
        wordInput.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        Ui.hint(wordInput, "输入要制卡的英文单词");
        wordInput.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) { }
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) { }
            @Override public void afterTextChanged(Editable s) {
                pushWord(s.toString());
            }
        });
        inputRow.addView(wordInput, new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        clearBtn = new Button(getContext());
        clearBtn.setText("清空");
        Ui.secondary(clearBtn);
        clearBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { clearAll(); }
        });

        aiBtn = new Button(getContext());
        aiBtn.setText("AI 填充");
        Ui.primary(aiBtn);
        aiBtn.setPadding(Ui.dp(20), aiBtn.getPaddingTop(), Ui.dp(20), aiBtn.getPaddingBottom());
        aiBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { aiFill(); }
        });

        if (wide) {
            // 宽屏：单词 + 两个按钮同排，省下一整行给编辑器
            LinearLayout.LayoutParams clp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            clp.leftMargin = Ui.dp(12);
            inputRow.addView(clearBtn, clp);
            LinearLayout.LayoutParams alp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            alp.leftMargin = Ui.dp(8);
            inputRow.addView(aiBtn, alp);
            wordCard.addView(inputRow);
        } else {
            wordCard.addView(inputRow);
            wordCard.addView(aiBadge, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            LinearLayout btnRow = new LinearLayout(getContext());
            btnRow.setOrientation(LinearLayout.HORIZONTAL);
            btnRow.setGravity(Gravity.END);
            btnRow.setPadding(0, Ui.dp(8), 0, 0);
            btnRow.addView(clearBtn, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            LinearLayout.LayoutParams alp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            alp.leftMargin = Ui.dp(8);
            btnRow.addView(aiBtn, alp);
            wordCard.addView(btnRow);
        }

        // ---- 编辑器 ----
        LinearLayout editorWrap = card();
        editorWrap.setPadding(0, 0, 0, 0);
        editorWrap.setClipToOutline(false);
        LinearLayout.LayoutParams elp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        elp.topMargin = Ui.dp(9);
        addView(editorWrap, elp);

        editor = new WebView(getContext());
        WebSettings s = editor.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        // 走本机 http 时不需要文件访问权限；只有回退到 file:// 时才打开（编辑器里没有任何远程内容）
        boolean fileMode = act.editorUrl().startsWith("file:");
        s.setAllowFileAccess(fileMode);
        s.setAllowFileAccessFromFileURLs(fileMode);
        s.setAllowUniversalAccessFromFileURLs(fileMode);
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(false);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        editor.setBackgroundColor(0x00000000);
        editor.setWebChromeClient(new android.webkit.WebChromeClient() {
            @Override
            public boolean onConsoleMessage(android.webkit.ConsoleMessage m) {
                android.util.Log.i("AnkiJS", m.message() + "  (line " + m.lineNumber() + ")");
                return true;
            }
        });
        // 编辑区底部那些控件（牌组/标签/从Anki选择/保存）通过这个桥回调 Java
        editor.addJavascriptInterface(new Bridge(), "Android");

        editor.setWebViewClient(new WebViewClient() {
            @Override
            public void onPageFinished(WebView view, String url) {
                editorReady = true;
                pushConfig();
                pushTemplates();
                pushMeta();
                pushWord(wordInput.getText().toString());
                checkMathJax();
                if (store.introShown()) {
                    // 首次引导里用户可能已经改了设置，这里刷新一下徽标
                    refreshAiBadge();
                }
            }
        });
        editorWrap.addView(editor, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        // 千万不要忘了这一句：之前漏了 loadUrl，编辑器区域整块空白
        editor.loadUrl(act.editorUrl());

        // 牌组 / 标签 / 保存按钮都搬到编辑区内部了（assets/editor.html 的 #metaPane），
        // 通过下面的 JS 桥回调，编辑区里不再固定占一块面板。

        // ---- AI 思考过程（只在思考型模型真的返回内容时才出现，默认折叠）----
        thinkingBox = new LinearLayout(getContext());
        thinkingBox.setOrientation(LinearLayout.VERTICAL);
        thinkingBox.setBackground(Ui.round(0xFFF7F9FC, 10));
        thinkingBox.setPadding(Ui.dp(12), Ui.dp(9), Ui.dp(12), Ui.dp(9));
        thinkingBox.setVisibility(View.GONE);

        thinkingHeader = new TextView(getContext());
        thinkingHeader.setTextSize(12.5f);
        thinkingHeader.setTextColor(Ui.SUB);
        thinkingHeader.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { toggleThinking(); }
        });
        thinkingBox.addView(thinkingHeader);

        thinkingScroll = new ScrollView(getContext());
        thinkingScroll.setVisibility(View.GONE);
        thinkingText = new TextView(getContext());
        thinkingText.setTextSize(12);
        thinkingText.setTextColor(Ui.TEXT_BODY);
        thinkingText.setLineSpacing(0, 1.15f);
        thinkingText.setPadding(0, Ui.dp(6), 0, 0);
        thinkingScroll.addView(thinkingText, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        thinkingBox.addView(thinkingScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(150)));

        LinearLayout.LayoutParams tblp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tblp.topMargin = Ui.dp(10);
        addView(thinkingBox, tblp);

        // 状态行：转圈 + 文字，AI 生成期间要能看出"正在工作"
        LinearLayout statusRow = new LinearLayout(getContext());
        statusRow.setOrientation(LinearLayout.HORIZONTAL);
        statusRow.setGravity(Gravity.CENTER_VERTICAL);
        statusRow.setPadding(0, Ui.dp(8), 0, 0);

        aiBusy = new ProgressBar(getContext());
        aiBusy.setIndeterminate(true);
        aiBusy.setVisibility(View.GONE);
        if (aiBusy.getIndeterminateDrawable() != null) {
            aiBusy.getIndeterminateDrawable().setColorFilter(
                    Ui.ACCENT, android.graphics.PorterDuff.Mode.SRC_IN);
        }
        statusRow.addView(aiBusy, new LinearLayout.LayoutParams(Ui.dp(18), Ui.dp(18)));

        statusLine = new TextView(getContext());
        statusLine.setTextSize(12.5f);
        statusLine.setTextColor(Ui.TEXT_DIM);
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        slp.leftMargin = Ui.dp(8);
        statusRow.addView(statusLine, slp);

        addView(statusRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // 牌组/标签的当前值（编辑区里的输入框由 pushMeta() 填充）
        tagValue = store.defaultTags();
        deckValue = store.defaultDeck();
        refreshAiBadge();
    }

    // ------------------------------------------------------------------ 状态显示

    private void status(String msg, int color) {
        statusLine.setTextColor(color);
        statusLine.setText(msg);
    }

    public void refreshAiBadge() {
        aiBadge.setText("AI：" + AiClient.presetLabel(store.aiProvider()) + " · 点此更换");
    }

    /** 切回本页时刷新（设置可能改过） */
    public void onShown() {
        refreshAiBadge();
        if (deckValue.length() == 0) deckValue = store.defaultDeck();
        pushMeta();
    }

    // ------------------------------------------------------------------ WebView 桥接

    private void js(String code) {
        if (!editorReady) return;
        try { editor.evaluateJavascript(code, null); } catch (Throwable ignored) { }
    }

    private void pushTemplates() {
        try {
            CardConfig cfg = store.activeConfig();
            JSONObject t = new JSONObject();
            t.put("front", cfg.cardFront());
            t.put("back", cfg.cardBack());
            t.put("css", CardFormat.CARD_CSS);
            js("setTemplates(" + t.toString() + ")");
        } catch (Exception ignored) { }
    }

    /** 把当前 config 的字段清单下发给编辑器，让它按配置重建输入框 */
    private void pushConfig() {
        try {
            CardConfig cfg = store.activeConfig();
            JSONObject o = new JSONObject();
            o.put("name", cfg.name);
            org.json.JSONArray fs = new org.json.JSONArray();
            for (int i = 0; i < cfg.fields.size(); i++) {
                CardConfig.Field fd = cfg.fields.get(i);
                JSONObject x = new JSONObject();
                x.put("name", fd.name);
                x.put("hint", fd.hint == null ? "" : fd.hint);
                x.put("front", i == 0);
                fs.put(x);
            }
            o.put("fields", fs);
            js("applyConfig(" + o.toString() + ")");
        } catch (Exception ignored) { }
    }

    /** 设置页换了 config 之后调用：重建字段框并刷新模板 */
    public void refreshConfig() {
        pushConfig();
        pushTemplates();
    }

    private void pushWord(String word) {
        js("setWord(" + JSONObject.quote(word == null ? "" : word) + ")");
    }

    private void pushFields(JSONObject fields) {
        if (fields == null) return;
        js("setFields(" + fields.toString() + ")");
    }

    /** 把当前牌组/标签推进编辑区里的输入框 */
    private void pushMeta() {
        js("setMeta(" + JSONObject.quote(deckValue) + "," + JSONObject.quote(tagValue) + ")");
    }

    // ------------------------------------------------------------------ 编辑区里的牌组/标签/保存按钮（JS 桥）

    /** JS → Java：编辑区底部的牌组、标签、保存按钮都从这里回调过来 */
    public class Bridge {
        /** 牌组 → 标签映射（JS 里同步调用，映射表只有 CardFormat 一份） */
        @android.webkit.JavascriptInterface
        public String tagForDeck(String deck) {
            String t = CardFormat.tagsForDeck(deck);
            return t == null ? "" : t;
        }

        @android.webkit.JavascriptInterface
        public void pickDeck() {
            act.runOnUiThread(new Runnable() {
                @Override public void run() { CreateView.this.pickDeck(); }
            });
        }

        @android.webkit.JavascriptInterface
        public void saveNote(String deck, String tags) { fromEditor(deck, tags, false); }

        @android.webkit.JavascriptInterface
        public void saveDraft(String deck, String tags) { fromEditor(deck, tags, true); }

        @android.webkit.JavascriptInterface
        public void log(String msg) { android.util.Log.i("AnkiJS", msg == null ? "" : msg); }
    }

    private void fromEditor(String deck, String tags, final boolean asDraft) {
        deckValue = deck == null ? "" : deck.trim();
        tagValue = tags == null ? "" : tags.trim();
        act.runOnUiThread(new Runnable() {
            @Override public void run() { save(asDraft); }
        });
    }

    /** 3 秒后问一次编辑器：MathJax 到底加载成功没有（公式渲染依赖它） */
    private void checkMathJax() {
        new android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(new Runnable() {
            @Override
            public void run() {
                if (!editorReady) return;
                try {
                    editor.evaluateJavascript(
                            "(function(){try{return JSON.stringify(mjStatus());}"
                            + "catch(e){return '{\"error\":\"'+e+'\"}';}})()",
                            new ValueCallback<String>() {
                                @Override
                                public void onReceiveValue(String v) {
                                    android.util.Log.i("AnkiJS", "mjStatus = " + v);
                                    if (v != null && v.indexOf("\"scriptLoaded\":false") >= 0) {
                                        status("公式渲染组件没加载出来，公式会以源码显示（不影响保存）",
                                                Ui.AMBER);
                                    }
                                }
                            });
                } catch (Throwable ignored) { }
            }
        }, 3000);
    }

    /** 读回编辑器里的背面字段（异步） */    public void readFields(final FieldsCb cb) {
        if (!editorReady) {
            cb.onFields(null);
            return;
        }
        try {
            editor.evaluateJavascript(
                    "(function(){try{return getFields();}catch(e){return null;}})()",
                    new ValueCallback<String>() {
                        @Override
                        public void onReceiveValue(String value) {
                            JSONObject o = null;
                            try {
                                if (value != null && !"null".equals(value)) o = new JSONObject(value);
                            } catch (Exception ignored) { }
                            cb.onFields(o);
                        }
                    });
        } catch (Throwable t) {
            cb.onFields(null);
        }
    }

    private void clearEditor() {
        wordInput.setText("");
        js("clearFields()");
    }

    private void clearAll() {
        clearEditor();
        status("已清空", Ui.TEXT_DIM);
    }

    // ------------------------------------------------------------------ AI 填充

    private void aiFill() {
        final String word = wordInput.getText().toString().trim();
        if (word.length() == 0) {
            status("先在上面输入单词，再点 AI 填充", Ui.RED);
            wordInput.requestFocus();
            return;
        }
        if (store.aiBaseUrlEffective().length() == 0) {
            status("还没选 AI 服务商，请到「设置」里配置", Ui.RED);
            return;
        }
        final boolean useThinking = store.aiThinking();
        status(useThinking
                ? "AI 正在生成卡片内容… 已开启思考，约 30～60 秒，请稍等（不要重复点击）"
                : "AI 正在生成卡片内容… 通常 5～15 秒，请稍等（不要重复点击）", Ui.SUB);
        setBusy(true);

        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    AiClient ai = new AiClient();
                    final String base = store.aiBaseUrlEffective();
                    final String key = store.aiApiKey();
                    final String model = store.aiModelEffective();
                    AiClient.Reply reply = ai.chatDetailed(base, key, model, CardFormat.SYSTEM,
                            store.activeConfig().buildPrompt(word, store.subject()), !useThinking);
                    JSONObject parsed = CardFormat.parseAi(reply.content);
                    boolean retried = false;
                    if (parsed == null) {
                        // 自动重试一次：明确要求"不要思考、只输出 JSON"，并强制关掉思考。
                        // 长思考容易把输出预算吃掉导致 JSON 截断，这一步实测能救回来。
                        retried = true;
                        reply = ai.chatDetailed(base, key, model, CardFormat.SYSTEM,
                                store.activeConfig().buildPromptStrict(word, store.subject()), true);
                        parsed = CardFormat.parseAi(reply.content);
                    }
                    final JSONObject parsedF = parsed;
                    final String rawF = reply.content;
                    final String reasoningF = reply.reasoning;
                    final boolean retriedF = retried;
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            setBusy(false);
                            setThinking(reasoningF);
                            if (parsedF != null) {
                                pushFields(CardFormat.noteFieldsFor(word, parsedF, store.activeConfig()));
                                status(retriedF
                                        ? "AI 填充完成（首次输出格式不对，已自动重试成功），可以逐项修改后保存"
                                        : "AI 填充完成，可以逐项修改后保存", Ui.GREEN);
                            } else {
                                pushFields(CardFormat.noteFieldsFor(word,
                                        CardFormat.fallbackFields(word, rawF), store.activeConfig()));
                                status("AI 两次输出都不是 JSON，已原样放进「定义」，请手动整理", Ui.AMBER);
                            }
                        }
                    });
                } catch (final AiClient.AiException e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            setBusy(false);
                            status("AI 调用失败：" + e.getMessage(), Ui.RED);
                        }
                    });
                }
            }
        });
    }

    /** AI 生成期间：按钮变成"生成中…"并禁用，状态行转圈 */
    private void setBusy(boolean busy) {
        if (aiBusy != null) aiBusy.setVisibility(busy ? View.VISIBLE : View.GONE);
        aiBtn.setEnabled(!busy);
        clearBtn.setEnabled(!busy);
        aiBtn.setText(busy ? "生成中…" : "AI 填充");
    }

    /** 思考型模型的思考过程：默认折叠成一行，点开是限高的滚动区，不占地方也不突兀 */
    private void setThinking(String reasoning) {
        if (thinkingBox == null) return;
        String r = reasoning == null ? "" : reasoning.trim();
        if (r.length() == 0) {
            thinkingBox.setVisibility(View.GONE);
            thinkingText.setText("");
            return;
        }
        thinkingText.setText(r);
        thinkingExpanded = false;
        thinkingScroll.setVisibility(View.GONE);
        updateThinkingHeader();
        thinkingBox.setVisibility(View.VISIBLE);
    }

    private void toggleThinking() {
        thinkingExpanded = !thinkingExpanded;
        thinkingScroll.setVisibility(thinkingExpanded ? View.VISIBLE : View.GONE);
        updateThinkingHeader();
    }

    private void updateThinkingHeader() {
        int n = thinkingText.getText() == null ? 0 : thinkingText.getText().length();
        thinkingHeader.setText("AI 思考过程（" + n + " 字）"
                + (thinkingExpanded ? "　点击收起 ▴" : "　点击展开 ▾"));
    }

    // ------------------------------------------------------------------ 保存

    private void save(final boolean asDraft) {
        final String word = wordInput.getText().toString().trim();
        if (word.length() == 0) {
            status("先输入单词（卡片正面）", Ui.RED);
            wordInput.requestFocus();
            return;
        }
        final String deck = deckValue.trim().length() == 0
                ? store.defaultDeck() : deckValue.trim();
        final String tags = tagValue.trim();

        status(asDraft ? "正在存草稿…" : "正在读取编辑器内容…", Ui.SUB);
        readFields(new FieldsCb() {
            @Override
            public void onFields(JSONObject back) {
                final JSONObject note = CardFormat.mergeNoteFor(word, back, store.activeConfig());
                if (asDraft) {
                    store.addDraft(word, note, deck, tags);
                    status("已存入本地草稿箱（浏览 → 本地草稿 可随时补发）", Ui.GREEN);
                    clearEditor();
                    return;
                }
                js("setSaving(true)");
                status("正在写入本机收藏库…", Ui.SUB);
                Th.bg(new Runnable() {
                    @Override
                    public void run() {
                        try {
                            // 保存路径统一由 AnkiBackend 决定（内置引擎优先）
                            final String ok = AnkiBackend.save(getContext(), store, deck, note,
                                    parseTagsToArray(tags));
                            Th.ui(new Runnable() {
                                @Override
                                public void run() {
                                    js("setSaving(false)");
                                    clearEditor();
                                    status(ok, Ui.GREEN);
                                }
                            });
                        } catch (final Exception e) {
                            // 写不进去就转草稿，卡片不会丢
                            store.addDraft(word, note, deck, tags);
                            Th.ui(new Runnable() {
                                @Override
                                public void run() {
                                    js("setSaving(false)");
                                    status("写入失败，已自动转存草稿箱。原因：" + e.getMessage(), Ui.AMBER);
                                }
                            });
                        }
                    }
                });
            }
        });
    }

    /** JSONArray -> String[]（本机 AnkiDroid 那边要数组） */
    static String[] jsonToStrings(JSONArray arr) {
        if (arr == null || arr.length() == 0) return new String[0];
        String[] out = new String[arr.length()];
        for (int i = 0; i < arr.length(); i++) out[i] = arr.optString(i, "");
        return out;
    }

    /** 标签字符串 -> 数组（空格/逗号分隔） */
    public static String[] parseTagsToArray(String tags) {
        JSONArray a = parseTags(tags);
        return jsonToStrings(a);
    }

    public static JSONArray parseTags(String tags) {
        JSONArray arr = new JSONArray();
        if (tags == null) return arr;
        String[] parts = tags.split("[,，\\s]+");
        for (int i = 0; i < parts.length; i++) {
            if (parts[i].trim().length() > 0) arr.put(parts[i].trim());
        }
        return arr;
    }

    // ------------------------------------------------------------------ 牌组选择

    private void pickDeck() {
        status("正在读取本机牌组…", Ui.SUB);
        Th.bg(new Runnable() {
            @Override
            public void run() {
                try {
                    final String[] items = AnkiBackend.deckNames(getContext(), store);
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            status("共 " + items.length + " 个牌组", Ui.TEXT_DIM);
                            if (items.length == 0) return;
                            AlertDialog d = new AlertDialog.Builder(act)
                                    .setTitle("选择牌组")
                                    .setItems(items, new DialogInterface.OnClickListener() {
                                        @Override
                                        public void onClick(DialogInterface dialog, int which) {
                                            deckValue = items[which];
                                            js("setDeck(" + JSONObject.quote(items[which]) + ")");
                                        }
                                    })
                                    .setNegativeButton("取消", null)
                                    .create();
                            d.show();
                        }
                    });
                } catch (final Exception e) {
                    Th.ui(new Runnable() {
                        @Override
                        public void run() {
                            status("读取牌组失败（可以手动输入）：" + e.getMessage(), Ui.RED);
                        }
                    });
                }
            }
        });
    }
}
