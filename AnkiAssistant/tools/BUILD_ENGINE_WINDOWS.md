# 在 Windows 上为 AnkiAssistant 构建 Anki 官方 Rust 引擎（rslib → rslib_aa.dll）

本文记录从零复现的全部步骤。所有命令均在 **PowerShell** 中执行，且 `.ps1` 必须保持纯 ASCII。

- 仓库根（下称 `<root>`）：`D:\UsrFiles\Documents\NCUK IFY Self Study\AnkiAssistant`
- Windows 工程：`<root>\AnkiAssistant\`
- Anki 源码树（**仓库外**）：`D:\Anki-Android-Backend`（Anki-Android-Backend 仓库）
- 产物：`<root>\AnkiAssistant\tools\lib\rslib_aa.dll`

本次实际验证的环境与结果：

| 项目 | 值 |
| --- | --- |
| Rust | 1.97.1，toolchain `1.97.1-x86_64-pc-windows-msvc`，target `x86_64-pc-windows-msvc` |
| MSVC | VS BuildTools 18，`VC\Tools\MSVC\14.51.36231` |
| protoc | `libprotoc 29.3`（手工下载的 win64 release，无需与 protobuf crate 版本一致） |
| Anki 源码 commit | `29bb700b951e3f0c0cb69b77c0180fc1fe33e6ba`（submodule `anki`） |
| rslib 版本 | `26.09.3` |
| 产物大小 | `33,464,320` 字节 |
| 导出符号 | 6 个，见第 6 节 |
| 依赖 DLL | 仅系统 DLL（kernel32/ole32/ws2_32/crypt32/advapi32/bcrypt/…），**无 vcruntime140 / msvcp140 / ucrtbase** |
| 探针结果 | 全部检查通过，退出码 0 |

---

## 0. 前置条件

```powershell
# 代理（GitHub / crates.io 需要）
$env:HTTP_PROXY  = "http://127.0.0.1:7890"
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:CARGO_HTTP_PROXY = "http://127.0.0.1:7890"

# 本机 cargo/rustc 不在 PATH，一律用绝对路径
& "$env:USERPROFILE\.cargo\bin\rustup.exe" toolchain list
& "$env:USERPROFILE\.cargo\bin\rustup.exe" target list --toolchain 1.97.1-x86_64-pc-windows-msvc
# 需要存在：x86_64-pc-windows-msvc
```

> **坑 1：cargo 的工作目录。** `cargo` 必须在 `D:\Anki-Android-Backend` 下执行。若在别的目录调用，会立刻报
> `error: could not find Cargo.toml in D:\... or any parent directory`。用 `workdir` 参数或先 `Set-Location`。

> **坑 2：cargo 把进度写到 stderr。** 在 PowerShell 里这会被包装成 `NativeCommandError`，`$LASTEXITCODE` 有时显示为 1，
> 但构建其实成功了。判断成功要看输出里的 `Finished \`release\` profile ...`。

---

## 1. clone 源码（仓库外）

```powershell
git -c http.proxy=http://127.0.0.1:7890 clone --depth 1 --recurse-submodules --shallow-submodules `
    https://github.com/ankidroid/Anki-Android-Backend.git D:\Anki-Android-Backend
```

> **坑 3：** git 把进度写到 stderr，PowerShell 同样会报 `NativeCommandError`；clone 本身是成功的。
> `--shallow-submodules` 会拉 submodule `anki`（rslib 的真正源码在 `D:\Anki-Android-Backend\anki\rslib`）。

工作区配置 `D:\Anki-Android-Backend\.cargo\config.toml` 已经预设了以下环境变量（相对仓库根）：

```
DESCRIPTORS_BIN       = anki/out/rslib/proto/descriptors.bin
STRINGS_JSON_ANKIDROID = anki/out/strings.json
PROTOC                = anki/out/extracted/protoc/bin/protoc
GENERATED_BACKEND_DIR = rsdroid/build/generated/source/backend
```

也就是说文件只要放到上面这些位置，`cargo build` 无需额外环境变量。

---

## 2. 准备 protoc / descriptors.bin / strings.json

Windows 版不需要 NDK，但 rslib 编译时需要 `protoc` 和 descriptor set。

```powershell
# 2.1 下载 protoc（本机没有 protoc；用哪个小版本都可以，29.3 已验证）
$dl = 'D:\Anki-Android-Backend\tools\protoc-dl'
New-Item -ItemType Directory -Force -Path $dl | Out-Null
Invoke-WebRequest -Proxy 'http://127.0.0.1:7890' `
  -Uri 'https://github.com/protocolbuffers/protobuf/releases/download/v29.3/protoc-29.3-win64.zip' `
  -OutFile "$dl\protoc-29.3-win64.zip"
