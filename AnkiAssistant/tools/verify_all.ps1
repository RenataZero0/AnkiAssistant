# verify_all.ps1 - acceptance check for the Windows build.
# Pure ASCII on purpose (this repo's .ps1 files must stay ASCII).
#
#   powershell -ExecutionPolicy Bypass -File tools\verify_all.ps1
#   powershell -ExecutionPolicy Bypass -File tools\verify_all.ps1 -Install
#
# Steps:
#   1. kill a stray AnkiAssistant (single-instance mutex would otherwise make
#      the --shot/--selftest run exit silently)
#   2. build the app          (build.ps1)   -> must print OK
#   3. run the static selftest              -> must be FAIL 0
#   4. confirm rslib_aa.dll sits next to the exe
#   5. build the installer   (build-setup.ps1)
#   6. with -Install: silent install, then run the INSTALLED exe's selftest
#      and report the registered DisplayVersion
param(
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$win   = Split-Path -Parent $PSScriptRoot          # AnkiAssistant\AnkiAssistant
$repo  = Split-Path -Parent (Split-Path -Parent $win)   # repo root
$setup = Join-Path $repo 'AnkiAssistantSetup'
$exe   = Join-Path $win 'AnkiAssistant.exe'
$dll   = Join-Path $win 'rslib_aa.dll'

$fail = 0
function Step($text) { Write-Host ""; Write-Host ("=== " + $text) }
function Ok($text)   { Write-Host ("  OK   " + $text) }
function Bad($text)  { Write-Host ("  FAIL " + $text); $script:fail++ }

Step '1. stop stray instances'
Get-Process AnkiAssistant -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Ok 'done'

Step '2. build the app'
$out = & powershell -ExecutionPolicy Bypass -File (Join-Path $win 'build.ps1') 2>&1
$out | Select-Object -Last 4 | ForEach-Object { Write-Host ("  | " + $_) }
if ($out -match 'OK ->') { Ok 'build.ps1 reported OK' } else { Bad 'build.ps1 did not report OK' }
$errors = ($out | Select-String -Pattern ': error CS' | Measure-Object).Count
if ($errors -eq 0) { Ok 'csc errors: 0' } else { Bad ("csc errors: " + $errors) }

Step '3. static selftest'
if (-not (Test-Path $exe)) {
    Bad 'exe missing, cannot run selftest'
} else {
    $p = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
    $txt = Join-Path $win 'selftest.txt'
    if (Test-Path $txt) {
        $line = (Select-String -Path $txt -Pattern '^PASS \d+ / FAIL \d+ / SKIP \d+').Line
        Write-Host ("  | " + $line)
        if ($line -match 'FAIL (\d+)') {
            if ([int]$Matches[1] -eq 0) { Ok 'no failing assertions' } else { Bad ($Matches[1] + ' assertions failed') }
        } else {
            Bad 'could not parse the PASS/FAIL line'
        }
        $bad = Select-String -Path $txt -Pattern '^\[FAIL\]'
        if ($bad) { $bad | ForEach-Object { Write-Host ("  | " + $_.Line) } }
    } else {
        Bad 'selftest.txt was not written'
    }
    if ($p.ExitCode -eq 0) { Ok 'selftest exit code 0' } else { Bad ("selftest exit code " + $p.ExitCode) }
}

Step '4. engine dll next to the exe'
if (Test-Path $dll) {
    Ok ("rslib_aa.dll " + [math]::Round((Get-Item $dll).Length / 1MB, 2) + " MB")
} else {
    Bad 'rslib_aa.dll is NOT next to the exe (the installer would ship without the engine)'
}

Step '5. build the installer'
$sout = & powershell -ExecutionPolicy Bypass -File (Join-Path $setup 'build-setup.ps1') 2>&1
$sout | Select-Object -Last 4 | ForEach-Object { Write-Host ("  | " + $_) }
$setupExe = Join-Path $setup 'AnkiAssistant-Setup.exe'
if (Test-Path $setupExe) {
    $mb = [math]::Round((Get-Item $setupExe).Length / 1MB, 2)
    Ok ("AnkiAssistant-Setup.exe " + $mb + " MB")
    if ($mb -lt 25) { Bad 'installer looks too small to contain the 31.9 MB engine' }
} else {
    Bad 'installer was not produced'
}

if ($Install -and (Test-Path $setupExe)) {
    Step '6. silent install + selftest of the installed copy'
    $p = Start-Process -FilePath $setupExe -ArgumentList '--silent' -Wait -PassThru
    if ($p.ExitCode -eq 0) { Ok 'installer exit code 0' } else { Bad ("installer exit code " + $p.ExitCode) }
    $instDir = Join-Path $env:LOCALAPPDATA 'Programs\AnkiAssistant'
    $instExe = Join-Path $instDir 'AnkiAssistant.exe'
    $instDll = Join-Path $instDir 'rslib_aa.dll'
    if (Test-Path $instExe) { Ok 'installed exe present' } else { Bad 'installed exe missing' }
    if (Test-Path $instDll) {
        Ok ("installed engine " + [math]::Round((Get-Item $instDll).Length / 1MB, 2) + " MB")
    } else {
        Bad 'installed engine dll missing'
    }
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AnkiAssistant'
    if (Test-Path $key) {
        $ver = (Get-ItemProperty -Path $key).DisplayVersion
        Ok ("registered version " + $ver)
    } else {
        Bad 'uninstall registry key missing'
    }
    if (Test-Path $instExe) {
        Get-Process AnkiAssistant -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
        $p2 = Start-Process -FilePath $instExe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
        $t2 = Join-Path $instDir 'selftest.txt'
        if (Test-Path $t2) {
            $l2 = (Select-String -Path $t2 -Pattern '^PASS \d+ / FAIL \d+ / SKIP \d+').Line
            Write-Host ("  | installed: " + $l2)
        }
        if ($p2.ExitCode -eq 0) { Ok 'installed selftest exit code 0' } else { Bad ("installed selftest exit " + $p2.ExitCode) }
    }
}

Write-Host ""
if ($fail -eq 0) { Write-Host 'VERIFY OK' ; exit 0 } else { Write-Host ("VERIFY FAILED (" + $fail + " problems)") ; exit 1 }
