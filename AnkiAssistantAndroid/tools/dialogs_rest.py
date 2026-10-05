"""其余弹窗迁移到 DialogUi：BrowseView / CreateView / MainActivity / ChangelogView。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")


def patch(name, pairs):
    p = APP + "\\src\\com\\ankiassistant\\" + name
    s = io.open(p, encoding="utf-8").read()
    for old, new in pairs:
        if old in s:
            s = s.replace(old, new, 1)
        else:
            print("  !! %s 未匹配: %s" % (name, old.strip().split("\n")[0][:64]))
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("  已处理 " + name)


# ---------------- BrowseView ----------------
patch("BrowseView.java", [
    # 选择牌组
    ('''        new android.app.AlertDialog.Builder(act)
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
                .show();''',
     '''        new DialogUi.Builder(act)
                .title("选择牌组")
                .choices(items, checked, new DialogUi.Picker() {
                    @Override public void onPick(int which) {
                        selectedDeck = which == 0 ? "" : items[which];
                        deckBtn.setText(which == 0 ? "全部牌组 ▾" : selectedDeck + " ▾");
                        query();
                    }
                })
                .negative("取消", null)
                .show();'''),
    # 删除卡片
    ('''        new android.app.AlertDialog.Builder(act)
                .setTitle("删除卡片")
                .setMessage("从本机收藏库删除这张卡片？（下次同步会同步到 AnkiWeb）")
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {''',
     '''        new DialogUi.Builder(act)
                .title("删除卡片")
                .message("从本机收藏库删除这张卡片。下次同步时云端也会一并删除。")
                .negative("取消", null)
                .positive("删除", new Runnable() {
                    @Override public void run() {'''),
    ('''                                        }
                                    });
                                }
                            }
                        });
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }''',
     '''                                        }
                                    });
                                }
                            }
                        });
                    }
                })
                .show();
    }'''),
    # 清空草稿
    ('''        new android.app.AlertDialog.Builder(act)
                .setTitle("清空草稿")
                .setMessage("确定清空全部本地草稿？清空后无法恢复。")
                .setPositiveButton("清空", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        store.clearDrafts();
                        renderDrafts();
                    }
                })
                .setNegativeButton("取消", null)
                .show();''',
     '''        new DialogUi.Builder(act)
                .title("清空草稿")
                .message("确定清空全部本地草稿？清空后无法恢复。")
                .negative("取消", null)
                .positive("清空", new Runnable() {
                    @Override public void run() {
                        store.clearDrafts();
                        renderDrafts();
                    }
                })
                .show();'''),
])

# ---------------- CreateView ----------------
patch("CreateView.java", [
    ('''                            AlertDialog d = new AlertDialog.Builder(act)
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
                            d.show();''',
     '''                            new DialogUi.Builder(act)
                                    .title("选择牌组")
                                    .choices(items, -1, new DialogUi.Picker() {
                                        @Override
                                        public void onPick(int which) {
                                            deckValue = items[which];
                                            js("setDeck(" + JSONObject.quote(items[which]) + ")");
                                        }
                                    })
                                    .negative("取消", null)
                                    .show();'''),
])

# ---------------- MainActivity ----------------
patch("MainActivity.java", [
    # 需要全量同步
    ('''        new AlertDialog.Builder(this)
                .setTitle("需要全量同步")
                .setMessage(reason + "\\n\\n上传：用本机的卡片覆盖云端\\n下载：用云端覆盖本机"
                        + "\\n\\n（本机是刚装的、云端才有你的卡片时，选「下载云端」）")
                .setPositiveButton("上传本机", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        runFullSync(user, pass, Boolean.TRUE);
                    }
                })
                .setNeutralButton("下载云端", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        runFullSync(user, pass, Boolean.FALSE);
                    }
                })
                .setNegativeButton("取消", null)
                .show();''',
     '''        new DialogUi.Builder(this)
                .title("需要全量同步")
                .message(reason + "\\n\\n上传：用本机的卡片覆盖云端\\n下载：用云端覆盖本机"
                        + "\\n\\n本机刚装好、云端才有卡片时，选「下载云端」。")
                .negative("取消", null)
                .neutral("上传本机", new Runnable() {
                    @Override public void run() { runFullSync(user, pass, Boolean.TRUE); }
                })
                .positive("下载云端", new Runnable() {
                    @Override public void run() { runFullSync(user, pass, Boolean.FALSE); }
                })
                .show();'''),
    # 首次引导
    ('''        AlertDialog d = new AlertDialog.Builder(this)
                .setTitle("三步开始使用")
                .setMessage("1. 设置 → AnkiWeb 同步：填邮箱和密码，点「登录并同步」"
                        + "（首次会问你上传还是下载，云端已有卡片就选下载）\\n\\n"
                        + "2. 回到制卡页输入单词 → 点「AI 填充」→ 逐项检查后「保存到 Anki」\\n\\n"
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
        d.show();''',
     '''        new DialogUi.Builder(this)
                .title("三步开始使用")
                .message("1. 点左上角头像登录 AnkiWeb（云端已有卡片就选「下载云端」）\\n\\n"
                        + "2. 制卡页输入单词 → 点「AI 填充」→ 逐项检查后「保存到 Anki」\\n\\n"
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
                .show();'''),
])

# ---------------- ChangelogView ----------------
patch("ChangelogView.java", [
    # 拉取中的提示
    ('''        final AlertDialog busy = new AlertDialog.Builder(act)
                .setTitle("更新内容")
                .setMessage("正在获取最新更新日志…")
                .setCancelable(false)
                .create();
        busy.show();''',
     '''        final Dialog busy = new DialogUi.Builder(act)
                .title("更新内容")
                .message("正在获取最新更新日志…")
                .show();
        busy.setCancelable(false);'''),
    # 没有内置日志
    ('''            new AlertDialog.Builder(act)
                    .setTitle("更新日志")
                    .setMessage("APK 里没有找到 CHANGELOG.md（构建时没打包进去？）")
                    .setPositiveButton("知道了", null)
                    .show();''',
     '''            new DialogUi.Builder(act)
                    .title("更新日志")
                    .message("APK 里没有找到 CHANGELOG.md（构建时没打包进去？）")
                    .positive("知道了", null)
                    .show();'''),
    # 更新日志阅读器（自带关闭按钮，所以弹窗本身不需要按钮）
    ('''        final AlertDialog dlg = new AlertDialog.Builder(act).setView(root).create();
        close.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { dlg.dismiss(); }
        });
        dlg.show();
        Window w = dlg.getWindow();
        if (w != null) {
            DisplayMetrics dm = act.getResources().getDisplayMetrics();
            w.setLayout((int) (dm.widthPixels * 0.94), (int) (dm.heightPixels * 0.88));
            w.setBackgroundDrawable(Ui.round(Ui.WHITE, 14));
        }
    }''',
     '''        final Dialog[] holder = new Dialog[1];
        close.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (holder[0] != null) holder[0].dismiss();
            }
        });
        Dialog made = new DialogUi.Builder(act).content(root).wide().show();
        holder[0] = made;
        Window w = made.getWindow();
        if (w != null) {
            DisplayMetrics dm = act.getResources().getDisplayMetrics();
            w.setLayout((int) (dm.widthPixels * 0.92), (int) (dm.heightPixels * 0.86));
        }
    }'''),
])