Expand-Archive "$dl\protoc-29.3-win64.zip" -DestinationPath "$dl\protoc-29.3" -Force

# 2.2 放到 .cargo/config.toml 期望的位置
$bin = 'D:\Anki-Android-Backend\anki\out\extracted\protoc\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item "$dl\protoc-29.3\bin\protoc.exe" $bin -Force
& "$bin\protoc.exe" --version      # -> libprotoc 29.3

# 2.3 生成 descriptors.bin（被 rslib 的 build.rs 用来生成 Rust 服务分发代码）
Set-Location D:\Anki-Android-Backend
New-Item -ItemType Directory -Force -Path 'anki\out\rslib\proto' | Out-Null
$protos = Get-ChildItem 'anki\proto\anki\*.proto' | ForEach-Object { "anki/$($_.Name)" }
& "$bin\protoc.exe" -I anki/proto --include_imports `
    --descriptor_set_out=anki/out/rslib/proto/descriptors.bin @protos
# 期望 87023 字节；会打印两条无害警告：
#   Import anki/notes.proto is unused.   (来自 anki/frontend.proto)
#   Import anki/notetypes.proto is unused.

# 2.4 给 rsdroid 生成器用的空字符串表（Windows 侧实际用不到，但 build.rs 要求文件存在）
'[]' | Out-File 'anki\out\strings.json' -Encoding ascii -NoNewline
```

---

## 3. 新增 `aa-ffi` crate

目录：`D:\Anki-Android-Backend\aa-ffi\`（完整源码见文末 **附录 A**）。

```powershell
New-Item -ItemType Directory -Force -Path 'D:\Anki-Android-Backend\aa-ffi\src\bin' | Out-Null
# 写入 aa-ffi\Cargo.toml / src\lib.rs / src\bin\svc_table.rs （附录 A）

# 把它登记进工作区
#   D:\Anki-Android-Backend\Cargo.toml
#   members = ["rslib-bridge", "build_rust", "aa-ffi"]
```

要点：

- `[lib] name = "aa_ffi"`、`crate-type = ["cdylib", "rlib"]` → 产物 `aa_ffi.dll`。
- 依赖 `anki = { path = "../anki/rslib", features = ["rustls"] }`（`rslib-bridge` 用的同一套 feature，TLS 走 rustls，不引入 OpenSSL，Windows 上无需额外库）。
- **不要**用 `.workspace = true`：`D:\Anki-Android-Backend\Cargo.toml` 里没有 `[workspace.dependencies]`（那些在 `anki/` 子工作区里）。
- 所有 `extern "C"` 入口都套 `catch_unwind(AssertUnwindSafe(..))`，错误用返回码表达（0 成功 / 1 参数错 / 2 引擎错 / 3 panic 被捕获），文字放 `aa_last_error()`。release profile 保持默认 `panic = "unwind"`（若是 `panic = "abort"`，`catch_unwind` 就失效了）。
- 返回值缓冲用 `Vec::into_boxed_slice()` + `Box::into_raw` 交出，`aa_free(buf, len)` 里用 `Box::from_raw(slice)` 精确还原。这样 len 与分配长度一致，避免 `Vec::from_raw_parts` 的容量不匹配 UB。

---

## 4. 编译（静态 CRT）

```powershell
$env:HTTP_PROXY  = "http://127.0.0.1:7890"
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:CARGO_HTTP_PROXY = "http://127.0.0.1:7890"
$env:CARGO_NET_RETRY = "5"
$env:RUSTFLAGS = "-C target-feature=+crt-static"     # <== 关键：静态链接 CRT

