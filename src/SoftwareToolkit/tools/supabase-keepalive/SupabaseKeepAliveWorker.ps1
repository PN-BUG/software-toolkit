param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$appDir = Join-Path $env:LOCALAPPDATA 'SoftwareToolkit\SupabaseKeepAlive'
$logPath = Join-Path $appDir 'worker.log'
$mutex = [Threading.Mutex]::new($false, 'Local\SoftwareToolkit_SupabaseKeepAlive_v2')
$hasMutex = $false

function Write-KeepAliveLog {
    param([string]$Message, [string]$Level = 'INFO')
    if (-not (Test-Path -LiteralPath $appDir)) {
        New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    }
    if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 524288) {
        Move-Item -LiteralPath $logPath -Destination "$logPath.previous" -Force
    }
    $safeMessage = $Message -replace '[\r\n]+', ' '
    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ('{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $safeMessage)
}

function Test-PrivilegedKey {
    param([string]$Key)
    if ($Key -like 'sb_secret_*') { return $true }
    $parts = $Key.Split('.')
    if ($parts.Count -ne 3) { return $false }
    try {
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        switch ($payload.Length % 4) { 2 { $payload += '==' } 3 { $payload += '=' } }
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        return [string]$json.role -eq 'service_role'
    }
    catch { return $false }
}

function Read-KeepAliveConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw "配置文件不存在：$ConfigPath" }
    $config = Get-Content -Raw -LiteralPath $ConfigPath -Encoding UTF8 | ConvertFrom-Json
    if (-not $config.email -or -not $config.passwordProtected) { throw '配置缺少邮箱或加密密码' }
    if (-not $config.targets -or @($config.targets).Count -eq 0) { throw '配置中没有 Supabase 项目' }
    $secure = ConvertTo-SecureString ([string]$config.passwordProtected)
    $credential = [PSCredential]::new('keepalive', $secure)
    $password = $credential.GetNetworkCredential().Password
    if ([string]::IsNullOrWhiteSpace($password)) { throw '无法读取加密密码，请回到工具中重新保存' }
    return [pscustomobject]@{ Config = $config; Password = $password }
}

function Invoke-PostJson {
    param([string]$Uri, [hashtable]$Headers, [string]$Body, [int]$TimeoutSec)
    Add-Type -AssemblyName System.Net.Http
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(3, [Math]::Min(120, $TimeoutSec)))
    $request = $null
    $response = $null
    try {
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $Uri)
        foreach ($entry in $Headers.GetEnumerator()) {
            [void]$request.Headers.TryAddWithoutValidation([string]$entry.Key, [string]$entry.Value)
        }
        $request.Content = [Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json')
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            $detail = ($responseBody -replace '[\r\n]+', ' ').Trim()
            if ($detail.Length -gt 300) { $detail = $detail.Substring(0, 300) + '…' }
            throw "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)$(if ($detail) { ": $detail" })"
        }
        return $responseBody
    }
    finally {
        if ($request) { $request.Dispose() }
        if ($response) { $response.Dispose() }
        $client.Dispose()
        $handler.Dispose()
    }
}

try {
    $hasMutex = $mutex.WaitOne(0)
    if (-not $hasMutex) {
        Write-KeepAliveLog '已有保活任务正在运行，本次触发已跳过' 'WARN'
        exit 0
    }

    $loaded = Read-KeepAliveConfig
    $config = $loaded.Config
    $tableName = if ($config.tableName) { [string]$config.tableName } else { 'test' }
    if ($tableName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "非法表名：$tableName" }
    $timeout = if ($config.timeoutSec) { [int]$config.timeoutSec } else { 12 }
    $targetDelay = if ($null -ne $config.targetIntervalSeconds) { [double]$config.targetIntervalSeconds } else { 0.2 }
    $success = 0
    $failed = 0
    Write-KeepAliveLog "开始执行，共 $(@($config.targets).Count) 个项目"

    foreach ($target in @($config.targets)) {
        $name = if ($target.name) { [string]$target.name } else { [string]$target.url }
        $stage = '配置检查'
        try {
            if (-not $target.url -or -not $target.apiKey) { throw 'URL 或 API Key 为空' }
            $key = [string]$target.apiKey
            if (Test-PrivilegedKey $key) { throw '禁止使用 secret/service_role key，请改用 publishable/anon key' }
            $uri = $null
            if (-not [Uri]::TryCreate(([string]$target.url).TrimEnd('/'), [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') {
                throw '项目 URL 必须是有效的 HTTPS 地址'
            }
            if ($uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw '项目 URL 不能包含用户信息、查询参数或片段' }
            $baseUrl = $uri.AbsoluteUri.TrimEnd('/')
            $headers = @{ apikey = $key }
            $authBody = @{ email = [string]$config.email; password = $loaded.Password } | ConvertTo-Json -Compress
            $stage = '登录'
            $authText = Invoke-PostJson -Uri "$baseUrl/auth/v1/token?grant_type=password" -Headers $headers -Body $authBody -TimeoutSec $timeout
            $auth = $authText | ConvertFrom-Json
            if (-not $auth.access_token) { throw '登录响应缺少 access_token' }

            $now = [DateTime]::UtcNow.ToString('o')
            $rowBody = @{ created_at = $now; change_time = $now } | ConvertTo-Json -Compress
            $writeHeaders = @{ apikey = $key; Authorization = "Bearer $($auth.access_token)"; Prefer = 'return=minimal' }
            $stage = "写入表 $tableName"
            $escapedTable = [Uri]::EscapeDataString($tableName)
            [void](Invoke-PostJson -Uri "$baseUrl/rest/v1/$escapedTable" -Headers $writeHeaders -Body $rowBody -TimeoutSec $timeout)
            $success++
            Write-KeepAliveLog "$name 保活成功"
        }
        catch {
            $failed++
            Write-KeepAliveLog "$name $stage 失败：$($_.Exception.Message)" 'ERROR'
        }
        if ($targetDelay -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(60000, [int]($targetDelay * 1000))) }
    }

    Write-KeepAliveLog "执行完成：成功 $success，失败 $failed" $(if ($failed -gt 0) { 'WARN' } else { 'INFO' })
    if ($failed -gt 0) { exit 1 }
}
catch {
    Write-KeepAliveLog "任务终止：$($_.Exception.Message)" 'ERROR'
    exit 1
}
finally {
    if ($hasMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
