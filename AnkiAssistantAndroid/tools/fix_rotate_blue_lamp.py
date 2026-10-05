"""1) 侧栏头像又垫了一层主色 → 头像旁边/整块变蓝：去掉
   2) 我上次的脚本把"清缓存 + 加载头像"插进了 onRequestPermissionsResult 里（插错位置）：
      从那里移除，只在 onCreate 保留一次
   3) 状态灯：加载完头像后再点亮一次，确保旋转重建后不是灰的
"""
import io
import re
import sys

APP = r"D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
p = APP + r"\src\com\ankiassistant\MainActivity.java"
sys.stdout.reconfigure(encoding="utf-8")
s = io.open(p, encoding="utf-8").read()

# 1) 侧栏头像不要主色底衬
s = s.replace('''        android.graphics.drawable.GradientDrawable clip = Ui.round(Ui.ACCENT, 11);
        accountAvatar.setBackground(clip);
''', '')

# 2) 把误插到 onRequestPermissionsResult 里的那段删掉
misplaced = '''            if (rail != null) updateSyncLamp();
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
'''
if misplaced in s:
    s = s.replace(misplaced, '            if (rail != null) updateSyncLamp();\n', 1)
    print("误插的代码已从 onRequestPermissionsResult 移除")
else:
    print("!! 误插代码未匹配")

# 3) 头像加载完成后点亮状态灯
s = s.replace('''                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                        if (topAvatar != null) topAvatar.setImageBitmap(b);
                    }
                });''',
'''                Th.ui(new Runnable() {
                    @Override public void run() {
                        if (accountAvatar != null) accountAvatar.setImageBitmap(b);
                        if (topAvatar != null) topAvatar.setImageBitmap(b);
                        updateSyncLamp();   // 旋转重建后也确保灯是点亮的
                    }
                });''')
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("状态灯补点亮 + 侧栏底衬已移除")
