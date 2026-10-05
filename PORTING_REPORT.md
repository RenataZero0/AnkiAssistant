# CardConfig.cs / CardFormat.cs 移植校验报告

本文件是移植工作的**校验证据**（非应用交付物，可随时删除）。

## 1. 交付物

| 文件 | 行数 | 编译 |
|---|---|---|
| `AnkiAssistant\AnkiAssistant\src\CardConfig.cs` | 453 | PASS |
| `AnkiAssistant\AnkiAssistant\src\CardFormat.cs` | 508 | PASS |

编译命令（.NET Framework 4.x，Release/Debug 均可）：

```
csc.exe /target:library /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll ^
    AnkiAssistant\src\CardConfig.cs AnkiAssistant\src\CardFormat.cs
```

`/r:System.Web.Extensions.dll` 是必需的（`System.Web.Script.Serialization.JavaScriptSerializer`）。

## 2. 公共 API 契约核对

用反射 dump 实际编译出的成员，与契约逐条一致：

```
AnkiAssistant.Field
  public String Name, Key, Hint; public Boolean Latex;
  ctor(string name, string key, string hint, bool latex)

AnkiAssistant.CardConfig
  const: BuiltinId, IdVocabDefault, IdAlevel
  field: Id, Name, NoteType, Css, SystemPrompt, Prompt, Fields, DefaultDeck, DefaultTags, Subject
  static: UsesAppDefaults(string), Builtins(), AlevelConfig(), VocabConfig(), DefaultConfig(),
          FromJson(Dictionary<string,object>), ToJson()
  instance: FieldNames(), AiKeys(), DeckOr(string), TagsOr(string), SubjectOr(string),
            CardFront(), CardBack(), BuildPrompt(string,string), BuildPromptStrict(string,string)

AnkiAssistant.CardFormat
  ModelName(), Fields(), System(), CardFront(), CardBack(), CardCss(), TagsForDeck(string),
  BuildPrompt(string,string), BuildPromptStrict(string,string), ParseAi(string), RepairJson(string),
  FallbackFields(string,string), NoteFields(string,Dictionary), NoteFieldsFor(string,Dictionary,CardConfig),
  MergeNoteFor(string,Dictionary,CardConfig), EmptyFieldsFor(string,CardConfig), EmptyFields(string),
  MergeNote(string,Dictionary), Escape(string), NormalizeFormula(string), PlainBack(Dictionary)
```

`CardFormat` 里比 Java 多出的是薄封装（`ModelName/Fields/System/CardFront/CardBack/CardCss/NoteFields/EmptyFields/MergeNote`），
它们对应 Java 的 `static final` 常量与旧命名；契约要求的成员一个不少。

## 3. 内置格式（与 Android 原版逐字一致）

### builtin[0] — 英语词汇（id `default`，即 `CardConfig.BuiltinId` / `IdVocabDefault`）

| 项 | 值 |
|---|---|
| name | `英语词汇` |
| noteType | `英语词汇卡` |
| defaultDeck | `英语词汇` |
| defaultTags | `English::Vocab` |
| subject | `通用英语（日常与学术都适用）` |
| prompt | `VOCAB_PROMPT`（原版 `CardFormat.VOCAB_PROMPT` 逐字） |

字段（4）：

| # | Name | Key | Hint | Latex |
|---|---|---|---|---|
| 0 | 单词 | word | 单词本身 | false |
| 1 | 音标 | phonetic | 英式与美式音标 | false |
| 2 | 中文 | chinese | 中文释义 | false |
| 3 | 例句 | example | 一个英文例句 | false |

### builtin[1] — A Level 数学/物理（id `alevel`，即 `IdAlevel`）

| 项 | 值 |
|---|---|
| name | `A Level Maths / Phy` |
| noteType | `专业术语卡` |
| defaultDeck / defaultTags / subject | 空（走应用默认，见下） |
| prompt | `DEFAULT_PROMPT`（原版 `CardFormat.SYSTEM` + 字段说明 逐字） |

字段（7）：

| # | Name | Key | Hint | Latex |
|---|---|---|---|---|
| 0 | 单词 | word | 单词或术语本身 | false |
| 1 | 音标 | phonetic | 音标 | false |
| 2 | 词性 | pos | 词性缩写 | false |
| 3 | 定义 | definition | 英文定义 | false |
| 4 | 关联公式/符号 | formula | 相关公式 | **true** |
| 5 | 易混 | confusables | 易混词 | false |
| 6 | 中文 | chinese | 中文释义 | false |

`builtins()` 顺序 = `[VocabConfig(), AlevelConfig()]`（与 Java 的 `builtins()` 一致）；
`DefaultConfig()` = `VocabConfig()`；`UsesAppDefaults(id)` = `id == "alevel"`（精确匹配，null → false）。

