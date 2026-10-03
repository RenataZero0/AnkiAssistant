# 内置 Anki 引擎（rslib / rsdroid）的编译方法

应用里的「内置引擎」= Anki 官方 Rust 后端 `rslib`，通过 `rsdroid` 的 JNI 桥接调用。
**它不是从 Maven 拿的**（官方没有发布 Android AAR），必须自己交叉编译。本文件记录完整步骤，
换机器时照着做即可。

> ⚠️ 许可证：`rslib` / `rsdroid` 是 **AGPL-3.0**。把 `librsdroid.so` 打进 APK 后，
> 本应用也需以 AGPL-3.0 兼容的方式发布（附源码与许可声明）。
> 相关源码：<https://github.com/ankidroid/Anki-Android-Backend>（含 `anki` 子模块）。

## 1. 工具链

| 组件 | 版本 | 说明 |
|---|---|---|
| Rust | **1.97.1**（`x86_64-pc-windows-msvc` 主机） | 用 MSVC 主机工具链，GNU 主机缺 `dlltool` 会在 `getrandom`/`windows-targets` 上失败 |
| Android NDK | **29.0.14206865** | 版本号取自后端仓库 `gradle/libs.versions.toml` 的 `versions.ndk` |
| MSVC Build Tools | 已装即可 | `rustup` 需要能链接 Windows 主机程序 |

```powershell
rustup toolchain install 1.97.1-x86_64-pc-windows-msvc --profile minimal
rustup target add --toolchain 1.97.1-x86_64-pc-windows-msvc aarch64-linux-android x86_64-linux-android
& "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" --install "ndk;29.0.14206865"
```

## 2. 取源码

```powershell
git clone --depth 1 --recurse-submodules --shallow-submodules `
    https://github.com/ankidroid/Anki-Android-Backend.git
```

## 3. 准备 protoc 与 descriptors（绕开 Anki 的 ninja/python 构建）

后端的构建脚本依赖 `anki/out/rslib/proto/descriptors.bin` 与 `anki/out/strings.json`。
不必跑 Anki 那套 ninja，用 protoc 直接生成即可：

```powershell
# protoc 放到后端约定的位置（.cargo/config.toml 里的 PROTOC 指向这里）
$dest = "Anki-Android-Backend\anki\out\extracted\protoc\bin"
# 从 https://github.com/protocolbuffers/protobuf/releases 取 protoc-<ver>-win64.zip，解出 bin\protoc.exe 放进去

$protos = Get-ChildItem "Anki-Android-Backend\anki\proto\anki" -Filter *.proto
& "$dest\protoc.exe" -I "Anki-Android-Backend\anki\proto" --include_imports `
    --descriptor_set_out="Anki-Android-Backend\anki\out\rslib\proto\descriptors.bin" $protos

# strings.json 只影响 Java 侧的翻译辅助类，空数组即可
'[]' | Out-File "Anki-Android-Backend\anki\out\strings.json" -Encoding ascii -NoNewline
```

## 4. 编译原生库

```powershell
$ndk = "$env:ANDROID_HOME\ndk\29.0.14206865"
$env:ANDROID_NDK_HOME = $ndk
$env:RUSTFLAGS = "-C link-args=-Wl,-z,max-page-size=16384"   # 16KB 页对齐
$bin = "$ndk\toolchains\llvm\prebuilt\windows-x86_64\bin"

foreach ($t in @('x86_64','aarch64')) {
    [Environment]::SetEnvironmentVariable("CC_$t-linux-android", "$bin\$t-linux-android23-clang.cmd", 'Process')
    [Environment]::SetEnvironmentVariable("AR_$t-linux-android", "$bin\llvm-ar.exe", 'Process')
    [Environment]::SetEnvironmentVariable("CARGO_TARGET_$($t.ToUpper())_LINUX_ANDROID_LINKER", "$bin\$t-linux-android23-clang.cmd", 'Process')
}

cd Anki-Android-Backend
cargo +1.97.1-x86_64-pc-windows-msvc build -p rsdroid --release --target x86_64-linux-android
cargo +1.97.1-x86_64-pc-windows-msvc build -p rsdroid --release --target aarch64-linux-android
```

产物（各约 37 MB）：

```
target\x86_64-linux-android\release\librsdroid.so   -> tools\lib\jni\x86_64\librsdroid.so
target\aarch64-linux-android\release\librsdroid.so  -> tools\lib\jni\arm64-v8a\librsdroid.so
```

`build.ps1` 会自动把这两个文件塞进 APK 的 `lib/<abi>/`（配合 manifest 的
`extractNativeLibs="true"`）。`.so` 不进 git（太大）。

## 5. 生成 Java protobuf 类（已提交在 `gen/`）

```powershell
& "$dest\protoc.exe" -I "Anki-Android-Backend\anki\proto" --java_out="lite:$out" $protos
# 把 $out\anki 拷到本工程的 gen\anki
```

`tools\lib\protobuf-javalite.jar` 是这些类的运行时（Maven: com.google.protobuf:protobuf-javalite）。

## 6. 服务/方法编号

`NativeMethods.runMethodRaw(ptr, service, method, args)` 用的是**整数编号**，
从后端生成的 `rsdroid/build/generated/source/backend/anki/backend/GeneratedBackend.kt`
里抄（例如 `addNote -> runMethodRaw(25, 1, input)`）。`AnkiEngine.java` 里的常量就是这么来的，
**改了会调到别的 RPC 上**，务必对照该文件核对。
