$ErrorActionPreference = 'Stop'
$apkPath = Join-Path $PSScriptRoot 'DandelionLanding.apk'

if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    Write-Host 'DandelionLanding.apk was not found.' -ForegroundColor Red
    exit 1
}

$adb = Get-Command adb.exe -ErrorAction SilentlyContinue
if ($adb) {
    $devices = @(& $adb.Source devices | Select-String '^\S+\s+device$')
    if ($devices.Count -eq 1) {
        Write-Host 'Installing Dandelion Landing...' -ForegroundColor Cyan
        & $adb.Source install -r $apkPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host 'Installed. Open Dandelion Landing on the phone and allow notifications.' -ForegroundColor Green
        exit 0
    }
    if ($devices.Count -gt 1) {
        Write-Host 'Multiple Android devices were found. Keep one connected and retry.' -ForegroundColor Yellow
        exit 1
    }
}

Write-Host 'No connected ADB device was found.' -ForegroundColor Yellow
Write-Host 'Copy this APK to the phone and install it manually:' -ForegroundColor White
Write-Host $apkPath -ForegroundColor Cyan
Start-Process explorer.exe -ArgumentList ('/select,"' + $apkPath + '"')
