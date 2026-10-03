"""浏览页搜索框：手机上单独占一行（不再被牌组/查询按钮挤成一条缝），占位文案也缩短。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\BrowseView.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 占位文案缩短
s = s.replace('Ui.hint(searchInput, "搜索（支持 deck: tag: 等 Anki 语法）");',
              'Ui.hint(searchInput, "搜索卡片（可用 deck: tag: 等语法）");')
s = s.replace('Ui.hint(searchInput, "搜索（支持 Anki 语法）");',
              'Ui.hint(searchInput, "搜索卡片（可用 deck: tag: 等语法）");')

# 2) 手机：搜索框单独一行
old = '''        LinearLayout row = new LinearLayout(getContext());
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
        Ui.hint(searchInput, "搜索卡片（可用 deck: tag: 等语法）");
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
        pane.addView(row);'''
new = '''        boolean phone = getResources().getConfiguration().smallestScreenWidthDp < 600;

        deckBtn = new Button(getContext());
        deckBtn.setText("全部牌组 ▾");
        Ui.secondary(deckBtn);
        deckBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { showDeckPicker(); }
        });

        searchInput = new EditText(getContext());
        searchInput.setSingleLine(true);
        searchInput.setTextSize(14);
        Ui.field(searchInput);
        Ui.hint(searchInput, "搜索卡片（可用 deck: tag: 等语法）");

        if (phone) {
            // 窄屏：牌组 + 查询一行，搜索框单独占满一行（否则输入框被挤成一条缝，内容看不全）
            LinearLayout row1 = new LinearLayout(getContext());
            row1.setOrientation(HORIZONTAL);
            row1.setGravity(Gravity.CENTER_VERTICAL);
            row1.addView(deckBtn, new LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            Button searchBtn = new Button(getContext());
            searchBtn.setText("查询");
            Ui.primary(searchBtn);
            searchBtn.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { query(); }
            });
            LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            blp.leftMargin = Ui.dp(8);
            row1.addView(searchBtn, blp);
            pane.addView(row1);

            LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            slp.topMargin = Ui.dp(8);
            pane.addView(searchInput, slp);
        } else {
            LinearLayout row = new LinearLayout(getContext());
            row.setOrientation(HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.addView(deckBtn, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
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
        }'''
if old in s:
    s = s.replace(old, new, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("搜索框布局已改：手机单独一行")
else:
    print("!! 搜索行未匹配")
