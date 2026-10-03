package com.ankiassistant;

import android.content.Context;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * 把老卡片（旧版建在「问答题」等笔记类型里、背面用【音标】【定义】… 拼成一大段的术语卡）
 * 转换成现在的「专业术语卡」（七个字段各自独立）。
 *
 * 安全性：
 *   · 只处理**背面确实带【…】小节**的卡片，其它一律跳过
 *   · 动手前先把收藏库复制一份成 collection.anki2.bak
 *   · 先建新卡，成功后才删旧卡；失败的卡片原样保留
 */
public class NoteMigrator {

    private static final String TAG = "AnkiAssistant";

    /** 老卡里可能出现的小节名 → 新卡的字段 */
    private static final String[][] LABELS = {
            {"音标", "音标"},
            {"词性", "词性"},
            {"定义", "定义"},
            {"释义", "定义"},
            {"关联公式/符号", "关联公式/符号"},
            {"关联公式/符号", "关联公式/符号"},
            {"关联公式", "关联公式/符号"},
            {"公式", "关联公式/符号"},
            {"易混", "易混"},
            {"易混淆", "易混"},
            {"中文", "中文"},
            {"中文释义", "中文"},
    };

    public static class Result {
        public int scanned;
        public int converted;
        public int skipped;
        public int failed;
        public String backup = "";
        public final List<String> failures = new ArrayList<String>();

        public String summary() {
            StringBuilder sb = new StringBuilder();
            sb.append("扫描 ").append(scanned).append(" 张，转换 ").append(converted)
              .append(" 张");
            if (skipped > 0) sb.append("，跳过 ").append(skipped).append(" 张（不是这种老格式）");
            if (failed > 0) sb.append("，失败 ").append(failed).append(" 张");
            if (backup.length() > 0) sb.append("\n备份：").append(backup);
            return sb.toString();
        }
    }

    /** 老卡背面是否是我们认识的那种「【小节】内容」格式 */
    public static boolean looksLegacy(String backHtml) {
        if (backHtml == null) return false;
        String plain = backHtml.replaceAll("(?is)<[^>]*>", "");
        int hits = 0;
        for (String[] pair : LABELS) {
            if (plain.contains("【" + pair[0] + "】")) hits++;
        }
        return hits >= 2;   // 至少两个小节才认定是老术语卡
    }

    /** 把被切断的 LaTeX 接回去："\\(A\\),\\quad \\(B\\)" → "\\(A,\\quad B\\)" */
    public static String mergeSplitLatex(String s) {
        if (s == null) return null;
        String cur = s;
        for (int i = 0; i < 8; i++) {
            String merged = cur.replaceAll("\\)([^()]*\\\\[a-zA-Z]+[^()]*)\\\\(", "$1");
            if (merged.equals(cur)) break;
            cur = merged;
        }
        return cur;
    }

    /** 把背面拆成 字段名 -> 内容 */
    public static JSONObject parseBack(String backHtml) {
        JSONObject out = new JSONObject();
        if (backHtml == null) return out;
        String s = backHtml;
        // 老版本的自定义包壳会让公式渲染不出来，一并去掉
        s = LaTeX.clean(s);
        s = s.replaceAll("(?i)<br\\s*/?>", "\n");
        s = s.replaceAll("(?i)</(div|p)>", "\n");
        // 老卡里满是 <span style="color:..."> 之类的样式壳：全部剥掉，只留纯文本
        s = s.replaceAll("(?s)<[^>]*>", "");
        s = s.replace("&nbsp;", " ").replace("&amp;", "&").replace("&lt;", "<")
             .replace("&gt;", ">").replace("&quot;", "\"").replace("&#39;", "'");
        // 按【小节名】切块
        String[] parts = s.split("(?=【[^】]{1,12}】)");
        StringBuilder tail = new StringBuilder();
        for (String part : parts) {
            String t = part.trim();
            if (t.length() == 0) continue;
            if (!t.startsWith("【")) { tail.append(t).append("\n"); continue; }
            int end = t.indexOf('】');
            if (end < 0) { tail.append(t).append("\n"); continue; }
            String label = t.substring(1, end).trim().replace(" ", "")
                    .replace("\u3000", "");   // 老卡里标签可能带空格，例如「关联公式 / 符号」
            String body = LaTeX.mergeSplit(t.substring(end + 1).trim());
            String field = null;
            for (String[] pair : LABELS) {
                if (pair[0].equals(label)) { field = pair[1]; break; }
            }
            if (field == null) {
                tail.append(t).append("\n");   // 认不出的小节，塞进中文里免得丢
                continue;
            }
            try {
                String old = out.optString(field, "");
                out.put(field, old.length() > 0 ? old + "\n" + body : body);
            } catch (Exception ignored) { }
        }
        String leftover = tail.toString().trim();
        if (leftover.length() > 0) {
            try {
                String old = out.optString("中文", "");
                out.put("中文", old.length() > 0 ? old + "\n" + leftover : leftover);
            } catch (Exception ignored) { }
        }
        return out;
    }

