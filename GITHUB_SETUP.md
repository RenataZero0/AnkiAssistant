# 发版流程（GitHub Release）

> 这份文档讲的是**怎么把新版本发出去**。程序里的「检查更新」会匿名读本仓库的最新 Release，
> 所以发版时**必须**建 Release 并把文件传上去 —— 只 push 代码，旧版本是看不到更新的。

仓库：<https://github.com/RenataZero0/AnkiAssistant>（公开，查更新与下载都不需要令牌）

> 电脑端（Windows）版已停止开发并已从本仓库移除；本流程只针对 Android 版。

---

## 一、版本号写在哪

| 平台 | 文件 | 常量 | 当前值 |
|---|---|---|---|
| Android | `AnkiAssistantAndroid/src/com/ankiassistant/Version.java` | `VERSION_TAG` / `VERSION_NUMBER` | `v1.17.1` / `1.17.1` |

- 安卓版的版本号由 `Version.java` 推导，`build.ps1` 会把它写进 manifest 的 `versionName / versionCode`
- Release 的 tag 必须与 `VERSION_TAG` 完全一致
- 版本比较是**逐段数值比较**（`2.1.10 > 2.1.9` 不会被判错），不是字符串比较

---

## 二、每次迭代按这个顺序做

### 1. 改 `CHANGELOG.md`（仓库根，唯一真源）

在**顶部**新增一节：

```markdown
## vX.Y.Z · YYYY-MM-DD —— 一句话标题

### 新增 / 调整 / 修
- ...
```

> `AnkiAssistantAndroid/assets/CHANGELOG.md` 是**构建脚本拷过去的副本**，不要手改。

### 2. 改版本号

- `AnkiAssistantAndroid/src/com/ankiassistant/Version.java` → `VERSION_TAG` / `VERSION_NUMBER`

### 3. 编译

```powershell
cd AnkiAssistantAndroid
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物：`AnkiAssistantAndroid\AnkiAssistant.apk`

（打包前想先跑纯逻辑自检：`powershell -ExecutionPolicy Bypass -File .\selftest.ps1`）

### 4. commit & push

```powershell
cd ..
git add -A
git commit -m "vX.Y.Z：一句话"
git push
```

（仓库本地已配 `http.proxy=http://127.0.0.1:7890`，公司/校园网环境必需。）

### 5. 建 Release

在 <https://github.com/RenataZero0/AnkiAssistant/releases/new> 上：

| 字段 | 填什么 |
|---|---|
| **Choose a tag** | **必须与 `Version.java` 的 `VERSION_TAG` 完全一致**，如 `v1.17.1` |
| **Target** | `main` |
| **Release title** | `v1.17.1 —— 一句话标题` |
| **Describe this release** | 把 `CHANGELOG.md` 顶部那一节粘过来 |
| **Attach binaries** | `AnkiAssistantAndroid\AnkiAssistant.apk` |
| **Set as the latest release** | ✅ 勾上（`make_latest=true`） |

附件只有 `AnkiAssistant.apk` 一个 —— 安卓端的「下载并安装」直接下它。

> 勾上「Set as the latest release」很重要：程序读的是
> `api.github.com/repos/RenataZero0/AnkiAssistant/releases/latest`，
> 最新 Release 不是这一版的话，旧版本检查更新会什么都看不到。

### 6. 用命令行发（可选的等价做法）

```powershell
gh release create v1.17.1 `
  "AnkiAssistantAndroid\AnkiAssistant.apk" `
  --title "v1.17.1 —— 一句话标题" `
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
gh release view v1.17.1
```

- [ ] Release 的 tag = `Version.java` 的 `VERSION_TAG`
- [ ] `AnkiAssistant.apk` 在资产里，且名字与上面的表一致
- [ ] 这一版是 Latest
- [ ] 打开旧版本 App →「检查更新」→ 能看到新版本说明
- [ ] 安卓端点「下载并安装」能下到 APK

---

## 四、更新日志是怎么被读到的

| 平台 | 联网拉取 | 离线兜底 |
|---|---|---|
| Android | `raw.githubusercontent.com/RenataZero0/AnkiAssistant/main/CHANGELOG.md` → 写进应用私有目录 | APK 内置的 `assets/CHANGELOG.md` |

App 会在标题下写明来源（「来自 GitHub 最新版（缓存于 …）」或「来自应用内置副本（离线）」）。
如果打包进去的日志最新版本和程序当前版本对不上，设置页会黄字提醒 —— 那说明发版时漏改了版本号。

---

## 五、常见问题

**Q：`gh release create` 报 tag 已存在**
A：同名 tag 已经发过了。要么删掉旧 Release 与 tag 重发，要么把版本号再往上加一档。

**Q：旧版本检查更新没反应**
A：先确认这一版是 Latest；再确认 `Version.java` 里的 `VERSION_TAG` 真的改了并且**重编过**。
