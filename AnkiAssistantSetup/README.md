# Anki 助手 · Windows 安装程序

把「Anki 助手 AnkiAssistant」装进 Windows 的安装包源码。

**成品：`AnkiAssistant-Setup.exe`（约 377 KB，主程序 / 图标 / 更新日志全部内嵌，单文件）**

---

## 它做什么

| 功能 | 说明 |
|---|---|
| **三步安装向导** | 欢迎 → 安装位置 → 安装选项 |
| **开始菜单快捷方式** | 必定创建，名字叫「Anki 助手」 |
| **桌面快捷方式** | 可选（第 3 步勾选） |
| **开机自启** | 可选，注册到 `HKCU\...\Run`，值是 `"程序路径" --tray`（后台待命，随叫随到） |
| **出现在「应用和功能」** | 显示名称、版本、发布者、大小，带卸载按钮 |
| **卸载程序** | 可选是否一并删除设置与缓存 |
| **静默安装** | `AnkiAssistant-Setup.exe --silent` |

**每用户安装，不需要管理员权限，全程不弹 UAC。**

```
%LOCALAPPDATA%\Programs\AnkiAssistant\     程序文件
                                           AnkiAssistant.exe / 卸载 Anki 助手.exe / CHANGELOG.md
%APPDATA%\AnkiAssistant\                   设置与缓存（由程序自己写）
```

程序的部分设置写在 exe 同目录的 `settings.ini`，其余放在 `%APPDATA%\AnkiAssistant`。
安装程序只创建 `%APPDATA%\AnkiAssistant` 这个目录、**不往里写任何东西**，
覆盖升级也不会碰这两个位置，所以升级不会丢设置和缓存。

---

## 目录结构

```
AnkiAssistantSetup\
├─ AnkiAssistant-Setup.exe   成品（单文件安装包）
├─ build-setup.ps1            编译
├─ src\
│   ├─ Program.cs             入口：向导 / --silent / --uninstall / --shot
│   ├─ WizardForm.cs          安装向导界面 + 卸载流程
│   ├─ Installer.cs           安装与卸载的实际动作
│   └─ Shortcuts.cs           快捷方式（WScript.Shell）与内嵌资源读取
└─ preview\                   向导各页截图（由 --shot 生成）
```

---

## 重新编译

```powershell
powershell -ExecutionPolicy Bypass -File build-setup.ps1
```

它需要先编译好主程序（`..\AnkiAssistant\AnkiAssistant.exe`，用应用自己的
`..\AnkiAssistant\build.ps1` 生成）。找不到主程序时，`build-setup.ps1` 会先替你把
那个脚本跑一遍；还是不行就直接报错，让你先把应用编出来。然后：

1. 从 `..\AnkiAssistant\src\GitHub.cs` 的 `VersionTag` 读出**版本号**
   （这样安装包和程序版本永远不会对不上）
2. 生成 `build\SetupInfo.cs`
3. 用 `csc` 编译，并把主程序、`assets\app.ico`、仓库根的 `CHANGELOG.md` 作为**资源**打进去
   （资源名依次是 `App.AnkiAssistant.exe`、`App.app.ico`、`App.CHANGELOG.md`）

> 注意：`/resource:` 的参数里有逗号，PowerShell 会把它当数组分隔符，
> 所以整个参数要加引号写成 `"/resource:$path,Name"`。

与「学习助手」安装包的区别：AnkiAssistant 不装数据文件
（那边要装 `Schedule.xlsx` / `books.tsv` / `pages.tsv`），所以安装包里只有主程序、
图标和更新日志，安装进度仍然是 10 / 40 / 55 / 75 / 90 / 100 分段汇报。

---

## 命令行参数

| 参数 | 作用 |
|---|---|
| （无） | 打开安装向导 |
| `--silent` | 静默安装到默认位置（建桌面快捷方式，不开机自启） |
| `--uninstall` | 卸载（从安装目录里的「卸载 Anki 助手.exe」调用） |
| `--uninstall --silent` | 静默卸载，保留设置与缓存 |
| `--shot <png> [--page N]` | **把向导第 N 页渲染成图片**（离屏，不弹窗、不安装）—— 用来验证界面 |

`--page` 从 1 开始：1 = 欢迎，2 = 安装位置，3 = 安装选项，4 = 安装中，5 = 完成。
截图页只**伪造**进度，不会真的执行安装。

`--shot` 是照着主程序的做法加的：把窗口移到屏幕外再 `DrawToBitmap`，
这样改界面时不用真的弹一个安装向导出来。

---

## 卸载时发生了什么

1. 结束正在运行的 `AnkiAssistant.exe`
2. 删除开始菜单与桌面快捷方式
3. 删除 `HKCU\...\Uninstall\AnkiAssistant` 注册表项
4. 删除安装目录里的文件（卸载程序自己除外）
5. **询问**是否删除 `%APPDATA%\AnkiAssistant`（设置与缓存）
6. 卸载程序删不掉自己，所以写一个批处理延迟自删

> 第 6 步的批处理（`%TEMP%\aa-uninstall.bat`）必须用 `Encoding.Default`（系统 ANSI）写。
> 曾经用 UTF-8 写过，结果 **cmd.exe 按 ANSI 解码，中文路径变乱码，自删失败**，
> 卸载完会在目录里留一个卸载程序。这个坑已经踩过并修好了。