Set-Location D:\Anki-Android-Backend
& "$env:USERPROFILE\.cargo\bin\cargo.exe" +1.97.1-x86_64-pc-windows-msvc build `
    --release --target x86_64-pc-windows-msvc -p aa-ffi
```

> **坑 4：必须加 `-C target-feature=+crt-static`。** 否则 DLL 会依赖 `vcruntime140.dll` / `msvcp140.dll`，
> 在没有装 VC++ 运行库的机器上加载即失败。加了之后 `/dependents` 只剩系统 DLL。
> 本仓库**没有** `.cargo/config.toml` 里的 `rustflags`，所以这是每次构建都必须显式设置的环境变量（或者写进 `[env]`）。

> **坑 5：`RUSTFLAGS` 变了会整棵树重编。** 从默认切到 `+crt-static` 会重新编译全部依赖（首次约 2 分 15 秒）。

首次完整构建约 2 分钟；增量重建不到 10 秒。产物：

```
D:\Anki-Android-Backend\target\x86_64-pc-windows-msvc\release\aa_ffi.dll   33464320 字节
D:\Anki-Android-Backend\target\x86_64-pc-windows-msvc\release\aa_ffi.pdb
```

---

## 5. 复制到 tools\lib

```powershell
$lib = '<root>\AnkiAssistant\tools\lib'
New-Item -ItemType Directory -Force -Path $lib | Out-Null
Copy-Item 'D:\Anki-Android-Backend\target\x86_64-pc-windows-msvc\release\aa_ffi.dll' `
          (Join-Path $lib 'rslib_aa.dll') -Force
```

> `tools\lib\rslib_aa.dll` **不进 git**（`.gitignore` 由你自己维护）。约定：探针与 C# 侧只认名字 `rslib_aa.dll`。

---

## 6. 校验导出与依赖

`dumpbin.exe` 路径：`C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\dumpbin.exe`

```powershell
$db  = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\dumpbin.exe'
$dll = '<root>\AnkiAssistant\tools\lib\rslib_aa.dll'
& $db /nologo /exports    $dll
& $db /nologo /dependents $dll
```

`/exports` 的实际输出（6 个符号，全部按名字导出）：

```
    ordinal hint RVA      name
          1    0 00002A60 aa_call = aa_call
          2    1 00003900 aa_close = aa_close
          3    2 00003A90 aa_free = aa_free
          4    3 00003AB0 aa_last_error = aa_last_error
          5    4 00003B50 aa_open = aa_open
          6    5 00003F10 aa_version = aa_version
```

`/dependents` 的实际输出（**没有** vcruntime140/msvcp140/ucrtbase）：

```
kernel32.dll
combase.dll
shell32.dll
api-ms-win-core-winrt-l1-1-0.dll
ole32.dll
oleaut32.dll
bcryptprimitives.dll
api-ms-win-core-synch-l1-2-0.dll
ws2_32.dll
crypt32.dll
advapi32.dll
bcrypt.dll
ntdll.dll
```

---

## 7. 用 `svc_table` 确认 service/method 索引（不要猜）

`Backend::run_service_method(service: u32, method: u32, ..)` 用的是 `anki_proto_gen::get_services()`
产生的整数索引，必须从 `descriptors.bin` 里读出来：

```powershell
Set-Location D:\Anki-Android-Backend
& "$env:USERPROFILE\.cargo\bin\cargo.exe" +1.97.1-x86_64-pc-windows-msvc run --release `
    --target x86_64-pc-windows-msvc --bin svc_table -- `
    'anki\out\rslib\proto\descriptors.bin' Collection
& "$env:USERPROFILE\.cargo\bin\cargo.exe" +1.97.1-x86_64-pc-windows-msvc run --release `
    --target x86_64-pc-windows-msvc --bin svc_table -- `
    'anki\out\rslib\proto\descriptors.bin' Decks