### 应用级默认（从 `Store.java` 内联成 `CardConfig` 常量）

| C# 常量 | 值 | 出处 |
|---|---|---|
| `AppDefaultDeck` | `A Level Pure Mathematics` | `Store.java:241` `DEFAULT_DECK` |
| `AppDefaultTags` | `ALevel::Maths` | `Store.java:244` `defaultTags()` |
| `AppDefaultSubject` | `A Level 数学与物理（纯数 / 力学 / 概率统计）` | `Store.java:233` `DEFAULT_SUBJECT` |

## 4. 与 Java 原版的行为对比（parity harness）

脚手架：`tmp_parity\Parity.java`（直接调 Java 原版）与 `tmp_parity\Parity.cs`（调 C# 移植版），
同一批输入、同一输出行格式，两端都自己写 UTF-8 文件（避开 Windows 控制台代码页）。

结果：**两个输出都是 434 行，逐字节比对只有 10 行不同**，全部落在下面这 3 处（均为有意为之）：

| # | 输入 | Java 原版 | C# 移植版 | 性质 |
|---|---|---|---|---|
| 1 | `parseAi("{\u201Cchinese\u201D:\"\u901F\u5EA6\"}")` | `null`（六键全空） | `chinese=速度`（其余键空串） | **放宽**：见 5.1 |
| 2 | `noteFieldsFor(..., 英语词汇config)` 的 `例句` | `<>` 空 | `<He ate an apple.>` | **修 bug**：见 5.2 |
| 3 | `fromJson` 一行里的 `builtin` 标记 | `builtin=true` | 无此字段 | **契约有意**：见 5.3 |

其余全部一致，包括容易走样的这些点（都做了逐行对比，且此前 96 条断言自检全 PASS）：

- 别名表 `parseAi` 的六组别名与 `looseExtract` 的分组差异（loose 的 chinese 组少 `meaningCn`、formula 组少 `关联公式`）——差异被原样保留。
- `repairJson`：全角冒号 `：`→`"："`、键后缺引号、弯引号→直引号、去代码围栏/前导散文/尾逗号。
- `stripFences` 首行判定（Java `matches("[a-z0-9_+-]+")` 是全串匹配 → C# `^[a-z0-9_+-]+$`）。
- `normalizeFormula`：`$$..$$`→`\[..\]`、`$..$`→`\(..\)`、含 CJK（≥ U+2E80）原样返回、否则包 `\( .. \)`。
- `escape` / `plainBack` 的固定六行顺序 / `tagsForDeck` 的前缀与大小写规则 / 卡片模板与 CSS 逐字节。

## 5. 需要父代理知道的 3 处有意偏离（以及 1 处原版 bug）

### 5.1 弯引号坏 JSON：C# 比原版「宽容」（不是漏抄，是有意的）

输入 `{"\u201Cchinese\u201D":"\u901F\u5EA6"}`（值里用的是全角弯引号）：

- Java：`new JSONObject(...)` **竟然能解析**这串，但把 `“chinese”` 整段（含弯引号）当成了键名，
  之后 `src.has("chinese")` 是精确匹配 → 六个键全取不到 → `isNullAll` → `parseAi` 返回 `null`。
- C#：`JavaScriptSerializer` 对这串抛异常 → 落到 `LooseExtract` 的正则硬抠 → 拿到 `速度`。

也就是说 C# 对这种坏输入多接住了一种情况。**若要求逐字节等价**，可在 `CardFormat.ParseAi` 开头加一句：
只要原始字符串里出现 `\u201C`/`\u201D` 就直接 `return null;`。
默认没有加，因为多接住一种坏 JSON 对实际使用只有好处。

顺带修掉一个更隐蔽的差异：`JavaScriptSerializer.Deserialize<Dictionary<string,object>>` 返回的字典
比较器是 `OrdinalIgnoreCase`（**不是** .NET 默认的 `EqualityComparer<string>.Default`），
所以 `ContainsKey("chinese")` 会把 AI 回成 `"Chinese"` / `"PHONETIC"` 的键也算命中，与 `org.json` 的精确匹配不符。
现已通过把解析结果字典换成 `StringComparer.Ordinal` 对齐（`CardFormat.cs` 的 `EnsureKeys` 里做转换）。
验证：`{"Chinese":"速度","POS":"n"}` 与 `{"PHONETIC":...,"CHINESE":"..."}` 两端现在都返回 `null`。

### 5.2 修了原版 Android 的一个 bug：「英语词汇」的例句永远是空的

