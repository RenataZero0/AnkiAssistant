# Build AnkiAssistant.apk  (no Gradle / no Android Studio needed)
#
# 沿用 StudyCompanion 的工具链（aapt2 / d8 / zipalign / apksigner + JDK），
# 已知的坑都已规避：
#   * build-tools 不能用 34.0.0（d8 解析两层匿名类会 NPE）-> 写死 36.0.0
#   * javac 必须显式 -encoding UTF-8（Windows 默认 GBK 会把中文字面量编成乱码）
#   * 不用 lambda（d8 处理 invokedynamic 不稳）
#   * 版本号从 Version.VERSION_TAG 推导，manifest 不写死
#
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 decodes .ps1 as ANSI when there
# is no BOM, which corrupts non-ASCII text and breaks parsing.
$ErrorActionPreference = "Stop"

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$SDK  = "D:\android-sdk"
$BT   = "$SDK\build-tools\36.0.0"
$AJAR = "$SDK\platforms\android-34\android.jar"
$JDK  = "D:\Program Files\Java\jdk-21"

foreach ($p in @("$BT\aapt2.exe", "$BT\d8.bat", "$BT\zipalign.exe", "$BT\apksigner.bat", $AJAR, "$JDK\bin\javac.exe")) {
    if (-not (Test-Path $p)) { throw "missing toolchain component: $p" }
}
$env:JAVA_HOME = $JDK

# Version comes from Version.java so it can never drift from the release.
$vjava = Get-Content "$here\src\com\ankiassistant\Version.java" -Raw
if ($vjava -match 'VERSION_TAG\s*=\s*"v?(\d+)\.(\d+)(?:\.(\d+))?"') {
    $maj = [int]$Matches[1]
    $min = [int]$Matches[2]
    $pat = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
    $verName = "$maj.$min.$pat"
    $verCode = $maj * 10000 + $min * 100 + $pat
} else {
    throw "cannot parse VERSION_TAG from Version.java"
}
Write-Host ("version: {0}  (versionCode {1})" -f $verName, $verCode)

# Drop the previous APK up front: if any later step fails we must not leave a
# stale package behind that looks like a successful build.
$apkPath = "$here\AnkiAssistant.apk"
if (Test-Path $apkPath) { Remove-Item $apkPath -Force }

if (-not (Test-Path "$here\assets\mj-tex-mml-chtml.js")) {
    throw "missing assets\mj-tex-mml-chtml.js - run tools\prepare_assets.py first"
}

# CHANGELOG.md lives at the project root; copy it into assets so the app can show it offline.
$cl = Join-Path $here "CHANGELOG.md"
if (Test-Path $cl) {
    Copy-Item $cl (Join-Path $here "assets\CHANGELOG.md") -Force
    Write-Host "changelog: copied into assets"
} else {
    Write-Host "changelog: CHANGELOG.md not found, skipping"
}

$out = "$here\build"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force "$out\gen", "$out\classes", "$out\dex" | Out-Null

Write-Host "[1/7] aapt2 compile resources ..."
& "$BT\aapt2.exe" compile --dir "$here\res" -o "$out\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed" }

Write-Host "[2/7] aapt2 link (manifest + resources + assets) ..."
# NOTE: the compiled resources zip is a POSITIONAL argument (main resource set).
# -R is for overlays; using it here fails with "does not override an existing resource".
#
# NOTE (real bug we hit): assets MUST stay flat (no subdirectories).
# On Windows aapt2 writes nested asset paths with BACKSLASH separators into the
# APK (assets/mathjax\tex-mml-chtml.js), and AssetManager treats "\" as part of
# the file name, so assets.open("mathjax/tex-mml-chtml.js") throws
# FileNotFoundException and list("mathjax") returns []. Symptoms in the WebView:
# the page renders but every relative script/font 404s (MathJax never loads).
# tools/prepare_assets.py flattens MathJax into assets\mj-* and AssetServer maps
# the URL paths back. Keep this file ASCII-only: PowerShell 5.1 decodes .ps1 as
# ANSI when there is no BOM and non-ASCII comments break parsing.
& "$BT\aapt2.exe" link `
    -o "$out\base.apk" `
    -I $AJAR `
    --manifest "$here\AndroidManifest.xml" `
    -A "$here\assets" `
    --java "$out\gen" `
    --min-sdk-version 21 --target-sdk-version 34 `
    --version-code $verCode --version-name $verName `
    "$out\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed" }

Write-Host "[3/7] javac ..."
$srcs = @(Get-ChildItem "$here\src" -Recurse -Filter *.java | ForEach-Object { $_.FullName })
$gen  = @(Get-ChildItem "$out\gen" -Recurse -Filter *.java -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
& "$JDK\bin\javac.exe" --release 8 -encoding UTF-8 -nowarn -cp $AJAR -d "$out\classes" @($srcs + $gen)
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

Write-Host "[4/7] d8 (dex) ..."
# NOTE: pass ONE jar instead of hundreds of .class paths - Windows caps the command
# line at 8191 chars and "The command line is too long" silently killed d8 here.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$jar = "$out\classes.jar"
if (Test-Path $jar) { Remove-Item $jar -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory("$out\classes", $jar)
if (-not (Test-Path $jar)) { throw "classes.jar not produced" }
& "$BT\d8.bat" --lib $AJAR --min-api 21 --output "$out\dex" $jar
if ($LASTEXITCODE -ne 0) { throw "d8 failed" }

Write-Host "[5/7] add classes.dex into apk ..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
Copy-Item "$out\base.apk" "$out\unsigned.apk" -Force
$zip = [System.IO.Compression.ZipFile]::Open("$out\unsigned.apk", 'Update')
$dex = "$out\dex\classes.dex"
if (-not (Test-Path $dex)) { $zip.Dispose(); throw "classes.dex not produced" }
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $dex, "classes.dex") | Out-Null
$zip.Dispose()

Write-Host "[6/7] zipalign ..."
& "$BT\zipalign.exe" -f -p 4 "$out\unsigned.apk" "$out\aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

Write-Host "[7/7] sign ..."
$ks = "$here\debug.keystore"
if (-not (Test-Path $ks)) {
    & "$JDK\bin\keytool.exe" -genkeypair -v `
        -keystore $ks -storepass android -keypass android `
        -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 `
        -dname "CN=AnkiAssistant, OU=Dev, O=AnkiAssistant, L=Shanghai, C=CN"
    if ($LASTEXITCODE -ne 0) { throw "keytool failed" }
}
$apk = $apkPath
& "$BT\apksigner.bat" sign `
    --ks $ks --ks-pass pass:android --key-pass pass:android `
    --v1-signing-enabled true --v2-signing-enabled true `
    --out $apk "$out\aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

Write-Host ""
Write-Host "OK -> $apk" -ForegroundColor Green
# Guard: verify the produced APK really carries the version we intended.
$badged = & "$BT\aapt2.exe" dump badging $apk 2>&1 | Select-Object -First 1
if ($badged -notmatch [regex]::Escape("versionName='$verName'")) {
    Write-Host "!! APK version mismatch (expected $verName)" -ForegroundColor Red
    Write-Host "   $badged" -ForegroundColor Red
    throw "APK version check failed"
}
Write-Host ("size: {0:N0} KB" -f ((Get-Item $apk).Length / 1KB))