    /**
     * 执行转换。deckHint 为空时按每张卡原本所在的牌组去搜（逐牌组搜，才能保住牌组归属）。
     */
    public static Result convert(Context c, Store store) throws Exception {
        return convert(c, store, false);
    }

    /** dryRun=true 时只统计，不改任何数据 */
    public static Result convert(Context c, Store store, boolean dryRun) throws Exception {
        Result r = new Result();

        // 1) 备份收藏库（dry run 不动数据）
        if (!dryRun) try {
            java.io.File dir = c.getFilesDir();
            java.io.File src = new java.io.File(dir, "collection.anki2");
            if (src.exists()) {
                java.io.File dst = new java.io.File(dir, "collection.anki2.bak");
                java.io.FileInputStream in = new java.io.FileInputStream(src);
                java.io.FileOutputStream out = new java.io.FileOutputStream(dst);
                byte[] buf = new byte[65536];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                in.close();
                out.close();
                r.backup = "collection.anki2.bak（" + (dst.length() / 1024) + " KB）";
            }
        } catch (Exception e) {
            Log.w(TAG, "备份收藏库失败：" + e.getMessage());
            r.backup = "备份失败：" + e.getMessage();
        }

        CardConfig cfg = null;
        for (CardConfig cc : CardConfig.builtins()) {
            if (CardConfig.usesAppDefaults(cc.id)) cfg = cc;   // A Level 那套
        }
        if (cfg == null) cfg = CardConfig.defaultConfig();

        // 2) 逐个牌组搜（这样能保住卡片原来所在的牌组）
        String[] decks = AnkiBackend.deckNames(c, store);
        for (String deck : decks) {
            JSONArray notes;
            try {
                notes = AnkiBackend.searchNotes(c, store, "deck:\"" + deck + "\"", 500);
            } catch (Exception e) {
                continue;
            }
            for (int i = 0; i < notes.length(); i++) {
                JSONObject note = notes.optJSONObject(i);
                if (note == null) continue;
                String model = note.optString("modelName", "");
                if ("专业术语卡".equals(model) || "英语词汇卡".equals(model)) continue;  // 已经是新格式
                JSONObject fs = note.optJSONObject("fields");
                if (fs == null) continue;
                r.scanned++;

                String word = "";
                String back = "";
                java.util.Iterator<String> it = fs.keys();
                while (it.hasNext()) {
                    String k = it.next();
                    JSONObject f = fs.optJSONObject(k);
                    if (f == null) continue;
                    int order = f.optInt("order", 999);
                    if (order == 0) word = f.optString("value", "");
                    else back = back + "\n" + f.optString("value", "");
                }
                if (!looksLegacy(back)) { r.skipped++; continue; }

                JSONObject parsed = parseBack(back);
                JSONObject ai = new JSONObject();
                if (parsed.has("音标")) ai.put("phonetic", parsed.optString("音标"));
                if (parsed.has("词性")) ai.put("pos", parsed.optString("词性"));
                if (parsed.has("定义")) ai.put("definition", parsed.optString("定义"));
                if (parsed.has("关联公式/符号")) ai.put("formula", parsed.optString("关联公式/符号"));
                if (parsed.has("易混")) ai.put("confusables", parsed.optString("易混"));
                if (parsed.has("中文")) ai.put("chinese", parsed.optString("中文"));

                String plainWord = BrowseView.plainText(word);
                JSONObject fields = CardFormat.noteFieldsFor(plainWord, ai, cfg);

                long oldId = note.optLong("noteId", -1);
                if (dryRun) {
                    if (r.converted == 0) {
                        android.util.Log.i(TAG, "MIGRATE 样例旧卡：" + plainWord + " 背面=" + back.replace("\n", " "));
                        android.util.Log.i(TAG, "MIGRATE 样例解析：" + parsed.toString());
                        android.util.Log.i(TAG, "MIGRATE 样例新字段：" + fields.toString());
                    }
                    r.converted++;
                    continue;
                }
                try {
                    // 用该卡片原本的牌组与标签写新卡
                    JSONArray tagArr = note.optJSONArray("tags");
                    String[] tags = new String[tagArr == null ? 0 : tagArr.length()];
                    for (int t = 0; t < tags.length; t++) tags[t] = tagArr.optString(t, "");
                    AnkiBackend.save(c, store, deck, fields, tags);
                    JSONArray del = new JSONArray();
                    del.put(oldId);
                    AnkiBackend.deleteNotes(c, store, del);
                    r.converted++;
                } catch (Exception e) {
                    r.failed++;
                    r.failures.add(plainWord + "：" + e.getMessage());
                }
            }
        }
        return r;
    }
}
