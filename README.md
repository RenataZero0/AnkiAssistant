# Anki 助手 · Android 版

对着 `PRMOPT.md` 第二节「Anki 协助」做的安卓应用：**输入一个词 → AI 按固定格式填好卡背 → 存进 Anki → 自动同步到 AnkiWeb 云端**，并且可以在应用里浏览自己已有的卡片。

**成品：`AnkiAssistant.apk`（约 700 KB，minSdk 21 / targetSdk 34，已签名，可直接安装）**

- 手机：底部横栏切换「制卡 / 浏览 / 设置」
- 平板（最短边 ≥ 600dp）：左侧常驻选择栏（100dp）。侧栏做成分层结构，不再是一整条死白：
  顶部是圆角应用标识，中间是三个导航项（上下等权重留白，垂直居中），底部是**Anki 连接状态灯**
  （绿=连得上 / 红=连不上 / 黄=检测中；点一下重新检测，启动时自动测一次，保存设置后也会复测）；
  底色是极淡的横向渐变，与内容区分层
- 顶栏标题与内容卡片左边缘对齐
- 制卡页只有两块：单词卡片 + **编辑器卡片**。牌组 / 标签 / 「从 Anki 选择」/ 保存按钮都在**编辑区内部**（`assets/editor.html` 的 `#metaPane`），跟着字段一起滚动，不占固定面板；
  这些控件通过 `editor.addJavascriptInterface(bridge, "Android")` 回调 Java：`Android.pickDeck()` / `Android.saveNote(deck,tags)` / `Android.saveDraft(deck,tags)` / `Android.tagForDeck(deck)`
- 设置页的勾选框是"方块 + 独立文字"的一行（不再用 `CheckBox.setText`），整行 `Gravity.CENTER_VERTICAL`：
  方块和文字都在行内垂直居中，两者中心必然重合（多行文字也一样），不会再出现"方块跑到两行中间"
- 编辑区顶部的「编辑/预览」+ 工具栏是**同一个** `#topSticky` sticky 容器（各自 `position: sticky` + 硬编码 `top:48px` 会因 tab 实际高度不同而错位、滚动时互相追着重排 → 抖动）；容器再套一层 `translateZ(0)` 提升为合成层
- 预览页只有「显示答案」一个按钮（夜间模式已按用户要求移除）；字段为空时显示"还没有输入单词 / 还没有内容"的空状态提示
- 编辑区的灰色占位文字全部是**格式说明**（如 `英 /…/；美 /…/`），不放任何假数据（早先那版写了假的音标，会误导人）
- 牌组 → 标签自动映射：四个数学牌组 → `ALevel::Maths`，`A Level Physics` → `ALevel::Physics`，其它牌组不动标签
- 公式：内置 MathJax 3.2.2（离线，不用联网），预览与写入 Anki 的内容都是 Anki 标准 `\( ... \)` 写法

---

## 一、需求对照（PRMOPT.md 第二节第 2 条）

| 要求 | 实现 |
|---|---|
| (1) 适配平板、手机端 | 见上；同一份代码按最短边 600dp 自动切布局，转屏不重建 Activity（编辑到一半的卡片不会丢） |
| (2) 调用 Anki API 自动保存到云端账号 | AnkiConnect：`addNote` 写入 + `sync()` 推到 AnkiWeb，见下文「为什么是 AnkiConnect」 |
| (3) 可以查看我的 Anki 内容 | 浏览页：牌组下拉、Anki 搜索语法、笔记列表、卡片详情（正面/背面按真实模板渲染，含 MathJax）、删除、`guiEditNote` 在电脑上打开 |
| (4) 主界面输入 + Anki 级输入/预览 | 见「编辑器能力」；单词输入即卡片正面，AI 填充后逐项可改 |
| (5) 手机底栏 / 平板左侧选择框 | 见上 |
| (6) 复用 StudyCompanion 的开发经验 | 见「踩过的坑（务必先读）」—— 这些都是这次真机调试中真实撞到并修掉的 |

### 编辑器能力（第 4 条的「Anki 的所有功能」）

编辑区是一个 `contenteditable` 页面（`assets/editor.html`），工具栏与 Anki 桌面端一致：