- `CreateView.java:581` 用 `CardFormat.parseAi(reply.content)` 得到 `parsed`，`:601` 再 `CardFormat.noteFieldsFor(word, parsedF, store.activeConfig())`。
- 而 Java 的 `parseAi`（`CardFormat.java:112-120`）只把六个规范键写进结果（`phonetic/pos/definition/formula/confusables/chinese`），
  **AI 返回的其它键全部丢弃** → 内置「英语词汇」config 的 `example` 键永远取不到 → `例句` 恒为空。
- C# 的 `ParseAi` 在 `IsNullAll` 检查之后加了一次 extras 透传：

  ```csharp
  foreach (KeyValuePair<string, object> kv in o)
  {
      if (!outp.ContainsKey(kv.Key)) outp[kv.Key] = kv.Value;   // 规范化值优先
  }
  ```

  这样自定义 config 可以用任意 AI 键（`example`、`例句`、任何名字）——这正是 `CardConfig.Fields[].Key` 自由配置的意义。

**影响**：C# 版会填上例句，Android 版不会。如果希望 C# 与 Android 完全一致，删掉这段 extras 透传即可（但「英语词汇」的例句就还是空的）。
**建议**：把这个 bug 也回报给 Android 侧修一下（同样一处透传）。

### 5.3 契约有意省略的 `builtin`

Java 的 `CardConfig` 有 `public boolean builtin` 字段、`fromJson` 会读、`toJson` 会写。
C# 契约里没有这个字段，所以：`FromJson` 忽略 `builtin` 键，`ToJson` 不输出它。
另外 `ToJson` 会**多输出** `css` 与 `systemPrompt` 两个键（Java 的 `toJson` 没有），
`FromJson` 也接受这两个键，缺省时回落到 `CardConfig.BuiltinCss` / `BuiltinSystemPrompt`。
这是为了让自定义格式能保存/恢复 CSS 与系统提示词；若不需要，删掉 `ToJson` 里这两行、
把 `FromJson` 里对应的两行换成 `BuiltinCss` / `BuiltinSystemPrompt` 常量即可。

## 6. 其它移植决策（供参考，无行为影响）

- `CardFormat` 里有一个名为 `System()` 的静态方法（对应 Java 常量 `SYSTEM`），它会遮住 `System` 命名空间，
  因此文件头用 `using CsSystem = System;`，命名空间前缀统一写 `CsSystem.xxx`（`CsSystem.StringComparison`、
  `CsSystem.Globalization.CultureInfo`、`catch (CsSystem.Exception)` 等）。
- Java 的 `static final` 常量在 C# 里改成静态方法（`ModelName()/Fields()/System()/CardFront()/CardBack()/CardCss()`），
  避免 C# 静态初始化顺序造成 `CardFormat.CardFront` 依赖 `CardConfig.BuiltinCss` 时的初始化竞态。
- `Regex.Replace` 的替换串在 C# 里反斜杠**不是**转义字符（与 Java 不同）：
  Java 的 `"\\\\[$1\\\\]"` / `"\\\\($1\\\\)"` 在 C# 必须写成 `"\\[$1\\]"` / `"\\($1\\)"`，
  否则会输出双反斜杠 `\\(x^2\\)`。
- `CardConfig.BuiltinCss` / `BuiltinSystemPrompt` 与 `CardFormat.CardCss()` / `System()` 是同一段文本（保持单一来源，改的时候两边一起改）。

## 7. 复现校验的方法

```powershell
$root = "D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant"
$out  = "$root\tmp_parity"; $android = "$root\AnkiAssistantAndroid\src"
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$javac = "C:\Program Files\Common Files\Oracle\Java\javapath\javac.exe"

& $javac -encoding UTF-8 -cp "$android;$root\AnkiAssistantAndroid\tools\lib\json.jar" -d $out "$out\Parity.java"
& $csc /nologo /target:exe /out:"$out\parity_cs.exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll `
    "$root\AnkiAssistant\src\CardConfig.cs" "$root\AnkiAssistant\src\CardFormat.cs" "$out\Parity.cs"
Push-Location $out
& java -cp "$out;$android;$root\AnkiAssistantAndroid\tools\lib\json.jar" Parity "$out\java.txt"
& "$out\parity_cs.exe" "$out\cs.txt"
Pop-Location
# 两个 txt 都是 434 行，逐行 Ordinal 比对，只有第 151-157、283、287、377 行不同（即 5.1/5.2/5.3）
```

自检（96 条断言，全 PASS）：

```powershell
& $csc /nologo /target:exe /out:"$root\.portcheck\portcheck.exe" /r:System.dll /r:System.Core.dll `
    /r:System.Web.Extensions.dll "$root\AnkiAssistant\src\CardConfig.cs" "$root\AnkiAssistant\src\CardFormat.cs" "$root\.portcheck\PortSmoke.cs"
& "$root\.portcheck\portcheck.exe"
```
