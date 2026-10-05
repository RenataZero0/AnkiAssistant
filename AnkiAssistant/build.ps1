# Build AnkiAssistant.exe  (Windows desktop build, no Visual Studio needed)
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
#
# Only needs the .NET Framework 4.x csc.exe that ships with Windows.
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 decodes .ps1 files as ANSI
# when there is no BOM, which corrupts non-ASCII characters and breaks parsing.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "csc.exe not found (need .NET Framework 4.x)" }

$src = Get-ChildItem "$here\src\*.cs" | Where-Object { $_.Name -ne "_test.cs" } | ForEach-Object { $_.FullName }
$out = "$here\AnkiAssistant.exe"
$ico = "$here\assets\app.ico"

# Drop the previous exe up front: if a later step fails we must not leave a stale
# binary behind that looks like a successful build.
if (Test-Path $out) { Remove-Item $out -Force }

# /win32icon -> Explorer shows this icon for the exe
# /resource   -> the app picks the right size at runtime (16 for tray, 32 for title bar)
$iconArgs = @()
if (Test-Path $ico) {
    $iconArgs = @("/win32icon:$ico", "/resource:$ico,AnkiAssistant.app.ico")
} else {
    Write-Host "WARN: assets\app.ico not found - the exe will have no icon (run tools\make_icon.py)" -ForegroundColor Yellow
}

& $csc /nologo /codepage:65001 /target:winexe /optimize+ /out:$out `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Xml.Linq.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Security.dll @iconArgs $src

if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

# CHANGELOG.md lives at the repo root; copy it next to the exe as an offline fallback.
# At runtime the app prefers the copy it fetched from GitHub (%APPDATA%\AnkiAssistant\CHANGELOG.md).
$cl = Join-Path (Split-Path -Parent $here) "CHANGELOG.md"
if (Test-Path $cl) { Copy-Item $cl (Join-Path $here "CHANGELOG.md") -Force }

Write-Host "OK -> $out" -ForegroundColor Green
Write-Host ("size: {0:N0} KB" -f ((Get-Item $out).Length / 1KB))
