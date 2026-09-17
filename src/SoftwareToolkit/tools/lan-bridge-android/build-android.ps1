[CmdletBinding()]
param(
    [string]$AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { 'D:\Program Files\Android SDK' }),
    [string]$JavaHome = $(if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'D:\Program Files\Java\jdk-17' }),
    [string]$KeyStorePath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$buildRoot = Join-Path $repoRoot '.android-build\lan-bridge'
$sourceRoot = Join-Path $PSScriptRoot 'android-src'
$buildTools = Join-Path $AndroidSdk 'build-tools\35.0.0'
$androidJar = Join-Path $AndroidSdk 'platforms\android-35\android.jar'
$javaBin = Join-Path $JavaHome 'bin'
if (-not $KeyStorePath) { $KeyStorePath = Join-Path $repoRoot '.build-keys\lan-bridge.keystore' }

$tools = @{
    aapt2 = Join-Path $buildTools 'aapt2.exe'
    d8 = Join-Path $buildTools 'd8.bat'
    zipalign = Join-Path $buildTools 'zipalign.exe'
    apksigner = Join-Path $buildTools 'apksigner.bat'
    javac = Join-Path $javaBin 'javac.exe'
    jar = Join-Path $javaBin 'jar.exe'
    keytool = Join-Path $javaBin 'keytool.exe'
}
foreach ($entry in $tools.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) { throw "Missing build tool $($entry.Key): $($entry.Value)" }
}
if (-not (Test-Path -LiteralPath $androidJar -PathType Leaf)) { throw "Android 35 platform is missing: $androidJar" }

$expectedBuildRoot = Join-Path $repoRoot '.android-build\lan-bridge'
if (-not [string]::Equals([IO.Path]::GetFullPath($buildRoot), [IO.Path]::GetFullPath($expectedBuildRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe build directory: $buildRoot"
}
if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
New-Item -ItemType Directory -Path $buildRoot, (Join-Path $buildRoot 'classes'), (Join-Path $buildRoot 'dex'), (Join-Path $buildRoot 'gen') -Force | Out-Null

Write-Host '[1/6] Compiling Android resources'
& $tools.aapt2 compile --dir (Join-Path $sourceRoot 'res') -o (Join-Path $buildRoot 'resources.zip')
if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile failed' }

Write-Host '[2/6] Linking resources and unsigned APK'
& $tools.aapt2 link -o (Join-Path $buildRoot 'unsigned.apk') -I $androidJar --manifest (Join-Path $sourceRoot 'AndroidManifest.xml') --java (Join-Path $buildRoot 'gen') --min-sdk-version 26 --target-sdk-version 35 --version-code 2 --version-name '1.1.0' (Join-Path $buildRoot 'resources.zip')
if ($LASTEXITCODE -ne 0) { throw 'aapt2 link failed' }

Write-Host '[3/6] Compiling Java sources'
$javaFiles = @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'java'), (Join-Path $buildRoot 'gen') -Filter '*.java' -Recurse -File | ForEach-Object FullName)
& $tools.javac -encoding UTF-8 -source 8 -target 8 -classpath $androidJar -d (Join-Path $buildRoot 'classes') $javaFiles
if ($LASTEXITCODE -ne 0) { throw 'javac failed' }
Push-Location (Join-Path $buildRoot 'classes')
try { & $tools.jar cf (Join-Path $buildRoot 'classes.jar') . } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'jar failed' }

Write-Host '[4/6] Creating DEX'
& $tools.d8 --min-api 26 --lib $androidJar --output (Join-Path $buildRoot 'dex') (Join-Path $buildRoot 'classes.jar')
if ($LASTEXITCODE -ne 0) { throw 'd8 failed' }
& $tools.jar uf (Join-Path $buildRoot 'unsigned.apk') -C (Join-Path $buildRoot 'dex') classes.dex
if ($LASTEXITCODE -ne 0) { throw 'Unable to add classes.dex' }

Write-Host '[5/6] Aligning and signing'
& $tools.zipalign -f 4 (Join-Path $buildRoot 'unsigned.apk') (Join-Path $buildRoot 'aligned.apk')
if ($LASTEXITCODE -ne 0) { throw 'zipalign failed' }
if (-not (Test-Path -LiteralPath $KeyStorePath -PathType Leaf)) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($KeyStorePath)) -Force | Out-Null
    & $tools.keytool -genkeypair -keystore $KeyStorePath -storepass softwaretoolkit -keypass softwaretoolkit -alias lanbridge -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=SoftwareToolkit LAN Bridge, O=SoftwareToolkit, C=CN'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create signing key' }
}
$apkOutput = Join-Path $PSScriptRoot 'DandelionLanding.apk'
& $tools.apksigner sign --ks $KeyStorePath --ks-key-alias lanbridge --ks-pass pass:softwaretoolkit --key-pass pass:softwaretoolkit --out $apkOutput (Join-Path $buildRoot 'aligned.apk')
if ($LASTEXITCODE -ne 0) { throw 'APK signing failed' }

Write-Host '[6/6] Verifying APK'
& $tools.apksigner verify --verbose $apkOutput
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
Write-Host "Completed: $apkOutput ($([Math]::Round((Get-Item -LiteralPath $apkOutput).Length / 1KB, 1)) KB)" -ForegroundColor Green
