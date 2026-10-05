# 发版流程（GitHub Release）

> 这份文档讲的是**怎么把新版本发出去**。程序里的「检查更新」会匿名读本仓库的最新 Release，
> 所以发版时**必须**建 Release 并把文件传上去 —— 只 push 代码，旧版本是看不到更新的。

仓库：<https://github.com/RenataZero0/AnkiAssistant>（公开，查更新与下载都不需要令牌）

---

## 一、版本号写在哪（两处，都要改）

| 平台 | 文件 | 常量 | 当前值 |
|---|---|---|---|
| Windows | `AnkiAssistant/src/GitHub.cs` | `public const string VersionTag = "..."` | `v1.15.8` |
| Android | `AnkiAssistantAndroid/src/com/ankiassistant/Version.java` | `VERSION_TAG` / `VERSION_NUMBER` | `v1.15.8` / `1.15.8` |

- Windows 版程序内显示的版本号来自 `GitHub.cs` 的 `VersionTag`，**它就是发版时要改的那一处**
- 安卓版的版本号由 `Version.java` 推导，`build.ps1` 会把它写进 manifest 的 `versionName / versionCode`
- 两个平台的版本号**保持一致**（同一个 `v1.15.8`），Release 的 tag 也必须与之完全一致
- 版本比较是**逐段数值比较**（`2.1.10 > 2.1.9` 不会被判错），不是字符串比较

> `VersionTag` 是 `const`，会被编译期内联，所以发版前必须**真的重编译**，
> 只改源码不重编，exe 里还是旧版本号。

---

## 二、每次迭代按这个顺序做

### 1. 改 `CHANGELOG.md`（仓库根，唯一真源）

在**顶部**新增一节：

```markdown
## vX.Y.Z · YYYY-MM-DD —— 一句话标题

### 新增 / 调整 / 修
- ...
```

> 各子目录里的 `AnkiAssistant/CHANGELOG.md`、`AnkiAssistantAndroid/assets/CHANGELOG.md`
> 都是**构建脚本拷过去的副本**，不要手改。

### 2. 改两处版本号

- `AnkiAssistant/src/GitHub.cs` → `VersionTag`
- `AnkiAssistantAndroid/src/com/ankiassistant/Version.java` → `VERSION_TAG` / `VERSION_NUMBER`

### 3. 编译两个平台

```powershell
cd AnkiAssistant
powershell -ExecutionPolicy Bypass -File build.ps1

cd ..\AnkiAssistantAndroid
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物：`AnkiAssistant\AnkiAssistant.exe`、`AnkiAssistantAndroid\AnkiAssistant.apk`

（打包前想先跑纯逻辑自检：`cd AnkiAssistantAndroid; powershell -ExecutionPolicy Bypass -File .\selftest.ps1`）

### 4. 打安装包（Windows 只发安装版）

```powershell
cd ..\AnkiAssistantSetup
powershell -ExecutionPolicy Bypass -File build-setup.ps1
```

产物：`AnkiAssistantSetup\AnkiAssistant-Setup.exe`

> ⚠️ **顺序不能反**：安装器读的是上一步刚编出来的 `AnkiAssistant\AnkiAssistant.exe`。
> 先打安装包再重编 exe，发出去的安装包里就是上一版。

### 5. commit & push

```powershell
cd ..
git add -A
git commit -m "vX.Y.Z：一句话"
git push
```

（仓库本地已配 `http.proxy=http://127.0.0.1:7890`，公司/校园网环境必需。）

### 6. 建 Release

在 <https://github.com/RenataZero0/AnkiAssistant/releases/new> 上：

| 字段 | 填什么 |
|---|---|
| **Choose a tag** | **必须与 `GitHub.cs` 的 `VersionTag` 完全一致**，如 `v1.15.8` |
| **Target** | `main` |
| **Release title** | `v1.15.8 —— 一句话标题` |
| **Describe this release** | 把 `CHANGELOG.md` 顶部那一节粘过来 |
| **Attach binaries** | `AnkiAssistantSetup\AnkiAssistant-Setup.exe` 与 `AnkiAssistantAndroid\AnkiAssistant.apk` |
| **Set as the latest release** | ✅ 勾上（`make_latest=true`） |

**两个附件都必须传**：

- `AnkiAssistant-Setup.exe` —— 程序里的自动更新要找的就是这个文件名
  （`GitHub.cs` 的 `SetupAsset`）；找不到安装包时才会回退去挑 `AnkiAssistant.exe`
- `AnkiAssistant.apk` —— 安卓端的「下载并安装」直接下它

> 勾上「Set as the latest release」很重要：两端都是读
> `api.github.com/repos/RenataZero0/AnkiAssistant/releases/latest`，
> 最新 Release 不是这一版的话，旧版本检查更新会什么都看不到。

### 7. 用命令行发（可选的等价做法）

```powershell
gh release create v1.15.8 `
  "AnkiAssistantSetup\AnkiAssistant-Setup.exe" `
  "AnkiAssistantAndroid\AnkiAssistant.apk" `
  --title "v1.15.8 —— 一句话标题" `
  --notes-file _release_notes.md `
  --target main `
  --latest
```

`_release_notes.md`（仓库根，不入库）就是给 `--notes-file` 用的简短发布说明，
内容从 `CHANGELOG.md` 最新一节归纳。

---

## 三、发完自己验一遍

```powershell
# Release 里的 tag 和资产名对不对
gh release view v1.15.8
```

- [ ] Release 的 tag = `GitHub.cs` 的 `VersionTag`
- [ ] 两个资产都在，且名字与上面的表一致
- [ ] 这一版是 Latest
- [ ] 打开旧版本程序 →「检查更新」→ 能看到新版本说明
- [ ] 安卓端点「下载并安装」能下到 APK

---

## 四、更新日志是怎么被读到的

| 平台 | 联网拉取 | 离线兜底 |
|---|---|---|
| Windows | `raw.githubusercontent.com/RenataZero0/AnkiAssistant/main/CHANGELOG.md` → 写进 `%APPDATA%\AnkiAssistant\CHANGELOG.md` | exe 同目录的 `CHANGELOG.md`（`build.ps1` 拷的） |
| Android | 同一路径 → 写进应用私有目录 | APK 内置的 `assets/CHANGELOG.md` |

两端都会在标题下写明来源（「来自 GitHub 最新版（缓存于 …）」或「来自应用内置副本（离线）」）。
如果打包进去的日志最新版本和程序当前版本对不上，设置页会黄字提醒 —— 那说明发版时漏改了版本号。

---

## 五、常见问题

**Q：`gh release create` 报 tag 已存在**
A：同名 tag 已经发过了。要么删掉旧 Release 与 tag 重发，要么把版本号再往上加一档。

**Q：旧版本检查更新没反应**
A：先确认这一版是 Latest；再确认 `GitHub.cs` 里的 `VersionTag` 真的改了并且**重编译过**。

**Q：Windows 的自动更新是怎么装的？**
A：把新的 `AnkiAssistant-Setup.exe` 下到 %TEMP% 再启动它，由安装程序覆盖文件、
重建快捷方式、更新注册表 —— 所以 Windows 只发安装版，没有绿色版要替换自己。

**Q：能不能只发安卓端？**
A：可以。单独发某个平台的 Release 时，另一端会看到「有更新」但下不到自己那个附件 ——
所以只要是两端都有改动，就一起发。
