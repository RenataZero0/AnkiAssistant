# Anki 助手 AnkiAssistant

一个自用的 Anki 制卡助手（Android 版）：**输入一个词 → AI 按固定格式填好卡背 → 存进 Anki**，还能浏览本机已有的卡片。

> 电脑端（Windows）版已**停止开发**，代码与安装包已从本仓库移除；历史版本仍可在
> [Releases 页面](https://github.com/RenataZero0/AnkiAssistant/releases) 看到。
> 本 README 只描述现在的 Android 版。

---

## 直接下载

> 🔒 **本仓库是公开的**，查更新与下载都不需要登录 ——
> 直接打开 [Releases 页面](https://github.com/RenataZero0/AnkiAssistant/releases/latest) 就能拿文件。

| 平台 | 文件 | 说明 |
|---|---|---|
| **Android** | `AnkiAssistant.apk` | Android 5.0（API 21）以上，已签名，直接安装 |

---

## 它做什么

- **AI 制卡**：输入单词，AI 按你选的服务商生成音标、词性、定义、关联公式、易混词，逐项可改
- **输出格式（config）可自定义**：字段清单、提示词都能自己定义，每个 config 生成自己的 Anki 笔记类型
- **浏览本机卡片**：牌组下拉、Anki 搜索语法、笔记列表、卡片详情（按真实模板渲染，含 MathJax）、删除
- **皮肤**：5 套（默认蓝 / 森林绿 / 暖阳橙 / 玫瑰紫 / 深色）
- **账号头像**：右上角头像角标；上传的头像会写进收藏库，**跟着同步到 AnkiWeb**，另一台设备同步后也能看到
- **更新日志 / 检查更新**：程序内查看更新日志（缓存优先 → 内置副本兜底），一键检查新版本
- **手机 / 平板自适应**：手机底部横栏切换页面，平板左侧常驻选择栏

> 需求原文见仓库根的 `PRMOPT.md`（第二节「Anki 协助」），本 README 描述的是**实现现状**。

---

## 目录结构

**本目录就是 Git 仓库的根**。

```
AnkiAssistant/                ← Git 仓库根，就是本目录
├─ README.md                   本文件
├─ CHANGELOG.md                ★更新日志的唯一真源（子目录里的都是构建产物）
├─ GITHUB_SETUP.md             发版与 Release 流程
├─ PRMOPT.md                   最初的原始需求
│
└─ AnkiAssistantAndroid/       Android 版（唯一在维护的版本）
    ├─ build.ps1               编译 APK（aapt2 + javac + d8 + zipalign + apksigner，不需要 Gradle）
    ├─ selftest.ps1            在电脑 JVM 上跑安卓端同一份纯逻辑代码做验证
    ├─ AndroidManifest.xml
    ├─ preview/                界面截图
    ├─ src/com/ankiassistant/  Java 源码
    ├─ gen/                    protobuf(javalite) 生成的类（内置引擎要用）
    ├─ assets/                 编辑器页面与数学公式资源、更新日志副本（打包进 APK）
    ├─ res/                    启动图标 / 字符串 / 备份规则
    └─ tools/                  构建与调试脚本（含 BUILD_ENGINE.md）
```

> 编译产生的 `AnkiAssistant.apk` 不入库 —— 需要时按下面的步骤重新编译。

> ⚠️ **构建脚本依赖目录层级，别再挪。**
> `AnkiAssistantAndroid/build.ps1` 从**本目录**取 `CHANGELOG.md` 拷进 `assets/`。

> ⚠️ **仓库路径必须是纯英文。** aapt2 是原生程序，不认非 ASCII 路径，
> 目录名带中文会直接报 `failed to open directory`。

---

## 重新编译

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

安卓 5.0（API 21）以上。安卓版把 **Anki 官方的 Rust 后端（rslib/rsdroid）直接打进 APK**，
在设备上自己维护一个收藏库，并由它直接与 AnkiWeb 同步 ——
**不走 AnkiConnect，也不需要装 Anki 电脑端、不需要装 AnkiDroid**：

```
Anki 助手（内置引擎） → 本机收藏库（filesDir/collection.anki2） → 同步 → AnkiWeb 云端账号
```

设置 → **AnkiWeb 同步**：填邮箱 + 密码 → 点「登录并同步」（密码只用于登录，不落盘，
登录后只保留后端签发的 hkey）。某台设备的 ABI 没有内置库时会退回**本机 AnkiDroid**（装了才生效）。

---

## AI 服务商

设置 → AI 自动填充，选服务商后填 API Key：

| 预设 | 接口 |
|---|---|
| **智谱 GLM-4.5-Flash（免费）** ← 默认 | `open.bigmodel.cn/api/paas/v4/chat/completions` |
| DeepSeek | `api.deepseek.com/chat/completions` |
| 豆包（火山方舟） | `ark.cn-beijing.volces.com/api/v3/chat/completions` |
| 硅基流动 SiliconFlow | `api.siliconflow.cn/v1/chat/completions` |
| 自定义 | 任意 OpenAI 兼容的 `/chat/completions` |

**内置了一把「智谱 GLM-4.5-Flash」的免费档 Key**（安卓 `Secret.java` 是 AES-GCM 密文，
口令分段存放），所以新设备装完什么都不用填就能制卡；**设置里手填的 Key 优先于内置的那把**，随时可换、不用发版。
付费余额的 Key（如 DeepSeek）不打包。
每个服务商各存一把 Key，切换服务商时自动带出上次填的那把。

要换掉内置的那把（比如免费额度用完了）：`java tools/MakeSecret.java "<新 Key>" "<新口令>"`，
把输出贴回 `Secret.java`。

---

## 数据与配置放在哪

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

**真源只有仓库根的一份 `CHANGELOG.md`。** 子目录里出现的是**构建产物**：
`AnkiAssistantAndroid\assets\CHANGELOG.md` 由安卓 `build.ps1` 从仓库根拷进 assets，打包进 APK。

运行时读取顺序：**联网拉 `raw.githubusercontent.com/.../main/CHANGELOG.md` 的缓存 → 内置副本兜底**。
改日志只改仓库根那一份，别去改子目录里的副本。

「检查更新」读 `releases/latest`，把 APK 下到 `cache/update/` 后交给**系统安装器**
（不做静默安装 —— 那需要 root 或设备管理员）。

---

## 第三方组件与许可

### 内含 Anki 官方后端（AGPL-3.0）

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
