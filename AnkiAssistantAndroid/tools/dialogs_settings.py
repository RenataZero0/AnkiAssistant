"""把所有系统 AlertDialog 换成统一的 DialogUi。"""
import io
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
sys.stdout.reconfigure(encoding="utf-8")


def patch(name, pairs, must=True):
    p = APP + "\\src\\com\\ankiassistant\\" + name
    s = io.open(p, encoding="utf-8").read()
    for old, new in pairs:
        if old in s:
            s = s.replace(old, new, 1)
        else:
            print("  !! %s 未匹配: %s" % (name, old.strip().split("\n")[0][:60]))
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("  已处理 " + name)


# ---------- Ui：圆角顶部（弹窗里的软色头部用） ----------
p = APP + r"\src\com\ankiassistant\Ui.java"
s = io.open(p, encoding="utf-8").read()
if "roundTop(" not in s:
    anchor = "    public static GradientDrawable roundStroke("
    add = '''    /** 只有上面两个角是圆的（弹窗里的软色头部用） */
    public static GradientDrawable roundTop(int color, float radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        float r = dp(radiusDp);
        g.setCornerRadii(new float[]{r, r, r, r, 0, 0, 0, 0});
        return g;
    }

'''
    s = s.replace(anchor, add + anchor, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("  Ui.roundTop 已加")

# ---------- SettingsView ----------
patch("SettingsView.java", [
    # 1) 选择输出格式
    ('''        new AlertDialog.Builder(act)
                .setTitle("选择输出格式")
                .setSingleChoiceItems(names, checked, new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int which) {
                        store.setActiveConfigId(all.get(which).id);
                        d.dismiss();
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
                .show();''',
     '''        new DialogUi.Builder(act)
                .title("选择输出格式")
                .choices(names, checked, new DialogUi.Picker() {
                    @Override public void pick(int which) { }
                    @Override public void onPick(int which) {
                        store.setActiveConfigId(all.get(which).id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .negative("取消", null)
                .show();'''),
    # 2) 新建/编辑输出格式
    ('''        new AlertDialog.Builder(act)
                .setTitle(creating ? "新建输出格式" : "编辑输出格式")
                .setView(sv)
                .setPositiveButton("保存", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {''',
     '''        new DialogUi.Builder(act)
                .title(creating ? "新建输出格式" : "编辑输出格式")
                .content(sv)
                .positive("保存", new Runnable() {
                    @Override public void run() {'''),
    ('''                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    /** 以内置/当前 config 为模板复制一份，供新建时改 */''',
     '''                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .negative("取消", null)
                .show();
    }

    /** 以内置/当前 config 为模板复制一份，供新建时改 */'''),
    # 3) 删除输出格式
    ('''        new AlertDialog.Builder(act)
                .setTitle("删除 config")
                .setMessage("确定删除「" + c.name + "」吗？已经用它做过的卡片不受影响。")
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        store.deleteConfig(c.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .setNegativeButton("取消", null)
                .show();''',
     '''        final CardConfig target = c;
        new DialogUi.Builder(act)
                .title("删除输出格式")
                .message("确定删除「" + c.name + "」吗？用它做过的卡片不受影响。")
                .negative("取消", null)
                .positive("删除", new Runnable() {
                    @Override public void run() {
                        store.deleteConfig(target.id);
                        refreshConfigCard();
                        onConfigChanged();
                    }
                })
                .show();'''),
    # 4) 发现新版本
    ('''        final AlertDialog dlg = new AlertDialog.Builder(act)
                .setView(root)
                .setPositiveButton("下载并安装", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) { downloadAndInstall(rel); }
                })
                .setNeutralButton("打开仓库", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) {
                        try {
                            Intent web = new Intent(Intent.ACTION_VIEW,
                                    android.net.Uri.parse(rel.pageUrl.length() > 0
                                            ? rel.pageUrl : Updater.REPO_PAGE));
                            act.startActivity(web);
                        } catch (Exception e) {
                            updateMsg("打不开浏览器：" + e.getMessage(), Ui.RED);
                        }
                    }
                })
                .setNegativeButton("稍后", null)
                .create();
        dlg.show();
        android.view.Window w = dlg.getWindow();
        if (w != null) {
            // 高度交给内容决定（说明区的最终高度已在显示前按内容量好）
            w.setLayout((int) (getResources().getDisplayMetrics().widthPixels * 0.94),
                    ViewGroup.LayoutParams.WRAP_CONTENT);
            w.setBackgroundDrawable(Ui.round(Ui.WHITE, 14));
        }
    }''',
     '''        new DialogUi.Builder(act)
                .content(root)
                .wide()
                .negative("稍后", null)
                .neutral("打开仓库", new Runnable() {
                    @Override public void run() {
                        try {
                            Intent web = new Intent(Intent.ACTION_VIEW,
                                    android.net.Uri.parse(rel.pageUrl.length() > 0
                                            ? rel.pageUrl : Updater.REPO_PAGE));
                            act.startActivity(web);
                        } catch (Exception e) {
                            updateMsg("打不开浏览器：" + e.getMessage(), Ui.RED);
                        }
                    }
                })
                .positive("下载并安装", new Runnable() {
                    @Override public void run() { downloadAndInstall(rel); }
                })
                .show();
    }'''),
    # 5) 选择 AI 服务商
    ('''        new AlertDialog.Builder(act)
                .setTitle("选择 AI 服务商")
                .setItems(labels, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {''',
     '''        new DialogUi.Builder(act)
                .title("选择 AI 服务商")
                .choices(labels, -1, new DialogUi.Picker() {
                    @Override
                    public void onPick(int which) {'''),
    ('''                        aiStatus.setTextColor(Ui.AMBER);
                    }
                })
                .setNegativeButton("取消", null)
                .create().show();''',
     '''                        aiStatus.setTextColor(Ui.AMBER);
                    }
                })
                .negative("取消", null)
                .show();'''),
])
