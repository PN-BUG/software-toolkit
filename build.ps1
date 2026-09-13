[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$Zip,
    [switch]$OpenOutput,
    [switch]$Pause
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\SoftwareToolkit\SoftwareToolkit.csproj'
# Each invocation gets a fresh directory: no stale files, no overwriting user tools/state.
$mode = if ($SelfContained) { 'standalone' } else { 'portable' }
$packageName = "SoftwareToolkit-$Runtime-$mode-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
$outputDir = Join-Path $PSScriptRoot "publish\$packageName"
$logPath = "$outputDir.log"
$watch = [Diagnostics.Stopwatch]::StartNew()

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET 8 SDK is required. Install it before running this script.'
    }
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Project not found: $projectPath"
    }
    New-Item -ItemType Directory -Path (Split-Path $outputDir) -Force | Out-Null
    Write-Host "[1/4] Publishing $Configuration / $Runtime / $mode"
    Write-Host "Log: $logPath"
    $publishArgs = @('publish', $projectPath, '-c', $Configuration, '-r', $Runtime,
        '-o', $outputDir, '--nologo', '-v', 'minimal',
        "--self-contained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())")
    Push-Location $PSScriptRoot
    try {
        & dotnet @publishArgs 2>&1 | Tee-Object -FilePath $logPath
        $publishExitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($publishExitCode -ne 0) { throw "dotnet publish failed (exit $publishExitCode). See $logPath" }

    Write-Host '[2/4] Checking package files'
    foreach ($required in @('SoftwareToolkit.exe', 'tools.json', 'tools\lan-share\manifest.json',
        'tools\software-inventory\manifest.json', 'tools\ai-manager\manifest.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDir $required) -PathType Leaf)) {
            throw "Missing package file: $required"
        }
    }
    foreach ($localOnly in @('tools\mouseinc', 'tools\SoftwareToolkit')) {
        if (Test-Path -LiteralPath (Join-Path $outputDir $localOnly)) {
            throw "Local-only tool leaked into package: $localOnly"
        }
    }
    # Check all bundled resources, including scripts used by the built-in tools.
    $sourceDir = Split-Path $projectPath
    $resources = @((Get-Item -LiteralPath (Join-Path $sourceDir 'tools.json')))
    $resources += @(Get-ChildItem -LiteralPath (Join-Path $sourceDir 'tools') -Recurse -File -Force |
        Where-Object {
            $sourceRelativePath = $_.FullName.Substring($sourceDir.Length + 1)
            $sourceRelativePath -notmatch '^tools\\(mouseinc|SoftwareToolkit)\\'
        })
    foreach ($resource in $resources) {
        $relativePath = $resource.FullName.Substring($sourceDir.Length + 1)
        $publishedPath = Join-Path $outputDir $relativePath
        if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            throw "Missing package resource: $relativePath"
        }
        if ((Get-FileHash -LiteralPath $resource.FullName).Hash -ne (Get-FileHash -LiteralPath $publishedPath).Hash) {
            throw "Package resource does not match source: $relativePath"
        }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $outputDir
    # The generated apphost detects runtime architecture/version and provides installation guidance.
    # Avoid a second, unreliable CLI/registry runtime detector.
    $launcher = "@echo off`r`nstart `"`" `"%~dp0SoftwareToolkit.exe`" %*`r`n"
    Set-Content -LiteralPath (Join-Path $outputDir 'SoftwareToolkit.bat') -Value $launcher -Encoding ASCII
    $runtimeNote = if ($SelfContained) { 'Runtime included.' } else { ".NET 8 Desktop Runtime ($Runtime) required. https://dotnet.microsoft.com/download/dotnet/8.0" }
    Set-Content -LiteralPath (Join-Path $outputDir 'START-HERE.txt') -Encoding UTF8 -Value @"
SoftwareToolkit
Run SoftwareToolkit.exe. Keep tools.json and tools/ beside the executable.
$runtimeNote
Ctrl+Alt+T: show/hide. Ctrl+F: search. Enter: launch. Esc: clear search/cancel selection. F5: refresh.
Back up your tools.json, tools/ and user state before upgrading an existing installation.
"@
    Write-Host '[3/4] Generating file checksums'
    $files = @(Get-ChildItem -LiteralPath $outputDir -Recurse -File -Force | Sort-Object FullName)
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    $manifest = [ordered]@{
        configuration = $Configuration
        runtime = $Runtime
        selfContained = $SelfContained.IsPresent
        createdUtc = [DateTime]::UtcNow.ToString('o')
        totalBytes = $bytes
        files = @($files | ForEach-Object {
            [ordered]@{
                path = $_.FullName.Substring($outputDir.Length + 1).Replace('\', '/')
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDir 'package-manifest.json') -Encoding UTF8
    if ($Zip) {
        Write-Host '[4/4] Creating ZIP archive'
        $archivePath = "$outputDir.zip"
        # ZipFile includes hidden resources that Compress-Archive silently omits.
        Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
        $partialArchivePath = "$archivePath.partial"
        $archive = [IO.Compression.ZipFile]::Open($partialArchivePath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $outputDir -Recurse -File -Force | Sort-Object FullName)) {
                # Explicit forward slashes also work with Windows PowerShell's older .NET runtime.
                $entryName = $packageName + '/' + $file.FullName.Substring($outputDir.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
                    $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $archive.Dispose() }
        Move-Item -LiteralPath $partialArchivePath -Destination $archivePath
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($archivePath) |
            Set-Content -LiteralPath "$archivePath.sha256" -Encoding ASCII
        Write-Host "Archive: $archivePath"
    }
    else { Write-Host '[4/4] ZIP skipped (use -Zip to enable)' }
    $watch.Stop()
    Write-Host ("SUCCESS: {0:N2} MB, {1} files, {2:N1}s" -f ($bytes / 1MB), $files.Count, $watch.Elapsed.TotalSeconds) -ForegroundColor Green
    Write-Host "Output: $outputDir"
    if ($OpenOutput) { Invoke-Item -LiteralPath $outputDir }
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($Pause) { Read-Host 'Press Enter to exit' | Out-Null }
}