```

本机实际输出（节选）：

```
SERVICE idx=3 name=BackendCollectionService trait_methods=6 delegating_methods=8
  METHOD service_idx=3 method_idx=0 BackendCollectionService::open_collection
  METHOD service_idx=3 method_idx=1 BackendCollectionService::close_collection
  ...
SERVICE idx=7 name=BackendDecksService trait_methods=0 delegating_methods=24
  ...
  METHOD service_idx=7 method_idx=13 BackendDecksService::get_deck_names
  ...
```

=> 探针用到的常量：`S_COLLECTION=3`、`M_OPEN_COLLECTION=0`、`M_CLOSE_COLLECTION=1`、`S_DECKS=7`、`M_GET_DECK_NAMES=13`，
与安卓侧 `<root>\AnkiAssistantAndroid\src\com\ankiassistant\AnkiEngine.java` 里的常量一致。

> **坑 6：** 索引不是 `backend.proto` 里的声明顺序 —— `get_services()` 把「只在 Backend 服务里声明的 delegating 方法」
> 排在 trait 方法之后（`method.index + service.trait_methods.len()`，见 `anki/rslib/proto_gen/src/lib.rs:76`）。
> 唯一可靠来源就是 `svc_table`。

> **坑 7：** `cargo run` 在你没设代理的会话里可能卡住（需要下载新依赖时）。跑之前先设好 `HTTP_PROXY`。

---

## 8. 编译并运行探针 `ffi_probe.cs`

探针：`<root>\AnkiAssistant\tools\ffi_probe.cs`（C# 5 语法，只用 `DllImport` + 手写 protobuf 字节，不用 Span/unsafe/NuGet）。
引用集对齐 `<root>\AnkiAssistant\build.ps1`。

```powershell
$csc   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tools = '<root>\AnkiAssistant\tools'
& $csc /nologo /codepage:65001 /target:exe /optimize+ /main:FfiProbe `
  /out:"$env:TEMP\ffi_probe.exe" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
  /r:System.Xml.Linq.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Security.dll `
  "$tools\ffi_probe.cs"

# 运行：DLL 用 DllImport("rslib_aa.dll") 加载，所以 tools\lib 必须在 PATH 里
$env:PATH = "$tools\lib;" + $env:PATH
& "$env:TEMP\ffi_probe.exe"
"PROBE_EXIT=$LASTEXITCODE"
```

探针做的事：

1. `aa_version()` 打印版本，并断言包含 `aa-ffi`；
2. 反例：把 `%TEMP%\aa-engine-test-bad\collection.anki2` 建成一个**目录**，再调 `aa_open` + `OpenCollection`，
   期望返回码非 0 且 `aa_last_error()` 有可读文字、不含 `panic`、进程不崩；
3. 正例：在 `%TEMP%\aa-engine-test\collection.anki2`（媒体目录取同目录）打开全新收藏库，调 `getDeckNames`，解析并打印牌组名；
4. `aa_open` / `aa_call` / `aa_close` 循环 200 次，统计失败次数并采样 `WorkingSet64`；
5. 关库、删临时目录、汇报退出码。

**实际输出**（`ffi_probe.exe`，退出码 0）：

```
== AnkiAssistant native engine probe ==
dll            : rslib_aa.dll
probe exe      : C:\Users\SCHIRO~1\AppData\Local\Temp\ffi_probe.exe
aa_version()   : aa-ffi 0.1.0 / rslib anki 26.09.3 (build ) / windows-x86_64
PASS  DLL loads and aa_version() returns text  [aa-ffi 0.1.0 / rslib anki 26.09.3 (build ) / windows-x86_64]
PASS  aa_open() on a valid BackendInit returns 0  [rc=0]
bad OpenCollection rc=2 err=DbError { info: "SqliteFailure(Error { code: CannotOpen, extended_code: 526 }, Some(\"unable to open database file: C:\\\\Users\\\\Schirowarz\\\\AppData\\\\Local\\\\Temp\\\\aa-engine-test-bad\\\\collection.anki2\"))", kind: Other }
PASS  opening an unusable collection path fails with a non-zero code  [rc=2]
PASS  ...and the failure carries readable text (no panic, no crash)  [DbError { ... } / 236 error bytes]
PASS  aa_open + OpenCollection + GetDeckNames on a fresh collection
GetDeckNames   : 13 response bytes, 1 deck(s)
      - Default
