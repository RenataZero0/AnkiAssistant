# Compile + run the pure-logic self test on the desktop JVM.
# The tested classes deliberately depend only on java.* and org.json, so they
# run without any Android SDK.
# Usage: powershell -ExecutionPolicy Bypass -File selftest.ps1
#
# NOTE: keep this file ASCII-only (PowerShell 5.1 decodes .ps1 as ANSI without BOM).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$JDK  = "D:\Program Files\Java\jdk-21"
$JAR  = "$here\tools\lib\json.jar"
$out  = "$here\build\selftest"

if (-not (Test-Path "$JDK\bin\javac.exe")) { throw "missing javac: $JDK" }
if (-not (Test-Path $JAR)) { throw "missing $JAR" }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

$src = @(
    "$here\src\com\ankiassistant\CardFormat.java",
    "$here\src\com\ankiassistant\AnkiClient.java",
    "$here\src\com\ankiassistant\AiClient.java",
    "$here\src\com\ankiassistant\Version.java",
    "$here\tools\SelfTest.java"
)
foreach ($f in $src) { if (-not (Test-Path $f)) { throw "missing source $f" } }

& "$JDK\bin\javac.exe" -encoding UTF-8 -nowarn -cp $JAR -d $out @src
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

& "$JDK\bin\java.exe" -cp "$out;$JAR" SelfTest
if ($LASTEXITCODE -ne 0) { throw "self test failed" }

Write-Host "SELFTEST OK" -ForegroundColor Green
