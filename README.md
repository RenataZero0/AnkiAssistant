# Anki 助手 AnkiAssistant

一个自用的 Anki 制卡助手：**输入一个词 → AI 按固定格式填好卡背 → 存进 Anki**，还能浏览本机已有的卡片。

同时提供 **Windows 桌面版** 和 **Android 版**，两端共用同一套界面设计与同一份卡片格式定义。

---

## 直接下载

> 🔒 **本仓库是公开的**，查更新与下载都不需要登录 ——
> 直接打开 [Releases 页面](https://github.com/RenataZero0/AnkiAssistant/releases/latest) 就能拿文件。

| 平台 | 文件 | 说明 |
|---|---|---|
| **Windows** | `AnkiAssistant-Setup.exe` | **安装程序**：向导、开始菜单、可在「设置 → 应用」里卸载 |
| **Android** | `AnkiAssistant.apk` | Android 5.0+（minSdk 21 / targetSdk 34），已签名，直接安装 |

> Windows **只发安装版，不做绿色版**（这是硬要求）。
> 装完就是系统里正常注册的应用，可以走「设置 → 应用」正常卸载；
> 自动更新也是下载**新的安装包**再运行它，不用管文件替换。

---

## 它做什么

- **AI 制卡**：输入单词，AI 按你选的服务商生成音标、词性、定义、关联公式、易混词，逐项可改
- **输出格式（config）可自定义**：字段清单、提示词都能自己定义，每个 config 生成自己的 Anki 笔记类型
- **浏览本机卡片**：牌组下拉、Anki 搜索语法、笔记列表、卡片详情（按真实模板渲染，含 MathJax）、删除
- **皮肤**：5 套（默认蓝 / 森林绿 / 暖阳橙 / 玫瑰紫 / 深色）
- **账号头像**：右上角头像角标；安卓端上传的头像会写进收藏库，**跟着同步到 AnkiWeb**，另一台设备同步后也能看到
- **更新日志 / 检查更新**：程序内查看更新日志（缓存优先 → 内置副本兜底），一键检查新版本
- **手机 / 平板自适应**（安卓端）：手机底部横栏切换页面，平板左侧常驻选择栏

> 需求原文见仓库根的 `PRMOPT.md`（第二节「Anki 协助」），本 README 描述的是**实现现状**。

---

## 目录结构

**本目录就是 Git 仓库的根**，只放这个软件的三端源码。

```
AnkiAssistant/                ← Git 仓库根，就是本目录
├─ README.md                   本文件
├─ CHANGELOG.md                ★更新日志的唯一真源（各子目录里的都是构建产物）
├─ GITHUB_SETUP.md             发版与 Release 流程
│
├─ AnkiAssistant/              Windows 桌面版
│   ├─ build.ps1               编译（只要 Windows 自带的 csc.exe）
│   ├─ src/                    C# 源码
│   ├─ assets/app.ico          应用图标（要提交）
│   └─ tools/make_icon.py      生成图标
│
├─ AnkiAssistantAndroid/       Android 版
│   ├─ build.ps1               编译 APK（aapt2 + javac + d8 + zipalign + apksigner，不需要 Gradle）
│   ├─ selftest.ps1            在电脑 JVM 上跑安卓端同一份纯逻辑代码做验证
│   ├─ AndroidManifest.xml
│   ├─ src/com/ankiassistant/  Java 源码
│   ├─ gen/                    protobuf(javalite) 生成的类（内置引擎要用）
│   ├─ assets/                 编辑器页面与数学公式资源、更新日志副本（打包进 APK）
│   ├─ res/                    启动图标 / 字符串 / 备份规则
│   └─ tools/                  构建与调试脚本（含 BUILD_ENGINE.md）
│
└─ AnkiAssistantSetup/         Windows 安装器（向导 + 卸载 + 注册表登记）
    ├─ build-setup.ps1         把桌面版 exe 打成安装包
    └─ src/                    C# 源码（WizardForm / Installer / Shortcuts）
```

> 编译产生的 `AnkiAssistant.exe` / `AnkiAssistant.apk` / `AnkiAssistant-Setup.exe`
> 都不入库 —— 需要时按下面的步骤重新编译。

> ⚠️ **构建脚本依赖目录层级，别再挪。**
> `AnkiAssistant/build.ps1` 与 `AnkiAssistantAndroid/build.ps1` 都从**本目录**取 `CHANGELOG.md`
> （Windows 版拷到 exe 旁边，安卓版拷进 `assets/`）；
> 安装器 `AnkiAssistantSetup/build-setup.ps1` 也从**本目录**取 `AnkiAssistant/` 与 `CHANGELOG.md`。

> ⚠️ **仓库路径必须是纯英文。** aapt2 是原生程序，不认非 ASCII 路径，
> 目录名带中文会直接报 `failed to open directory`。
> （桌面版和安装包用 .NET，能正确处理中文，只有安卓端有这个限制。）

---

## 重新编译

### Windows 版

只要 Windows 自带的 `csc.exe`（.NET Framework 4.x），**不需要 Visual Studio**：

```powershell
cd AnkiAssistant
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物：`AnkiAssistant\AnkiAssistant.exe`。编译成功后脚本会把仓库根的 `CHANGELOG.md`
拷到 exe 旁边当离线兜底（运行时优先用从 GitHub 拉到 `%APPDATA%\AnkiAssistant\CHANGELOG.md` 的那份）。

> 构建后**需要重新打包安装器**才能发布 —— Windows 只发安装版，exe 只是安装器的输入。

### 安装器

```powershell
cd AnkiAssistantSetup
powershell -ExecutionPolicy Bypass -File build-setup.ps1
```

产物：`AnkiAssistantSetup\AnkiAssistant-Setup.exe`。它会把 `AnkiAssistant\AnkiAssistant.exe`、
`assets\app.ico` 和仓库根的 `CHANGELOG.md`（以 `/resource:` 方式嵌成 `App.AnkiAssistant.exe` /
`App.app.ico` / `App.CHANGELOG.md`）打成一个自包含的 exe；版本号从 `AnkiAssistant\src\GitHub.cs`
的 `VersionTag` 解析，所以**安装包与程序不会各自漂移**。

若 `AnkiAssistant\AnkiAssistant.exe` 还不存在，这个脚本会先自动跑一次桌面版的 `build.ps1`。

安装器支持的命令行开关（`AnkiAssistantSetup\src\Program.cs`）：

| 开关 | 作用 |
|---|---|
| `--silent` / `/S` / `/silent` | 静默安装到默认目录（建桌面快捷方式、不开机自启） |
| `--uninstall` | 卸载（`--uninstall --silent` 为静默卸载，注册表里的 `QuietUninstallString` 就是它） |

### Android 版

需要 **JDK + Android SDK**（`build-tools` 与 `platform-34`）+ **NDK 编译出的内置引擎原生库**，
**不需要 Gradle，也不需要 Android Studio**：

```powershell
cd AnkiAssistantAndroid
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物：`AnkiAssistantAndroid\AnkiAssistant.apk`。

依赖的绝对路径写死在 `build.ps1` 开头，换机器改这几行即可：

```powershell
$SDK  = "D:\android-sdk"
$BT   = "$SDK\build-tools\36.0.0"
$AJAR = "$SDK\platforms\android-34\android.jar"
$JDK  = "D:\Program Files\Java\jdk-21"
```

原生库（Anki 官方 Rust 后端的 `librsdroid.so`）放在
`AnkiAssistantAndroid\tools\lib\jni\{arm64-v8a,x86_64}\`，**不入库**，按
`AnkiAssistantAndroid\tools\BUILD_ENGINE.md` 的方法自行编译；
缺哪个 ABI 就在那台设备上退回本机 AnkiDroid。protobuf 运行时是
`AnkiAssistantAndroid\tools\lib\protobuf-javalite.jar`。

想先验证纯逻辑再打包：

```powershell
powershell -ExecutionPolicy Bypass -File selftest.ps1
```

版本号只有一处真源 —— `AnkiAssistantAndroid\src\com\ankiassistant\Version.java` 的
`VERSION_TAG`，`build.ps1` 从它推导 manifest 的 `versionName / versionCode`，
打包后还会 `aapt2 dump badging` 复核一遍。

---

## 运行前置条件

### Windows 版：自带引擎，不需要装 Anki、也不需要插件

从 v1.17.0 起，Windows 版和安卓版走同一条路：把 **Anki 官方的 Rust 后端（rslib）编成
`rslib_aa.dll` 随安装包一起装**，在本程序自己的收藏库里写卡，再由它直接与 AnkiWeb 同步。

```
Anki 助手（内置引擎 rslib_aa.dll） → 自己的收藏库（数据目录\anki\） → 同步 → AnkiWeb 云端账号
```

所以：

- **不需要装 Anki 桌面端**，不需要 AnkiConnect 插件，也没有 `8765` 端口这回事
- **不需要开着别的程序**：引擎就在安装目录里（`rslib_aa.dll`，约 32 MB，
  `%LOCALAPPDATA%\Programs\AnkiAssistant\`）
- 本程序**有自己的收藏库**，不读写 Anki 桌面端的 `%APPDATA%\Anki2\`；
  想让手机 / 别的电脑上的卡片出现在这里，就在「设置 → 同步 → 登录 / 同步…」里登录 AnkiWeb，
  首次做一次**全量「下载」**（AnkiWeb 对全量下载有频率限制，一天几次）
- 引擎在同一份收藏库上只允许一个打开者：程序退出时会自己关闭收藏库；
  若看到「收藏库正被占用」，先确认没有另一个 AnkiAssistant 还在运行

### Android 版：自带引擎，不需要电脑

安卓版把 **Anki 官方的 Rust 后端（rslib/rsdroid）直接打进 APK**，在设备上自己维护一个收藏库，
并由它直接与 AnkiWeb 同步 —— **不走 AnkiConnect，也不需要装 AnkiDroid**：

```
Anki 助手（内置引擎） → 本机收藏库（filesDir/collection.anki2） → 同步 → AnkiWeb 云端账号
```

设置 → **AnkiWeb 同步**：填邮箱 + 密码 → 点「登录并同步」（密码只用于登录，不落盘，
登录后只保留后端签发的 hkey）。某台设备的 ABI 没有内置库时会退回**本机 AnkiDroid**（装了才生效）。

---

## AI 服务商

设置 → AI 自动填充，选服务商后填 API Key。Windows 版与安卓版是同一套预设：

| 预设 | 接口 |
|---|---|
| **智谱 GLM-4.5-Flash（免费）** ← 默认 | `open.bigmodel.cn/api/paas/v4/chat/completions` |
| DeepSeek | `api.deepseek.com/chat/completions` |
| 豆包（火山方舟） | `ark.cn-beijing.volces.com/api/v3/chat/completions` |
| 硅基流动 SiliconFlow | `api.siliconflow.cn/v1/chat/completions` |
| 自定义 | 任意 OpenAI 兼容的 `/chat/completions` |

**两端都内置了一把「智谱 GLM-4.5-Flash」的免费档 Key**（安卓 `Secret.java` 是 AES-GCM 密文，
Windows `AnkiAssistant\src\Secret.cs` 是 AES-128-CBC + HMAC-SHA256 密文，口令都分段存放），
所以新设备装完什么都不用填就能制卡；**设置里手填的 Key 优先于内置的那把**，随时可换、不用发版。
付费余额的 Key（如 DeepSeek）不打包。
每个服务商各存一把 Key，切换服务商时自动带出上次填的那把。

要换掉内置的那把（比如免费额度用完了）：
- 安卓：`java tools/MakeSecret.java "<新 Key>" "<新口令>"`，把输出贴回 `Secret.java`
- Windows：`AnkiAssistant\tools\MakeSecret.cs`（用法见文件头注释），把输出的 base64 贴回
  `Secret.cs` 的 `Blob`，再跑一次 `--selftest` 确认「内置 Key 封装能往返」通过

---

## 数据与配置放在哪

### Windows 版

| 内容 | 位置 |
|---|---|
| 设置（key=value），含每把 AI Key、皮肤、AnkiWeb 登录后的 hkey、「输出格式」的 configs.json | `%APPDATA%\AnkiAssistant\settings.ini` |
| 头像、本地草稿 | `%APPDATA%\AnkiAssistant\data\`（`avatar.png`、`drafts\*.json`） |
| 从 GitHub 拉下来的更新日志缓存 | `%APPDATA%\AnkiAssistant\CHANGELOG.md` |
| 离线兜底的更新日志 | exe 同目录的 `CHANGELOG.md`（`build.ps1` 拷进去的） |
| **内置引擎的收藏库**（卡片、笔记类型、媒体） | `%APPDATA%\AnkiAssistant\data\anki\`（`collection.anki2`、`collection.media\`、`collection.media.db2`） |
| 程序本体 + 引擎 | 每用户安装到 `%LOCALAPPDATA%\Programs\AnkiAssistant\`（含 `rslib_aa.dll`），注册表写在 HKCU，可在「设置 → 应用」卸载 |

设置放在 `%APPDATA%` 而不是 exe 旁边：安装版装在 `%LOCALAPPDATA%\Programs` 下，
而且用户以后也可能手动挪 exe。早期版本把 `data\` 放在 exe 旁边，启动时会跑一次
「只补缺、不覆盖」的迁移。

### Android 版

全部在**应用私有目录**（其它应用读不到）：

| 内容 | 位置 |
|---|---|
| 设置、草稿、AI Key | 应用私有 SharedPreferences |
| 内置引擎收藏库 | `filesDir/collection.anki2` |
| AnkiWeb 凭证 | 只存后端签发的 hkey（密码不落盘） |
| 头像 | 应用私有目录 + 写进收藏库（可同步到 AnkiWeb） |
| 崩溃栈 | 应用私有目录（界面上没有查看入口） |

---

## 更新日志

**真源只有仓库根的一份 `CHANGELOG.md`。** 各子目录里出现的都是**构建产物**：

| 出现的位置 | 谁生成的 |
|---|---|
| `AnkiAssistant\CHANGELOG.md` | Windows `build.ps1` 从仓库根拷到 exe 旁边 |
| `AnkiAssistantAndroid\assets\CHANGELOG.md` | 安卓 `build.ps1` 从仓库根拷进 assets，打包进 APK |
| 安装包里的 `App.CHANGELOG.md` | 安装器 `build-setup.ps1` 用 `/resource:` 嵌进去 |

运行时读取顺序：**联网拉 `raw.githubusercontent.com/.../main/CHANGELOG.md` 的缓存 → 内置副本兜底**，
两端一致。改日志只改仓库根那一份，别去改子目录里的副本。

「检查更新」的流程（两端一致，都读 `releases/latest`）：

- **Windows**：把新的 `AnkiAssistant-Setup.exe` 下到 `%TEMP%` 并启动它，由安装程序覆盖文件、重建快捷方式、更新注册表
- **Android**：把 APK 下到 `cache/update/` 后交给**系统安装器**（不做静默安装 —— 那需要 root 或设备管理员）

---

## 第三方组件与许可

### Android 版：内含 Anki 官方后端（AGPL-3.0）

| 组件 | 来源 | 许可 |
|---|---|---|
| `rslib` / `rsdroid` | <https://github.com/ankidroid/Anki-Android-Backend>（含 `ankitects/anki` 子模块） | **AGPL-3.0** |
| protobuf-javalite | `com.google.protobuf:protobuf-javalite` | BSD-3-Clause |
| MathJax | <https://www.mathjax.org> | Apache-2.0 |

APK 里的 `lib/*/librsdroid.so` 就是 Anki 官方 Rust 后端的原生库。
**AGPL-3.0 的义务**：分发包含它的 APK 时，必须一并提供对应源码与许可声明 ——
对应源码即上面那个仓库，版本与 `AnkiAssistantAndroid/tools/BUILD_ENGINE.md` 中记录的
commit / 工具链一致。是否把本项目整体改为 AGPL-3.0 由作者决定；
若不希望承担 AGPL 义务，可在设置里关掉「优先使用内置引擎」（改用本机 AnkiDroid），
并自行从 APK 中移除 `lib/*/librsdroid.so` 与 `gen/` 目录。

### Windows 版：同样内含 Anki 官方后端（AGPL-3.0）

从 v1.17.0 起，Windows 版把 Anki 官方 Rust 后端编成 `rslib_aa.dll` 一起分发，
因此**与安卓版承担同样的 AGPL-3.0 义务**：

| 组件 | 来源 | 许可 |
|---|---|---|
| `rslib`（Anki 官方 Rust 后端） | <https://github.com/ankidroid/Anki-Android-Backend>（含 `ankitects/anki` 子模块） | **AGPL-3.0** |
| `aa-ffi`（把它包成 C ABI 的薄壳，本仓库自写） | `AnkiAssistant\tools\BUILD_ENGINE_WINDOWS.md` 附录 A（含全部源码） | 随本项目 |
| protobuf / C# 侧编解码 | 手写，见 `AnkiAssistant\src\Pb.cs`、`Engine.cs` | 随本项目 |

对应源码：`rslib` 即上面那个仓库（commit 与
`AnkiAssistant\tools\BUILD_ENGINE_WINDOWS.md` 中记录的一致），把官方后端包成
`rslib_aa.dll` 的 `aa-ffi` crate 源码完整收录在该文档的附录 A，编译步骤见该文档第 1–9 节。