PASS  deck list is non-empty  [1 deck(s)]
PASS  deck list contains the default deck  [Default]
loop           : 200 x (aa_open + OpenCollection + GetDeckNames + aa_close)
working set    : after1=22MB  after100=23MB  after200=23MB
PASS  200 open/call/close cycles all succeeded  [0 failures]
PASS  working set did not run away across the loop  [23MB vs 23MB]
PASS  temporary collection directory removed  [C:\Users\Schirowarz\AppData\Local\Temp\aa-engine-test]

RESULT: all checks passed
PROBE_EXIT=0
```

> **坑 8：不存在的收藏库路径不是可靠的失败用例。** rslib 会**新建**缺失的 `collection.anki2`，
> 所以「打开一个不存在的路径」会成功。探针改成在目标路径上放一个同名**目录**（SQLite 报 `CannotOpen / extended_code 526`），
> 这样在任何机器上都稳定失败。

> **坑 9：`ffi_probe.cs` 的 `codepage:65001` 不能省**，文件里有中文注释。`csc` 会报一条 `CS0675`
>（`Pb.Tag` 里 `int << 3` 的符号扩展）—— 无害。

> **坑 10：** `aa_version()` 里 `buildhash()` 是空的（源码树是 `--depth 1` 的 tarball 式 checkout，没有 jammy 注入的 buildhash），
> 显示成 `(build )`。不影响功能。

---

## 9. 一次性复现脚本（要点汇总）

```powershell
# 1) clone + protoc + descriptors + strings   -> 第 1、2 节
# 2) 写 aa-ffi crate (附录 A) 并登记 workspace -> 第 3 节
# 3) 构建
$env:RUSTFLAGS = "-C target-feature=+crt-static"
$env:HTTP_PROXY = $env:HTTPS_PROXY = $env:CARGO_HTTP_PROXY = "http://127.0.0.1:7890"
Set-Location D:\Anki-Android-Backend
& "$env:USERPROFILE\.cargo\bin\cargo.exe" +1.97.1-x86_64-pc-windows-msvc build --release --target x86_64-pc-windows-msvc -p aa-ffi
# 4) 复制
Copy-Item 'D:\Anki-Android-Backend\target\x86_64-pc-windows-msvc\release\aa_ffi.dll' `
          '<root>\AnkiAssistant\tools\lib\rslib_aa.dll' -Force
# 5) 校验 + 探针 -> 第 6、8 节
```

---

## 附录 A：`aa-ffi` crate 源码

> 位置：`D:\Anki-Android-Backend\aa-ffi\`（**在 git 仓库外**，`<root>` 里只保留本文件作为可复现记录）。

### A.1 `D:\Anki-Android-Backend\aa-ffi\Cargo.toml`（23 行）

```toml
[package]
name = "aa-ffi"
version = "0.1.0"
edition = "2021"
license = "AGPL-3.0-or-later"
publish = false
description = "Minimal C ABI wrapper around the Anki Rust backend (rslib) for AnkiAssistant"

[lib]
name = "aa_ffi"
crate-type = ["cdylib", "rlib"]

[dependencies]
anki = { path = "../anki/rslib", features = ["rustls"] }
anki_proto = { path = "../anki/rslib/proto" }
anki_proto_gen = { path = "../anki/rslib/proto_gen" }
once_cell = "1.21"
prost = "0.13"
prost-reflect = "0.14"