- 加粗 / 斜体 / 下划线 / 删除线 / 文字颜色 / 荧光高亮 / 清除格式
- 项目符号 / 编号列表 / 引用 / 行内代码
- 挖空 `{{c1::...}}`（预览里高亮显示）
- 行内公式 `\( ... \)`、独立公式 `\[ ... \]`
- 撤销 / 重做；每个字段可切 **HTML 源码** 模式直接改 HTML
- 预览页用**真正的卡片模板**渲染（模板/CSS 就是写进 Anki 笔记类型的那一份），支持「显示答案 / 隐藏答案」与夜间模式
- 公式渲染用内置 MathJax 3.2.2（与 Anki 相同的定界符），离线可用

---

## 二、卡片格式

字段（写进 Anki 的笔记类型「专业术语卡」）：

```
单词 | 音标 | 词性 | 定义 | 关联公式/符号 | 易混 | 中文
```

正面 = `{{单词}}`；背面按 `PRMOPT.md` 第二节第 3 条的样式渲染，空字段连标签一起隐藏：

```
velocity
────────────────
【音标】英 /vəˈlɒsəti/；美 /vəˈlɑːsəti/
【词性】n
【定义】(n) the rate of change of displacement with respect to time; a vector quantity
【关联公式/符号】 \(\vec{v}=\dfrac{\Delta \vec{s}}{\Delta t}\)
【易混】speed /spiːd/ n. 速率，标量，无方向
        acceleration /əkˌseləˈreɪʃn/ n. 加速度，速度的变化率
【中文】速度（矢量）
```

**格式按你 Anki 里已有的卡片对齐**（我读了本机 `Anki2\账户 1\collection.anki2` 里那 9 张卡）：