[[bin]]
name = "svc_table"
path = "src/bin/svc_table.rs"
```

同时把 `D:\Anki-Android-Backend\Cargo.toml` 的

```toml
members = ["rslib-bridge", "build_rust"]
```

改成

```toml
members = ["rslib-bridge", "build_rust", "aa-ffi"]
```

### A.2 `D:\Anki-Android-Backend\aa-ffi\src\lib.rs`（261 行）

```rust
#![allow(clippy::missing_safety_doc)]
//! Minimal C ABI wrapper around Anki's Rust backend (`rslib`).
//!
//! This is the Windows/desktop analogue of `rslib-bridge` (the rsdroid JNI
//! bridge). It exposes a flat, P/Invoke friendly surface:
//!
//! ```c
//! int32_t aa_open(const uint8_t* data, size_t data_len, uint64_t* out_handle, uint8_t** out_buf, size_t* out_len);
//! int32_t aa_call(uint64_t handle, int32_t service, int32_t method, const uint8_t* data, size_t data_len, uint8_t** out_buf, size_t* out_len);
//! void    aa_close(uint64_t handle);
//! void    aa_free(uint8_t* buf, size_t len);
//! const char* aa_version(void);
//! const char* aa_last_error(void);
//! ```
//!
//! Return codes:
//!   0  ok
//!   1  caller error (bad arguments)
//!   2  engine error (`Err` from rslib; message in `aa_last_error()`)
//!   3  a Rust panic was caught (`aa_last_error()` holds the payload)
//!
//! No panic is allowed to cross the FFI boundary: every entry point is wrapped
//! in `catch_unwind`. The release profile keeps the default `panic = "unwind"`.

use std::{
    cell::RefCell,
    ffi::{c_char, CString},
    panic::{catch_unwind, AssertUnwindSafe},
    slice,
};

use anki::backend::{init_backend, Backend};
use once_cell::sync::Lazy;
use prost::Message;

const AA_OK: i32 = 0;
const AA_ERR_ARGS: i32 = 1;
const AA_ERR_ENGINE: i32 = 2;
const AA_ERR_PANIC: i32 = 3;

thread_local! {
    /// Last error text for this thread. `aa_last_error()` returns a pointer into
    /// this buffer; it stays valid until the next `aa_*` call on this thread.
    static LAST_ERROR: RefCell<CString> = RefCell::new(CString::new("").unwrap());
}

fn set_last_error(msg: &str) {
    // CString::new fails on interior NULs; strip them so we never lose the text.
    let sanitized: String = msg
        .chars()
        .map(|c| if c == '\0' { ' ' } else { c })
        .collect();
    let c = CString::new(sanitized).unwrap_or_else(|_| CString::new("error").unwrap());
    LAST_ERROR.with(|slot| *slot.borrow_mut() = c);
}

fn panic_message(panic: Box<dyn std::any::Any + Send>) -> String {
    if let Some(s) = panic.downcast_ref::<&'static str>() {
        (*s).to_string()
    } else if let Some(s) = panic.downcast_ref::<String>() {
        s.clone()
    } else {
        "unknown panic".to_string()
    }
}

/// Turn a Vec<u8> into a heap block the caller must release with `aa_free`.
/// Empty payloads become (null, 0) so `aa_free` is a no-op for them.
fn leak_vec(v: Vec<u8>) -> (*mut u8, usize) {
    if v.is_empty() {
        return (std::ptr::null_mut(), 0);
    }
    let boxed = v.into_boxed_slice();
    let len = boxed.len();
    (Box::into_raw(boxed) as *mut u8, len)
}

/// rslib packs errors as an encoded `anki_proto::backend::BackendError`.
fn describe_backend_error(bytes: &[u8]) -> String {
    match anki_proto::backend::BackendError::decode(bytes) {
        Ok(err) => {
            if err.message.is_empty() {
                format!("backend error (kind {:?})", err.kind())
            } else {
                err.message
            }
        }
        Err(_) => format!(
            "undecodable backend error: {} bytes, hex {}",
            bytes.len(),
            bytes
                .iter()
                .take(64)
                .map(|b| format!("{b:02x}"))
                .collect::<String>()
        ),
    }
}

static VERSION_C: Lazy<CString> = Lazy::new(|| {
    let text = format!(
        "aa-ffi {} / rslib anki {} (build {}) / {}-{}",
        env!("CARGO_PKG_VERSION"),
        anki::version::version(),
        anki::version::buildhash(),
        std::env::consts::OS,
        std::env::consts::ARCH,
    );
    CString::new(text).unwrap()
});

/// Static version / self-test string. Do NOT free.
#[no_mangle]
pub extern "C" fn aa_version() -> *const c_char {
    VERSION_C.as_ptr()
}

/// Human readable text for the most recent failure on this thread. Do NOT free.
#[no_mangle]
pub extern "C" fn aa_last_error() -> *const c_char {
    LAST_ERROR.with(|slot| slot.borrow().as_ptr())
}

/// Release a buffer previously handed out through `*out_buf`.
#[no_mangle]
pub unsafe extern "C" fn aa_free(buf: *mut u8, len: usize) {
    if buf.is_null() || len == 0 {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        let slice = slice::from_raw_parts_mut(buf, len);
        drop(Box::from_raw(slice as *mut [u8]));
    }));
}

unsafe fn input_slice<'a>(data: *const u8, data_len: usize) -> Option<&'a [u8]> {
    if data_len == 0 {
        Some(&[])
    } else if data.is_null() {
        None
    } else {
        Some(slice::from_raw_parts(data, data_len))
    }
}

/// Open the backend. `data` is an encoded `anki_proto::backend::BackendInit`.
/// On success `*out_handle` receives the handle (non-zero). Any `*out_buf`
/// written here must be released with `aa_free`.
#[no_mangle]
pub unsafe extern "C" fn aa_open(
    data: *const u8,
    data_len: usize,
    out_handle: *mut u64,
    out_buf: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if out_handle.is_null() || out_buf.is_null() || out_len.is_null() {
        set_last_error("aa_open: out_handle/out_buf/out_len must not be NULL");
        return AA_ERR_ARGS;
    }
    *out_handle = 0;
    *out_buf = std::ptr::null_mut();
    *out_len = 0;

    let input = match input_slice(data, data_len) {
        Some(s) => s,
        None => {
            set_last_error("aa_open: data is NULL but data_len is non-zero");
            return AA_ERR_ARGS;
        }
    };

    match catch_unwind(AssertUnwindSafe(|| init_backend(input))) {
        Ok(Ok(backend)) => {
            // BackendInit has no response message; the caller only needs the handle.
            *out_handle = Box::into_raw(Box::new(backend)) as u64;
            AA_OK
        }
        Ok(Err(err)) => {
            set_last_error(&err);
            AA_ERR_ENGINE
        }
        Err(panic) => {
            set_last_error(&format!("panic in aa_open: {}", panic_message(panic)));
            AA_ERR_PANIC
        }
    }
}

/// Dispatch one RPC. `service`/`method` are the integer indices from the
/// generated backend (see `rsdroid/build/generated/source/backend`).
///
/// On success `*out_buf`/`*out_len` hold the encoded response.
/// On `AA_ERR_ENGINE` they hold the encoded `BackendError` (if any); either way
/// release them with `aa_free`.
#[no_mangle]
pub unsafe extern "C" fn aa_call(
    handle: u64,
    service: i32,
    method: i32,
    data: *const u8,
    data_len: usize,
    out_buf: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if out_buf.is_null() || out_len.is_null() {
        set_last_error("aa_call: out_buf/out_len must not be NULL");
        return AA_ERR_ARGS;
    }
    *out_buf = std::ptr::null_mut();
    *out_len = 0;

    if handle == 0 {
        set_last_error("aa_call: handle is 0 (backend not open)");
        return AA_ERR_ARGS;
    }
    let input = match input_slice(data, data_len) {
        Some(s) => s,
        None => {
            set_last_error("aa_call: data is NULL but data_len is non-zero");
            return AA_ERR_ARGS;
        }
    };

    let backend = &mut *(handle as *mut Backend);
    let service = service as u32;
    let method = method as u32;

    match catch_unwind(AssertUnwindSafe(|| {
        backend.run_service_method(service, method, input)
    })) {
        Ok(Ok(bytes)) => {
            let (buf, len) = leak_vec(bytes);
            *out_buf = buf;
            *out_len = len;
            AA_OK
        }
        Ok(Err(err_bytes)) => {
            set_last_error(&describe_backend_error(&err_bytes));
            let (buf, len) = leak_vec(err_bytes);
            *out_buf = buf;
            *out_len = len;
            AA_ERR_ENGINE
        }
        Err(panic) => {
            set_last_error(&format!("panic in aa_call: {}", panic_message(panic)));
            AA_ERR_PANIC
        }
    }
}