- 公式**只用行内** `\( ... \)`，不用 `\[ ... \]` —— 你现有卡片（如 `\(ax^2+bx+c=0,\ a\neq0\)`）都是这个写法，独立公式在预览里会占掉整整一行
- 【音标】写 **英 /…/；美 /…/**
- 【词性】用 `n`、`adj/n`（不加句点）
- 【定义】用 `(n) ...; (adj) ...`（括号里不带句点）
- 【易混】每行写成 `词 /音标/ 词性缩写 中文释义`（词性用英文缩写，不用破折号连接），例：`speed /spiːd/ n. 速率，标量，无方向`
- 【中文】是参考你现有卡片补的一行，留空则不显示
- 【中文】是参考你现有卡片补的一行，留空则不显示

词性/释义/公式/易混/中文全部由 AI 按提示词生成，生成后可逐项手改。

---

## 三、Anki 连接配置（电脑上只做一次）

**为什么是 AnkiConnect**：AnkiWeb 本身**没有公开的「往云端账号写卡片」API**（官方只提供桌面端与同步协议）。
官方认可的写入入口是桌面版 Anki 的 **AnkiConnect** 插件（插件 ID `2055492159`）。所以链路是：

```
Anki 助手  →  (局域网 Wi-Fi)  桌面版 Anki + AnkiConnect  →  sync()  →  AnkiWeb 云端账号
```

### 本机已经配置好了（2026-10-03）

这台电脑上已经完成：插件装在 `%APPDATA%\Anki2\addons21\2055492159`，配置里
`webBindAddress = "0.0.0.0"`、端口 8765，Windows 防火墙已放行 Anki 的入站连接，
App 里的 IP 填的是 `192.168.71.112`，实测「测试连接 → 连接成功 ✓ AnkiConnect v6，共 4 个牌组」。
**你只需要保证：用的时候电脑上的 Anki 开着，平板和电脑连同一个 Wi-Fi。**

### 换一台电脑时怎么做

1. 电脑打开 Anki → 工具 → 插件 → **获取插件…** → 输入 `2055492159` → 确定 → 重启 Anki
   （这一步是 Anki 自己从 AnkiWeb 下载，最省事）
2. 插件列表里选中 AnkiConnect → **插件设置**，把 `webBindAddress` 改成 `"0.0.0.0"`（允许局域网访问），
   保存后重启 Anki；想加密码就在同一份配置里加 `"apiKey": "一串字符"`（留 `null` 就是不校验，
   同一 Wi-Fi 下谁都能控制你的 Anki，介意就加上）
3. 第一次启动时 Windows 会弹「Windows 安全中心：是否允许公共网络和专用网络访问此应用？」
   → 点 **允许**（不点就是这次实测到的现象：本机 127.0.0.1 能连、平板连过去超时）
4. 电脑上 `Win+R` → `ipconfig` 看 IPv4 地址（本机是 `192.168.71.112`），填进 App 的
   「设置 → Anki 连接 → 电脑的 IP 或主机名」
5. 点「测试连接」：第一次会在电脑上的 Anki 弹出允许提示，点允许即可
6. 之后每次保存都会调用 `sync()` 把卡片推到 AnkiWeb 云端；也可以在浏览页点「↻ 同步到云端」手动同步

牌组与笔记类型**不用手工建**：应用会先 `createDeck` 建牌组，再 `modelNames` 检查「专业术语卡」是否存在，
不存在则用 `createModel` 按上面的字段/模板/CSS 建好。

> 没有 Wi-Fi、只有数据线时：`adb reverse tcp:8765 tcp:8765` 之后把 App 的 IP 填成 `127.0.0.1` 也能用
> （等价于把平板的 8765 转发到电脑），但拔线就断了。

### 断网怎么办

Anki 连不上时，保存会**自动转存到「浏览 → 本地草稿」**，卡片不会丢；联网后在草稿页点「发送」或「全部发送」补发，也可以「导出」成 TSV 再到 Anki 里导入。

---

## 四、AI 配置

设置 → AI 自动填充，选服务商后填 API Key：

| 预设 | 接口 | 备注 |
|---|---|---|
| DeepSeek | `api.deepseek.com/chat/completions` | 便宜，效果好 |
| 豆包（火山方舟） | `ark.cn-beijing.volces.com/api/v3/chat/completions` | 模型名按方舟控制台里的 ID 填 |
| 智谱 GLM-4.5-Flash | `open.bigmodel.cn/api/paas/v4/chat/completions` | 该模型**免费**（[官方免费模型列表](https://docs.bigmodel.cn/cn/guide/models/free/glm-4.5-flash)），注册即用。实测质量明显好于 glm-4-flash（音标/释义更准，公式会带 `\(...\)` 定界符） |
| 硅基流动 SiliconFlow | `api.siliconflow.cn/v1/chat/completions` | 有免费模型 |
| 自定义 | 任意 OpenAI 兼容 `/chat/completions` | 本地 mock、其他厂商都走这里 |

**APK 里不内置任何 API Key** —— 打包进 APK 的密钥任何人都能提取盗用。上面几个都是一注册就有免费额度/免费模型。
**每个服务商各存一把 Key**：切换服务商时会自动带出该服务商上次填的 Key，不用反复粘贴（旧版本的单一 Key 会自动迁移到当前服务商名下）。

### 思考模式（智谱 glm-4.5/4.7）

思考型模型会先输出一大段推理（`reasoning_content`），这段**也算输出 token**。实测 glm-4.5-flash 生成同一张卡：

| 模式 | 耗时 | 思考内容 | 输出 JSON |
|---|---|---|---|
| 开思考（默认参数） | 23s | 1279 字 | **被截断**（推理吃掉输出预算）→ 需要重试 |
| 关思考（`thinking={"type":"disabled"}`） | 4.5s | 无 | 一次成型 |

所以**默认关闭思考**；想要看推理过程，到「设置 → AI 自动填充 → 让思考型模型先思考」勾上即可，
思考内容会显示在制卡页底部的「AI 思考过程（N 字）」折叠面板里（默认只有一行，点开是限高滚动区）。

不管开不开思考，解析失败时 App 都会**自动重试一次**（改用严格提示词并强制关思考），
再失败才降级成"原样放进定义"。

**正确率实测（同一套提示词、每模式 5 个词，用 `tools/thinking_accuracy.py` 跑）**

| | 关思考 | 开思考 |
|---|---|---|
| 平均耗时 | **5.9s** | 35.0s |
| 平均 tokens | **484** | 1198（思考 1511 字） |
| JSON 可解析 | 5/5 | 5/5 |
| 音标英+美 / 词性 / 易混格式 | 5/5 · 5/5 · 5/5 | 5/5 · 5/5 · 5/5 |

**结论：关掉思考没有可测的正确率损失**（内容抽查两边都对），但省 6 倍时间和 2.5 倍 token。
个别差异是双向的：开思考偶尔会把词条自己列进【易混】、多加冗余义项；关思考偶尔【易混】里多空行、
公式忘了写定界符（App 会自动补）。

之前试过 `text.pollinations.ai`（免密钥），实测现在返回 `402 Payment Required`，已经不是免费服务，所以去掉了这个预设。

---

## 五、项目结构

```
AnkiAssistant\
├─ AnkiAssistant.apk          成品（已签名，可直接安装）
├─ CHANGELOG.md               ★更新日志的唯一来源；build.ps1 会拷进 assets\CHANGELOG.md 打包进 APK
├─ build.ps1                  编译 APK（aapt2 + javac + d8 + zipalign + apksigner，不需要 Gradle）
├─ selftest.ps1               在电脑 JVM 上跑纯逻辑自检（126 项断言）
├─ debug.keystore             自签证书（覆盖安装请保留同一个）
├─ AndroidManifest.xml
├─ assets\
│   ├─ CHANGELOG.md           构建时从工程根目录拷来的副本（App 里离线查看用，不要手改）
│   ├─ editor.html            编辑器 + 预览（模板引擎、MathJax、cloze、HTML 源码模式）
│   └─ mj-*.js / mj-woff-*.woff   MathJax 3.2.2（**必须平铺在根目录**，见踩坑第 1 条）
├─ res\                       图标 / 字符串 / 备份规则
├─ src\com\ankiassistant\
│   ├─ MainActivity.java      顶栏、手机底栏 / 平板左栏（含 Anki 状态灯）、页面切换、转屏重排
│   ├─ CreateView.java        制卡页：单词 → AI 填充 → 富文本编辑 → 存 Anki / 存草稿
│   ├─ BrowseView.java        浏览页：牌组/搜索/笔记列表/卡片详情/删除/同步/本地草稿
│   ├─ SettingsView.java      设置：Anki 连接、AI、默认值、更新内容
│   ├─ Changelog.java         更新日志：GitHub 缓存优先 → assets 内置副本兜底 + 版本号比对
│   ├─ ChangelogView.java     更新日志阅读器（自绘 Markdown + 版本快捷跳转）
│   ├─ CardFormat.java        ★格式的唯一事实来源：提示词、AI 回复解析、字段组装、模板与 CSS
│   ├─ AnkiClient.java        AnkiConnect 客户端（纯 java.* + org.json，可自检）
│   ├─ AiClient.java          OpenAI 兼容客户端 + 预设（可自检）
│   ├─ AssetServer.java       仅监听 127.0.0.1 的静态资源服务器（给 WebView 供 assets）
│   ├─ Store.java             设置与本地草稿（SharedPreferences）
│   ├─ Ui.java / IconDrawable.java   配色与控件样式 / Canvas 画的图标
│   ├─ Th.java / CrashHandler.java   线程小工具 / 崩溃捕获（崩溃栈写进应用私有目录，界面上没有查看入口）
└─ tools\
    ├─ SelfTest.java          JVM 自检（格式、提示词解析、请求构造、响应解析）
    ├─ mock_servers.py        联调用：假 AnkiConnect(8765) + 假 AI(8899)
    ├─ seed_mock_note.py      往假 AnkiConnect 里塞测试卡片
    ├─ inspect_anki_collection.py  只读解析本机 Anki 收藏库（看已有卡片的写法）
    ├─ prepare_assets.py      MathJax 资源扁平化（打包前跑，见踩坑第 1 条）
    ├─ make_icons.py          生成启动图标
    ├─ check_html_js.js       校验 editor.html 内联 JS 语法
    └─ lib\
        ├─ json.jar           org.json（电脑端自检用，安卓端由 android.jar 提供）
        └─ py\zstandard       读新版 Anki 收藏库（schema 18，字段是 zstd 压缩）用，可选
```

---

## 六、重新编译 / 自检

```powershell
cd AnkiAssistant
powershell -ExecutionPolicy Bypass -File selftest.ps1     # 先跑纯逻辑自检
powershell -ExecutionPolicy Bypass -File build.ps1        # 再打包
```

依赖的绝对路径写在 `build.ps1` 开头（`D:\android-sdk`、`D:\Program Files\Java\jdk-21`），换机器改那两行。

### 仓库与自动更新

- 本工程是**独立仓库**：<https://github.com/RenataZero0/AnkiAssistant>
  （原先把 Anki 助手放在 StudyCompanion 仓库里，v1.5.0 起拆成独立仓库，两边互不干扰）
- App 内「设置 → 更新内容 → 检查更新」会匿名读该仓库的**最新 Release**，
  与本机版本做数值比较（`Updater.compareVersion`：2.1.10 > 2.1.9 这种不会判错），
  有新版本就弹出说明，点「下载并安装」把 APK 下到 `cache/update/` 并交给系统安装器
- 仓库地址写在 `Updater.OWNER / Updater.REPO` 两行；仓库是公开的，**查更新与下载都不需要令牌**
- 只做「检查 + 下载 + 交给系统安装器」，不做静默安装（那需要 root 或设备管理员）

**发版流程（每次迭代）**

1. 改 `CHANGELOG.md` 顶部加一节 `## vX.Y.Z · 日期 —— 标题`
2. 改 `src\com\ankiassistant\Version.java` 的 `VERSION_TAG` / `VERSION_NUMBER`
3. `build.ps1` 打包出 `AnkiAssistant.apk`（版本号自动同步到 manifest）
4. `git commit && git push`（仓库本地已配 `http.proxy=http://127.0.0.1:7890`，公司/校园网环境必需）
5. 在 GitHub 建 **Release，tag 必须与 `VERSION_TAG` 完全一致**（如 `v1.5.0`），
   并把 `AnkiAssistant.apk` 作为附件传上去 —— 旧版本才能检查到这次更新
   （命令行做法见 `tools/` 之外的记录，或直接用网页界面拖拽）
### 版本号与更新日志（每次迭代都这么做）

1. 在 `CHANGELOG.md` **顶部**新增一节：`## vX.Y.Z · 日期 —— 一句话标题`，下面用 `### 新增 / 调整 / 修` 分类
2. 把 `src\com\ankiassistant\Version.java` 的 `VERSION_TAG` 与 `VERSION_NUMBER` 加一档
3. `build.ps1` 会自动把 `CHANGELOG.md` 拷进 `assets\CHANGELOG.md` 再打包（内置副本，保证离线也有东西看）
4. 打开「设置 → 更新内容 → 查看更新内容」时会**先联网拉一次最新日志**
   （`raw.githubusercontent.com/.../main/CHANGELOG.md`，匿名可读、不需要令牌），成功就写进应用私有目录当缓存；
   拉不到就用缓存 / 内置副本，并在标题下写明来源：
   「来自 GitHub 最新版（缓存于 MM-dd HH:mm）」或「来自应用内置副本（离线）· 在线更新失败：<原因>」
   —— 与 StudyCompanion 的更新日志读取方式**完全一致**（缓存优先 → 内置副本兜底）
4. 版本号只有一处：`Version.java`。`build.ps1` 从它推导 manifest 的 `versionName/versionCode`，
   不会出现"APK 显示 1.0、代码里是别的版本"这种漂移
5. 如果打包的日志里最新版本和运行的版本不一致，设置页会主动黄字提醒（构建时忘了改版本号）

自检覆盖：卡片字段与模板、`{{#字段}}` 条件块成对、提示词里的格式约束（行内公式 / 英美音标 / 中文行、**易混的英文词性**）、
**提示词里那段"输出示例"本身必须是合法 JSON**（实测模型会照着坏示例写）、
AI 回复的各种脏数据（```json 围栏、前后废话、中文键名、非 JSON、**全角冒号 / 键后丢引号的坏 JSON**、语法全坏时按字段名硬抠）、
公式兜底（裸 LaTeX 自动补 `\(...\)`、`$...$` 统一成 `\(...\)`）、
牌组→标签映射、思考模式参数、重试提示词、字段转义与换行处理、
AnkiConnect 请求构造、响应 `error` 处理、地址归一化、AI 请求体与响应解析。

### 真机联调（不装 Anki、不花 API 额度）

```powershell
python tools\mock_servers.py                 # 后台跑两个假服务
adb reverse tcp:8765 tcp:8765                # 让手机把 127.0.0.1:8765 转发到电脑
adb reverse tcp:8899 tcp:8899
python tools\seed_mock_note.py               # 塞两张测试卡片
```

然后在应用里：设置 → AI 选「自定义」→ 接口地址填 `http://127.0.0.1:8899/chat/completions`，
Anki 的 IP 留空（默认就是 `127.0.0.1:8765`）。所有请求都会记到 `tools/mock_log.txt`，
可以直接核对 App 发出的 `createModel` / `addNote` 报文。

---

## 七、踩过的坑（改代码前务必先读）

1. **Windows 上 aapt2 会把嵌套 assets 写成反斜杠路径。**
   APK 里真实存在的是 `assets/mathjax\tex-mml-chtml.js`，`AssetManager` 把 `\` 当成普通文件名字符，
   于是 `assets.open("mathjax/tex-mml-chtml.js")` 抛 `FileNotFoundException`、`list("mathjax")` 返回空。
   现象：页面能渲染，但所有相对路径的脚本/字体 404（MathJax 永远不生效）。
   → 所有资源**平铺在 assets 根目录**（`tools/prepare_assets.py` 生成 `mj-*`），URL 路径映射写在 `AssetServer.mapAsset()`。
   定位手段：`assets.list("")` 打出来看一眼就明白了。

2. **WebView 读不了 `file:///android_asset/` 下 1 MB 以上的资源。**
   1.17 MB 的 `tex-mml-chtml.js` 直接触发 `<script>` 的 error 事件，几十 KB 的探测文件却正常
   （把 assets 改成不压缩也没用，所以不是压缩的问题）。
   → 应用内起一个**只监听 127.0.0.1** 的小 HTTP 服务器（`AssetServer`）把 assets 供出去，
   页面、脚本、字体全走 http，问题消失，还顺带关掉了 `setAllowFileAccess*` 这些开关。

3. **HTTP 服务器必须读完整个请求头再回包。**
   只读请求行就 `close()`，套接字里还有未读数据 → 内核发 RST → 浏览器拿到**被截断的响应**：
   页面空白、所有内联脚本都不执行（表现为 `setTemplates is not defined`）。教训：先读到 `\r\n\r\n`。

4. **别漏 `INTERNET` 权限**（本应用全网络功能都靠它）。
5. **d8 不能用 build-tools 34.0.0**（解析两层匿名类会 NPE），写死 36.0.0。
6. **d8 的参数不能是几百个 `.class` 路径**：Windows 命令行 8191 字符上限会报 “The command line is too long”。
   → 先用 `ZipFile.CreateFromDirectory` 打成 `classes.jar`，只传一个参数。
7. **`javac` 必须显式 `-encoding UTF-8`**，否则 Windows 默认 GBK 会把中文字面量编成乱码。
8. **`.ps1` 保持纯 ASCII**：PowerShell 5.1 在无 BOM 时按 ANSI 解码，中文注释会把脚本解析搞崩
   （这次就因此让 `aapt2 link` 命令被截断）。
9. **不要用 lambda**（d8 处理 invokedynamic 不稳），全用显式匿名内部类。
10. **版本号只写一处**（`Version.java` 的 `VERSION_TAG`），`build.ps1` 自动推导 `versionName/versionCode`，
    打包后还会 `aapt2 dump badging` 复核，防止「编出来的 APK 还是旧版本」。
11. **别忘 `loadUrl`**：编辑器 WebView 建好了却忘了加载页面，界面就是一块空白（这次真踩了）。
12. **`AlertDialog.setItems` 的坐标**：自动化点击时用 `uiautomator dump` 取真实坐标，别按截图目测换算。

### 装 AnkiConnect 时踩到的坑（真机排障记录）

13. **手工写 `config.json` / `meta.json` 千万别带 UTF-8 BOM。**
    PowerShell 的 `Set-Content -Encoding UTF8` 会加 BOM，Anki 用 `json.load(open(path, encoding="utf8"))`
    读，BOM 直接让解析抛错 → `addonConfigDefaults()` 返回 `None` → 插件在
    `util.setting('apiLogPath')` 处崩掉，弹「插件启动失败」。
    诊断办法：临时放一个诊断插件把 `aqt.mw.addonManager` 的状态 dump 出来（`__name__`、`addonFromModule`、
    `getConfig`、`addonMeta`、以及 `dis` 反汇编 `getConfig`），一眼就能看出卡在哪一步。
    修法：用 `[System.IO.File]::WriteAllText($p, $json, (New-Object System.Text.UTF8Encoding($false)))` 写入。
14. **要有 `meta.json`**（`{"name":..., "mod":..., "disabled":false}`），否则容易只被当成"开发插件"导入。
15. **`createDeck` 的参数名在不同版本里不一样**：2025 年的 master 里是 `deck`，旧版/文档里是 `name`；
    传错会返回 `createDeck() got an unexpected keyword argument 'name'`。App 里先发 `deck`，
    报这个错再退回 `name`（见 `AnkiClient.createDeck`，自检有覆盖）。
16. **有些 AnkiConnect 版本在 Anki 26 上会把新卡片丢进「系统默认」牌组**
    （它们用的是老写法 `ankiNote.model()['did'] = deck_id`，对新版 Anki 不生效）。
    App 的兜底：`addNote` 之后用 `findCards("nid:<noteId>")` + `changeDeck` 把卡片搬进目标牌组，
    对已经放对的版本是空操作（见 `AnkiClient.saveNote` / `moveNoteToDeck`）。
    实测结果：卡片正确落在目标牌组（当时用的是 `NCUK::专业术语`，该牌组后来按用户要求删除，
    默认牌组改为收藏库里已有的 `A Level Pure Mathematics`）。
17. **Windows 防火墙**：Anki 绑定 `0.0.0.0:8765` 后会弹安全中心提示，必须点「允许」；
    否则电脑自己 `127.0.0.1:8765` 通、平板连过去 `nc: Timeout`。这个弹窗由系统进程托管，
    普通权限的脚本点不到，只能人工点。

---

## 八、已验证 / 未验证
**已在真机（OPPO Pad 3，Android 15，2800×2000）、模拟器（Pixel 5，Android 11）与真实 Anki 26.9.3 上跑通**：

- 制卡页：输入 `velocity` → 连假 AI → 字段自动填好 → 预览里公式渲染成 `v⃗ = Δs⃗/Δt`
- 保存（假服务）：`createDeck` → `modelNames` → `addNote`（字段与格式一致）→ `sync`
- **保存（真实 Anki，走 Wi-Fi 局域网）**：`测试连接` 返回 `AnkiConnect v6，共 4 个牌组`；
  保存后卡片确实出现在目标牌组，笔记类型「专业术语卡」自动创建，
  `sync()` 成功（App 显示「已保存并同步到 AnkiWeb 云端 ✓」）
- 浏览：牌组列表、`deck:*` 查询、笔记列表（标题/释义/标签）、卡片详情（正反面 + MathJax）都正常
- 手机竖屏底栏 / 平板横屏左栏、转屏重排；崩溃捕获已装，实测无崩溃记录
- 截图见 `preview/`（`tablet-*.png` / `phone-*.png`）

**未验证 / 已知限制**：

- **AI 已接好两套并实测通过**：智谱 GLM-4.5-Flash（免费，当前生效）与 DeepSeek（同一把 Key 实测可用，
  余额约 ¥13.5、单张卡约 ¥0.0023）。两者 Key 分别保存，设置里点「服务商 ▾」即可切换，切换时 Key 自动带出
- **AI 输出仍需人工过一眼**：免费档模型偶尔会把音标写偏（例如 probability 给成 /prɒˈbæbəl/），
  DeepSeek 的准确度明显更高
- **MathJax 字体**：已内置 23 个 `woff`，离线可用；若公式显示为方块，说明字体路径被改动了（见踩坑第 1 条）
- 卡片是**存到电脑上的 Anki**再由它同步云端；电脑关机/Anki 没开时只能先存草稿
- AnkiConnect 默认没有 API Key（`apiKey: null`），同一 Wi-Fi 下知道 IP 的人都能操作你的 Anki；
  介意的话在插件设置里加 `apiKey` 并在 App 里填同样的值