/// Close the backend and release its handle. Safe to call with 0.
#[no_mangle]
pub unsafe extern "C" fn aa_close(handle: u64) {
    if handle == 0 {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        drop(Box::from_raw(handle as *mut Backend));
    }));
}
```

### A.3 `D:\Anki-Android-Backend\aa-ffi\src\bin\svc_table.rs`（49 行）

```rust
//! Dump the service/method index table used by `Backend::run_service_method`.
//!
//!   cargo run --release --bin svc_table -- <path/to/descriptors.bin> [filter]

use anki_proto_gen::get_services;
use prost_reflect::DescriptorPool;

fn main() {
    let path = std::env::args()
        .nth(1)
        .expect("usage: svc_table <descriptors.bin> [name filter]");
    let filter = std::env::args().nth(2);
    let bytes = std::fs::read(&path).expect("cannot read descriptors.bin");
    let pool = DescriptorPool::decode(bytes.as_ref()).expect("cannot decode descriptors.bin");
    let (_col, backend) = get_services(&pool);

    for service in &backend {
        let hit = match &filter {
            Some(f) => service.name.contains(f.as_str()),
            None => true,
        };
        if hit {
            println!(
                "SERVICE idx={} name={} trait_methods={} delegating_methods={}",
                service.index,
                service.name,
                service.trait_methods.len(),
                service.delegating_methods.len()
            );
        }
        for method in service.all_methods() {
            let hit = match &filter {
                Some(f) => service.name.contains(f.as_str()) || method.name.contains(f.as_str()),
                None => true,
            };
            if hit {
                println!(
                    "  METHOD service_idx={} method_idx={} {}::{}",
                    service.index, method.index, service.name, method.name
                );
            }
        }
    }
}
```

---

## 附录 B：C# 侧 P/Invoke 约定

```csharp
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern IntPtr aa_version();                     // 静态字符串，勿释放
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern IntPtr aa_last_error();                  // 同上
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern int  aa_open(IntPtr data, UIntPtr dataLen, out ulong handle, out IntPtr outBuf, out UIntPtr outLen);
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern int  aa_call(ulong handle, int service, int method, IntPtr data, UIntPtr dataLen, out IntPtr outBuf, out UIntPtr outLen);
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void aa_close(ulong handle);
[DllImport("rslib_aa.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void aa_free(IntPtr buf, UIntPtr len);
```

- `rslib_aa.dll` 必须放在 exe 同目录，或所在目录加入 `PATH`。
- 返回码：`0` 成功，`1` 参数错，`2` 引擎错（`aa_last_error()` 有文字；同时 `outBuf` 里是编码的 `BackendError`），`3` panic 被捕获。
- `aa_call` 失败时 `outBuf` 也可能非空，无论成败都要用 `aa_free(buf, len)` 释放（`len == 0` 时可不调）。

## 附录 C：常量对照

| 常量 | 值 | 来源 |
| --- | --- | --- |
| `S_COLLECTION` | 3 | `svc_table` / `AnkiEngine.java` |
| `M_OPEN_COLLECTION` | 0 | 同上 |
| `M_CLOSE_COLLECTION` | 1 | 同上 |
| `S_DECKS` | 7 | 同上 |
| `M_GET_DECK_NAMES` | 13 | 同上 |
| `OpenCollectionRequest` | `collection_path=1`, `media_folder_path=2`, `media_db_path=3` | `anki/proto/anki/collection.proto:43` |
| `GetDeckNamesRequest` | `skip_empty_default=1`, `include_filtered=2` | `anki/proto/anki/decks.proto:196` |
| `DeckNames` | `repeated DeckNameId entries = 1` | `anki/proto/anki/decks.proto:202` |
| `DeckNameId` | `int64 id=1`, `string name=2` | `anki/proto/anki/decks.proto:206` |
| `BackendInit` | `repeated string preferred_langs=1`, `locale_folder_path=2`, `bool server=3` | `anki/proto/anki/backend.proto` |
