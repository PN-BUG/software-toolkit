# ============================================
#  lan-share.ps1
#  LAN file sharing server (pure PowerShell + .NET HttpListener)
#
#  Usage:
#    .\lan-share.ps1                              (share current dir, port 8088)
#    .\lan-share.ps1 -SharePath D:\MyShare        (share a specific dir)
#    .\lan-share.ps1 -SharePath D:\MyShare -Port 9000
#    .\lan-share.ps1 -SharePath D:\Files -ReadOnly -Token mysecret
#
#  Drag & drop: drag a folder onto lan-share.bat
# ============================================

param(
    [string]$SharePath = "",
    [int]$Port = 8088,
    [switch]$ReadOnly,
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"

# ---------- Validate port ----------
if ($Port -lt 1 -or $Port -gt 65535) {
    Write-Host "  WARNING: Invalid port $Port, using default 8088" -ForegroundColor Yellow
    $Port = 8088
}

# ---------- Resolve share path ----------
if ([string]::IsNullOrWhiteSpace($SharePath)) {
    $SharePath = (Get-Location).Path
}
if (-not [System.IO.Path]::IsPathRooted($SharePath)) {
    $SharePath = Join-Path (Get-Location) $SharePath
}
$SharePath = [System.IO.Path]::GetFullPath($SharePath).TrimEnd('\','/')

if (-not (Test-Path -LiteralPath $SharePath -PathType Container)) {
    Write-Host "  ERROR: Share path does not exist or is not a directory: $SharePath" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# ---------- Get local IPv4 addresses ----------
function Get-LocalIPs {
    $ips = @()
    try {
        # Prefer Get-NetIPConfiguration (Win8+)
        $nets = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
                Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' }
        foreach ($n in $nets) {
            foreach ($a in $n.IPv4Address) {
                if ($a.IPAddress -match '^\d+\.\d+\.\d+\.\d+$') { $ips += $a.IPAddress }
            }
        }
    } catch {}
    if (-not $ips) {
        try {
            $props = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                     Where-Object { $_.IPAddress -notmatch '^(169\.|127\.)' }
            foreach ($p in $props) { $ips += $p.IPAddress }
        } catch {}
    }
    if (-not $ips) {
        # Fallback: classic .NET Dns
        try {
            $hostEntry = [System.Net.Dns]::GetHostEntry([System.Net.Dns]::GetHostName())
            foreach ($a in $hostEntry.AddressList) {
                if ($a.AddressFamily -eq 'InterNetwork') { $ips += $a.ToString() }
            }
        } catch {}
    }
    return ,@($ips | Select-Object -Unique)
}

# ---------- URL-encode ----------
function Url-Encode([string]$s) {
    return [System.Uri]::EscapeDataString($s)
}

# ---------- HTML escape ----------
function Html-Encode([string]$s) {
    return $s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;' -replace '"','&quot;'
}

# ---------- Human readable size ----------
function Format-Size([long]$bytes) {
    if ($bytes -ge 1GB) { return "{0:N2} GB" -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return "{0:N2} MB" -f ($bytes / 1MB) }
    if ($bytes -ge 1KB) { return "{0:N2} KB" -f ($bytes / 1KB) }
    return "$bytes B"
}

# ---------- Build file list HTML ----------
function Build-ListingHtml([string]$absDir, [string]$relUrl) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!DOCTYPE html><html lang="zh-CN"><head><meta charset="UTF-8">')
    [void]$sb.AppendLine('<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">')
    [void]$sb.AppendLine('<title>局域网文件共享</title>')
    [void]$sb.AppendLine('<style>')
    [void]$sb.AppendLine('*{box-sizing:border-box;margin:0;padding:0}')
    [void]$sb.AppendLine('body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,"Microsoft YaHei",sans-serif;background:#f5f7fa;color:#222;padding:16px;max-width:960px;margin:0 auto}')
    [void]$sb.AppendLine('header{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;margin-bottom:16px;padding-bottom:12px;border-bottom:2px solid #4a90d9}')
    [void]$sb.AppendLine('h1{font-size:20px;color:#2c5aa0;display:flex;align-items:center;gap:8px}')
    [void]$sb.AppendLine('.breadcrumb{font-size:13px;color:#666;margin-bottom:12px;word-break:break-all}')
    [void]$sb.AppendLine('.breadcrumb a{color:#4a90d9;text-decoration:none}')
    [void]$sb.AppendLine('.breadcrumb a:hover{text-decoration:underline}')
    [void]$sb.AppendLine('.toolbar{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:12px}')
    [void]$sb.AppendLine('.btn{padding:8px 16px;border:none;border-radius:6px;cursor:pointer;font-size:14px;font-weight:500;transition:all .15s}')
    [void]$sb.AppendLine('.btn-primary{background:#4a90d9;color:#fff}')
    [void]$sb.AppendLine('.btn-primary:hover{background:#3a7bc8}')
    [void]$sb.AppendLine('.btn-secondary{background:#e0e6ed;color:#333}')
    [void]$sb.AppendLine('.btn-secondary:hover{background:#d0d8e0}')
    [void]$sb.AppendLine('table{width:100%;border-collapse:collapse;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.08)}')
    [void]$sb.AppendLine('th,td{padding:10px 12px;text-align:left;border-bottom:1px solid #eee}')
    [void]$sb.AppendLine('th{background:#f0f4f8;font-size:13px;color:#555;font-weight:600}')
    [void]$sb.AppendLine('td{font-size:14px}')
    [void]$sb.AppendLine('tr:last-child td{border-bottom:none}')
    [void]$sb.AppendLine('tr:hover{background:#f8fbff}')
    [void]$sb.AppendLine('.icon{display:inline-block;width:20px;text-align:center;margin-right:6px}')
    [void]$sb.AppendLine('a{color:#4a90d9;text-decoration:none}')
    [void]$sb.AppendLine('a:hover{text-decoration:underline}')
    [void]$sb.AppendLine('.size{color:#888;font-variant-numeric:tabular-nums;white-space:nowrap}')
    [void]$sb.AppendLine('.time{color:#aaa;font-size:12px;white-space:nowrap}')
    [void]$sb.AppendLine('.empty{text-align:center;padding:40px;color:#999}')
    [void]$sb.AppendLine('#drop{border:2px dashed #b0c4de;border-radius:8px;padding:24px;text-align:center;color:#666;margin-bottom:12px;transition:all .2s}')
    [void]$sb.AppendLine('#drop.over{border-color:#4a90d9;background:#eaf3ff;color:#2c5aa0}')
    [void]$sb.AppendLine('#fileInput{display:none}')
    [void]$sb.AppendLine('.progress{height:6px;background:#e0e6ed;border-radius:3px;overflow:hidden;margin-top:8px;display:none}')
    [void]$sb.AppendLine('.progress-bar{height:100%;background:#4a90d9;width:0;transition:width .2s}')
    [void]$sb.AppendLine('.msg{padding:10px 14px;border-radius:6px;margin:8px 0;font-size:13px}')
    [void]$sb.AppendLine('.msg-ok{background:#e6f7ed;color:#1a7d3a;border:1px solid #b8e6c8}')
    [void]$sb.AppendLine('.msg-err{background:#fdecea;color:#b71c1c;border:1px solid #f5c6cb}')
    [void]$sb.AppendLine('footer{margin-top:20px;padding-top:12px;border-top:1px solid #e0e6ed;font-size:12px;color:#999;text-align:center}')
    [void]$sb.AppendLine('@media(max-width:600px){th.time,td.time{display:none}body{padding:10px}h1{font-size:18px}}')
    [void]$sb.AppendLine('</style></head><body>')

    [void]$sb.AppendLine('<header><h1>📁 局域网文件共享</h1>')
    if (-not $ReadOnly) {
        [void]$sb.AppendLine('<button class="btn btn-primary" onclick="document.getElementById(''fileInput'').click()">⬆ 上传文件</button>')
    }
    [void]$sb.AppendLine('</header>')

    # Breadcrumb
    [void]$sb.Append('<div class="breadcrumb">📍 <a href="' + $script:RootUrl + '">根目录</a>')
    if ($relUrl -ne '/') {
        $parts = $relUrl.TrimStart('/').Split('/')
        $acc = ''
        for ($i = 0; $i -lt $parts.Length; $i++) {
            $p = $parts[$i]
            if ([string]::IsNullOrEmpty($p)) { continue }
            $acc += '/' + (Url-Encode $p)
            [void]$sb.Append(' / <a href="' + $script:RootUrl + $acc.TrimStart('/') + '/">' + (Html-Encode $p) + '</a>')
        }
    }
    [void]$sb.AppendLine('</div>')

    # Upload form (hidden file input + drop zone)
    if (-not $ReadOnly) {
        [void]$sb.AppendLine('<div id="drop">📂 拖拽文件到这里上传,或点击上方按钮选择</div>')
        [void]$sb.AppendLine('<input type="file" id="fileInput" multiple>')
        [void]$sb.AppendLine('<div class="progress" id="prog"><div class="progress-bar" id="progBar"></div></div>')
        [void]$sb.AppendLine('<div id="msgBox"></div>')
    }

    # File table
    [void]$sb.AppendLine('<table><thead><tr><th>名称</th><th class="size">大小</th><th class="time">修改时间</th></tr></thead><tbody>')

    # Parent dir link
    if ($relUrl -ne '/') {
        $parentUrl = $script:RootUrl + ([string]::Join('/', $relUrl.TrimStart('/').Split('/')[0..($relUrl.TrimStart('/').Split('/').Length - 2)]))
        if ($parentUrl -notmatch '/$') { $parentUrl += '/' }
        [void]$sb.AppendLine('<tr><td colspan="3"><span class="icon">📁</span><a href="' + $parentUrl + '">.. (上级目录)</a></td></tr>')
    }

    $entries = Get-ChildItem -LiteralPath $absDir -Force | Sort-Object @{Expression='PSIsContainer';Descending=$true}, Name
    $hasContent = $false
    foreach ($e in $entries) {
        $hasContent = $true
        $name = $e.Name
        $encName = Url-Encode $name
        $href = $script:RootUrl + $relUrl.TrimStart('/') + $encName
        $icon = if ($e.PSIsContainer) { '📁' } else { '📄' }
        if ($e.PSIsContainer) { $href += '/' }
        $size = if ($e.PSIsContainer) { '—' } else { (Format-Size $e.Length) }
        $time = $e.LastWriteTime.ToString('yyyy-MM-dd HH:mm')
        [void]$sb.AppendLine('<tr><td><span class="icon">' + $icon + '</span><a href="' + $href + '">' + (Html-Encode $name) + '</a></td>')
        [void]$sb.AppendLine('<td class="size">' + $size + '</td><td class="time">' + $time + '</td></tr>')
    }
    if (-not $hasContent) {
        [void]$sb.AppendLine('<tr><td colspan="3" class="empty">📭 此目录为空</td></tr>')
    }
    [void]$sb.AppendLine('</tbody></table>')

    # JS for upload
    if (-not $ReadOnly) {
        $uploadUrl = $script:RootUrl + $relUrl.TrimStart('/')
        if ($uploadUrl -notmatch '/$') { $uploadUrl += '/' }
        $uploadUrl += '__upload'
        [void]$sb.AppendLine(@"
<script>
(function(){
  var input=document.getElementById('fileInput');
  var drop=document.getElementById('drop');
  var prog=document.getElementById('prog');
  var bar=document.getElementById('progBar');
  var box=document.getElementById('msgBox');
  function upload(files){
    if(!files||!files.length)return;
    var fd=new FormData();
    for(var i=0;i<files.length;i++)fd.append('files',files[i],files[i].name);
    var xhr=new XMLHttpRequest();
    prog.style.display='block';bar.style.width='0';
    xhr.open('POST','$uploadUrl',true);
    xhr.upload.onprogress=function(e){if(e.lengthComputable){bar.style.width=(e.loaded/e.total*100)+'%'}};
    xhr.onload=function(){
      prog.style.display='none';
      if(xhr.status>=200&&xhr.status<300){showMsg('上传成功: '+xhr.responseText,'ok');setTimeout(function(){location.reload()},800)}
      else{showMsg('上传失败: '+xhr.responseText,'err')}
    };
    xhr.onerror=function(){prog.style.display='none';showMsg('网络错误','err')};
    xhr.send(fd);
  }
  input.addEventListener('change',function(){upload(input.files)});
  ['dragenter','dragover'].forEach(function(ev){drop.addEventListener(ev,function(e){e.preventDefault();drop.classList.add('over')})});
  ['dragleave','drop'].forEach(function(ev){drop.addEventListener(ev,function(e){e.preventDefault();drop.classList.remove('over')})});
  drop.addEventListener('click',function(){input.click()});
  drop.addEventListener('drop',function(e){upload(e.dataTransfer.files)});
  function showMsg(t,c){var d=document.createElement('div');d.className='msg msg-'+c;d.textContent=t;box.innerHTML='';box.appendChild(d)}
})();
</script>
"@)
    }

    [void]$sb.AppendLine('<footer>Powered by SoftwareToolkit · lan-share · 纯 PowerShell HTTP 服务器</footer>')
    [void]$sb.AppendLine('</body></html>')
    return $sb.ToString()
}

# ---------- Parse multipart/form-data ----------
function Parse-Multipart([System.IO.Stream]$body, [string]$boundary) {
    $files = @()
    $enc = [System.Text.Encoding]::UTF8
    $boundaryBytes = $enc.GetBytes("--" + $boundary)
    $crlf = $enc.GetBytes("`r`n")
    $crlfcrlf = $enc.GetBytes("`r`n`r`n")
    $ms = New-Object System.IO.MemoryStream
    $body.CopyTo($ms)
    $data = $ms.ToArray()
    $ms.Dispose()

    # Split by boundary
    $positions = @()
    $startIdx = 0
    while ($true) {
        $idx = Find-Bytes $data $boundaryBytes $startIdx
        if ($idx -lt 0) { break }
        $positions += $idx
        $startIdx = $idx + $boundaryBytes.Length
    }

    for ($i = 0; $i -lt $positions.Length - 1; $i++) {
        # Skip boundary line
        $partStart = $positions[$i] + $boundaryBytes.Length
        # Skip CRLF after boundary
        if ($partStart + 2 -le $data.Length -and $data[$partStart] -eq 13 -and $data[$partStart+1] -eq 10) {
            $partStart += 2
        }
        $partEnd = $positions[$i + 1]
        if ($partEnd -le $partStart) { continue }

        # Find header/body separator (CRLF CRLF = 4 bytes)
        $sep = Find-Bytes $data $crlfcrlf $partStart
        if ($sep -lt 0 -or $sep -ge $partEnd) { continue }
        $headerStr = $enc.GetString($data, $partStart, $sep - $partStart)
        $bodyStart = $sep + 4
        # Body ends with CRLF before next boundary
        $bodyEnd = $partEnd - 2
        if ($bodyEnd -lt $bodyStart) { continue }

        # Parse Content-Disposition
        if ($headerStr -match 'Content-Disposition:.*filename="([^"]+)"') {
            $filename = $matches[1]
            # Trim trailing CRLF if any
            $len = $bodyEnd - $bodyStart
            $fileBytes = New-Object byte[] $len
            [Array]::Copy($data, $bodyStart, $fileBytes, 0, $len)
            $files += [PSCustomObject]@{ Name = $filename; Bytes = $fileBytes }
        }
    }
    return $files
}

function Find-Bytes([byte[]]$haystack, [byte[]]$needle, [int]$start) {
    $end = $haystack.Length - $needle.Length
    for ($i = $start; $i -le $end; $i++) {
        $match = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($haystack[$i + $j] -ne $needle[$j]) { $match = $false; break }
        }
        if ($match) { return $i }
    }
    return -1
}

# ---------- MIME type helper ----------
function Get-MimeType([string]$ext) {
    $ext = $ext.ToLowerInvariant()
    $map = @{
        '.jpg'='image/jpeg'; '.jpeg'='image/jpeg'; '.png'='image/png'; '.gif'='image/gif'
        '.bmp'='image/bmp'; '.webp'='image/webp'; '.svg'='image/svg+xml'; '.ico'='image/x-icon'
        '.mp4'='video/mp4'; '.webm'='video/webm'; '.ogg'='video/ogv'; '.ogv'='video/ogv'
        '.mp3'='audio/mpeg'; '.wav'='audio/wav'; '.oga'='audio/ogg'; '.flac'='audio/flac'; '.aac'='audio/aac'
        '.pdf'='application/pdf'
        '.txt'='text/plain'; '.log'='text/plain'; '.csv'='text/csv'
        '.json'='application/json'; '.xml'='text/xml'
        '.html'='text/html'; '.htm'='text/html'; '.css'='text/css'
        '.js'='application/javascript'; '.mjs'='application/javascript'
        '.md'='text/markdown'; '.markdown'='text/markdown'
    }
    if ($map.ContainsKey($ext)) { return $map[$ext] }
    return 'application/octet-stream'
}

# ---------- Send response helpers ----------
function Send-Text([System.Net.HttpListenerResponse]$resp, [string]$text, [int]$code = 200, [string]$contentType = 'text/plain; charset=utf-8') {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $resp.StatusCode = $code
    $resp.ContentType = $contentType
    $resp.ContentLength64 = $bytes.Length
    $resp.OutputStream.Write($bytes, 0, $bytes.Length)
    $resp.OutputStream.Close()
}

function Send-Html([System.Net.HttpListenerResponse]$resp, [string]$html, [int]$code = 200) {
    Send-Text $resp $html $code 'text/html; charset=utf-8'
}

function Send-Error([System.Net.HttpListenerResponse]$resp, [int]$code, [string]$msg) {
    $html = '<!DOCTYPE html><html><head><meta charset="UTF-8"><title>Error</title><style>body{font-family:sans-serif;text-align:center;padding:60px;color:#b71c1c}h1{font-size:48px;margin:0}p{margin:12px 0;color:#666}a{color:#4a90d9;text-decoration:none}</style></head><body><h1>' + $code + '</h1><p>' + (Html-Encode $msg) + '</p><p><a href="javascript:history.back()">返回</a></p></body></html>'
    Send-Html $resp $html $code
}

function Send-Json([System.Net.HttpListenerResponse]$resp, $data, [int]$code = 200) {
    $json = ConvertTo-Json $data -Depth 10 -Compress
    Send-Text $resp $json $code 'application/json; charset=utf-8'
}

# ---------- Build JSON file list ----------
function Build-FileListJson([string]$absDir) {
    $entries = Get-ChildItem -LiteralPath $absDir -Force | Sort-Object @{Expression='PSIsContainer';Descending=$true}, Name
    $result = @()
    foreach ($e in $entries) {
        $item = @{
            name = $e.Name
            isDir = $e.PSIsContainer
            size = if ($e.PSIsContainer) { 0 } else { $e.Length }
            lastModified = $e.LastWriteTime.ToString('o')
        }
        $result += $item
    }
    return $result
}

# ---------- Main ----------
$ips = Get-LocalIPs
$primaryIp = if ($ips) { $ips[0] } else { '127.0.0.1' }

# Try to bind 0.0.0.0 (needs URL ACL on some systems); fallback to localhost-only
$listener = $null
$boundPrefix = $null
$actualPort = $Port
$maxRetries = 5

for ($retry = 0; $retry -lt $maxRetries; $retry++) {
    $tryPort = $Port + $retry
    $prefixes = @("http://+:$tryPort/", "http://*:$tryPort/")
    foreach ($pfx in $prefixes) {
        try {
            $listener = New-Object System.Net.HttpListener
            $listener.Prefixes.Add($pfx)
            $listener.Start()
            $boundPrefix = $pfx
            $actualPort = $tryPort
            break
        } catch {
            if ($listener) { try { $listener.Close() } catch {} }
            $listener = $null
        }
    }
    if ($listener) { break }
}
if (-not $listener) {
    # Fallback: localhost only (no admin needed, but LAN clients can't reach)
    for ($retry = 0; $retry -lt $maxRetries; $retry++) {
        $tryPort = $Port + $retry
        try {
            $listener = New-Object System.Net.HttpListener
            $listener.Prefixes.Add("http://127.0.0.1:$tryPort/")
            $listener.Start()
            $boundPrefix = "http://127.0.0.1:$tryPort/"
            $actualPort = $tryPort
            Write-Host "  [Warning] Could not bind 0.0.0.0:$tryPort (need URL ACL or admin)." -ForegroundColor Yellow
            Write-Host "  Running in localhost-only mode. LAN devices CANNOT reach this server." -ForegroundColor Yellow
            Write-Host "  Fix: run as admin:  netsh http add urlacl url=http://+:$tryPort/ user=Everyone" -ForegroundColor DarkYellow
            break
        } catch {
            if ($listener) { try { $listener.Close() } catch {} }
            $listener = $null
        }
    }
    if (-not $listener) {
        Write-Host "  ERROR: Failed to start HTTP listener (ports $Port-$($Port+$maxRetries-1) all unavailable)" -ForegroundColor Red
        Write-Host "  Another instance may be running, or ports are in use." -ForegroundColor DarkRed
        Write-Host "  Press Enter to exit..." -ForegroundColor Gray
        Read-Host
        exit 1
    }
}
$Port = $actualPort

# Root URL used in HTML (relative, so any host works)
$script:RootUrl = '/'

# Resolve path to index.html (same directory as this script)
$scriptDir = [System.IO.Path]::GetDirectoryName((Get-Item -LiteralPath $PSCommandPath).FullName)
$indexHtmlPath = Join-Path $scriptDir 'index.html'
$indexHtmlBytes = $null
if (Test-Path -LiteralPath $indexHtmlPath -PathType Leaf) {
    $indexHtmlBytes = [System.IO.File]::ReadAllBytes($indexHtmlPath)
    Write-Host "  [OK] SPA index.html loaded ($([math]::Round($indexHtmlBytes.Length/1KB,1)) KB)" -ForegroundColor DarkGray
} else {
    Write-Host "  [Warn] index.html not found at $indexHtmlPath, falling back to server-rendered HTML" -ForegroundColor Yellow
}

# Load qrcode.min.js for QR code generation
$qrJsPath = Join-Path $scriptDir 'qrcode.min.js'
$qrJsBytes = $null
if (Test-Path -LiteralPath $qrJsPath -PathType Leaf) {
    $qrJsBytes = [System.IO.File]::ReadAllBytes($qrJsPath)
    Write-Host "  [OK] qrcode.min.js loaded ($([math]::Round($qrJsBytes.Length/1KB,1)) KB)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  🌐 LAN File Share" -ForegroundColor Cyan
Write-Host "  " + ("-" * 50) -ForegroundColor DarkGray
Write-Host "  共享目录:   $SharePath" -ForegroundColor White
Write-Host "  端口:       $Port" -ForegroundColor White
Write-Host "  模式:       $(if($ReadOnly){'只读'}else{'可读写(上传+下载)'})" -ForegroundColor White
if ($Token) { Write-Host "  访问令牌:   $Token (URL 加 ?t=$Token)" -ForegroundColor Yellow }
Write-Host "  " + ("-" * 50) -ForegroundColor DarkGray
Write-Host "  本机访问:   http://127.0.0.1:$Port/" -ForegroundColor Green
if ($ips) {
    foreach ($ip in $ips) {
        Write-Host "  局域网访问: http://${ip}:$Port/" -ForegroundColor Green
    }
} else {
    Write-Host "  局域网访问: (未检测到局域网 IP)" -ForegroundColor DarkYellow
}
Write-Host "  " + ("-" * 50) -ForegroundColor DarkGray
Write-Host "  📱 手机连接同一 WiFi 后,在浏览器输入上面的局域网地址" -ForegroundColor Gray
Write-Host "  按 Ctrl+C 停止服务" -ForegroundColor Gray
Write-Host ""

# ---------- Auto-open browser ----------
try {
    $openUrl = "http://127.0.0.1:$Port/"
    Start-Process $openUrl
    Write-Host "  [OK] 已打开浏览器: $openUrl" -ForegroundColor DarkGray
} catch {
    Write-Host "  [Info] 请手动打开浏览器访问 http://127.0.0.1:$Port/" -ForegroundColor DarkGray
}
Write-Host ""

# ---------- Device discovery & chat globals ----------
$script:DeviceId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$script:DeviceName = $env:COMPUTERNAME
$script:Devices = @{}   # key=deviceId, value=@{id,name,ip,port,lastSeen}
$script:Clients = @{}   # key=clientId, value=@{id,name,ip,lastSeen} — browser clients
$script:Messages = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$script:Transfers = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$script:UdpBroadcastPort = 8098
$script:LastBroadcast = [datetime]::MinValue
$script:BroadcastInterval = 5  # seconds

# ---------- UDP broadcast helpers ----------
function Send-UdpBroadcast {
    param([int]$Port)
    try {
        $msg = @{
            id   = $script:DeviceId
            name = $script:DeviceName
            port = $Port
        } | ConvertTo-Json -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.EnableBroadcast = $true
        $ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Broadcast, $script:UdpBroadcastPort)
        $udp.Send($bytes, $bytes.Length, $ep) | Out-Null
        $udp.Close()
    } catch {}
}

function Process-UdpBroadcast {
    param([string]$Json, [string]$SenderIp)
    try {
        $data = $Json | ConvertFrom-Json
        if ($data.id -eq $script:DeviceId) { return }  # ignore self
        $script:Devices[$data.id] = @{
            id       = $data.id
            name     = $data.name
            ip       = $SenderIp
            port     = [int]$data.port
            lastSeen = [datetime]::UtcNow
        }
    } catch {}
}

# Log file
$logFile = Join-Path $env:TEMP "lan-share.log"

# Setup UDP listener for receiving broadcasts (gracefully handle port conflict)
$udpListener = $null
try {
    $udpListener = New-Object System.Net.Sockets.UdpClient $script:UdpBroadcastPort
    $udpListener.EnableBroadcast = $true
    Write-Host "  [OK] UDP broadcast listener on port $script:UdpBroadcastPort" -ForegroundColor DarkGray
} catch {
    Write-Host "  [WARN] UDP port $script:UdpBroadcastPort in use, discovery disabled" -ForegroundColor DarkYellow
}

try {
    # Start first async HTTP request BEFORE the loop
    $ar = $listener.BeginGetContext($null, $null)
    while ($listener.IsListening) {
        # --- UDP broadcast send (periodic) ---
        if (([datetime]::UtcNow - $script:LastBroadcast).TotalSeconds -ge $script:BroadcastInterval) {
            Send-UdpBroadcast -Port $Port
            $script:LastBroadcast = [datetime]::UtcNow
        }

        # --- UDP broadcast receive (non-blocking) ---
        if ($udpListener) {
            try {
                $remoteEP = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
                if ($udpListener.Available -gt 0) {
                    $udpBytes = $udpListener.Receive([ref]$remoteEP)
                    $udpJson = [System.Text.Encoding]::UTF8.GetString($udpBytes)
                    Process-UdpBroadcast -Json $udpJson -SenderIp $remoteEP.Address.ToString()
                }
            } catch {}
        }

        # --- Prune stale devices (>30s no heartbeat) ---
        $now = [datetime]::UtcNow
        $stale = @()
        foreach ($dk in $script:Devices.Keys) {
            if (($now - $script:Devices[$dk].lastSeen).TotalSeconds -gt 30) { $stale += $dk }
        }
        foreach ($dk in $stale) { $script:Devices.Remove($dk) }

        # --- Prune stale clients (>60s no heartbeat) ---
        $staleC = @()
        foreach ($ck in $script:Clients.Keys) {
            if (($now - $script:Clients[$ck].lastSeen).TotalSeconds -gt 60) { $staleC += $ck }
        }
        foreach ($ck in $staleC) { $script:Clients.Remove($ck) }

        # --- HTTP request (non-blocking check) ---
        $waited = $ar.AsyncWaitHandle.WaitOne(100)  # 100ms to allow UDP polling
        if (-not $waited) { continue }
        $ctx = $listener.EndGetContext($ar)
        # Start NEXT async request immediately (one at a time, no leak)
        $ar = $listener.BeginGetContext($null, $null)
        $req = $ctx.Request
        $resp = $ctx.Response

        try {
            $rawUrl = $req.Url.AbsolutePath
            $query = $req.QueryString
            $method = $req.HttpMethod

            # Token check
            if ($Token -and $query['t'] -ne $Token) {
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] 403 $method $rawUrl (no token)" -ForegroundColor DarkYellow
                Send-Error $resp 403 "需要访问令牌"
                continue
            }

            # URL-decode path
            $relPath = [System.Uri]::UnescapeDataString($rawUrl)
            # Normalize: strip leading slash
            $relPath = $relPath.TrimStart('/')
            $absPath = Join-Path $SharePath $relPath
            # Canonicalize and prevent path traversal
            $fullAbs = [System.IO.Path]::GetFullPath($absPath).TrimEnd('\')
            $fullShare = $SharePath.TrimEnd('\')
            if (-not $fullAbs.StartsWith($fullShare, [System.StringComparison]::OrdinalIgnoreCase)) {
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] 403 $method $rawUrl (path traversal blocked)" -ForegroundColor Red
                Send-Error $resp 403 "禁止访问共享目录之外"
                continue
            }

            $ts = Get-Date -Format 'HH:mm:ss'

            # API: server info
            if ($rawUrl -eq '/api/info' -and $method -eq 'GET') {
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] API /api/info" -ForegroundColor DarkGray
                $info = @{
                    sharePath = $SharePath
                    port = $Port
                    readOnly = [bool]$ReadOnly
                    tokenRequired = ($Token.Length -gt 0)
                    lanUrl = if ($ips.Count -gt 0) { "http://$($ips[0]):$Port/" } else { "http://127.0.0.1:$Port/" }
                    localUrl = "http://127.0.0.1:$Port/"
                    ips = $ips
                    deviceId = $script:DeviceId
                    deviceName = $script:DeviceName
                }
                Send-Json $resp $info
                continue
            }

            # API: directory listing
            if ($rawUrl -eq '/api/list' -and $method -eq 'GET') {
                $queryPath = $query['path']
                if (-not $queryPath) { $queryPath = '' }
                $queryPath = [System.Uri]::UnescapeDataString($queryPath)
                $queryPath = $queryPath.TrimStart('/')
                $listAbs = Join-Path $SharePath $queryPath
                $listFull = [System.IO.Path]::GetFullPath($listAbs).TrimEnd('\')
                if (-not $listFull.StartsWith($fullShare, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $ts = Get-Date -Format 'HH:mm:ss'
                    Write-Host "  [$ts] 403 API /api/list (path traversal)" -ForegroundColor Red
                    Send-Json $resp @{error='禁止访问共享目录之外'} 403
                    continue
                }
                if (-not (Test-Path -LiteralPath $listFull -PathType Container)) {
                    Send-Json $resp @{error='目录不存在'} 404
                    continue
                }
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] API /api/list $queryPath" -ForegroundColor DarkGray
                $fileList = Build-FileListJson $listFull
                Send-Json $resp $fileList
                continue
            }

            # API: client register (POST — browser clients register themselves)
            if ($rawUrl -eq '/api/client/register' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $body = $bodyStr | ConvertFrom-Json
                    if ($body.clientId -and $body.clientName) {
                        $clientIp = $req.RemoteEndPoint.Address.ToString()
                        # Replace localhost with server LAN IP so other devices can reach this client
                        if ($clientIp -eq '127.0.0.1' -or $clientIp -eq '::1' -or $clientIp -eq '::ffff:127.0.0.1') {
                            $clientIp = $primaryIp
                        }
                        $script:Clients[$body.clientId] = @{
                            id       = $body.clientId
                            name     = $body.clientName
                            ip       = $clientIp
                            lastSeen = [datetime]::UtcNow
                        }
                        $ts2 = Get-Date -Format 'HH:mm:ss'
                        Write-Host "  [$ts2] Client registered: $($body.clientName) ($($body.clientId)) from $clientIp" -ForegroundColor Cyan
                        Send-Json $resp @{ok=$true}
                    } else {
                        Send-Json $resp @{error='clientId and clientName required'} 400
                    }
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }

            # API: device list (servers + clients)
            if ($rawUrl -eq '/api/devices' -and $method -eq 'GET') {
                $devArr = @()
                foreach ($dk in $script:Devices.Keys) {
                    $dev = $script:Devices[$dk]
                    $devArr += @{
                        id       = $dev.id
                        name     = $dev.name
                        ip       = $dev.ip
                        port     = $dev.port
                        lastSeen = $dev.lastSeen.ToString('o')
                        type     = 'server'
                    }
                }
                # Deduplicate clients by IP: keep only the most recently seen per IP
                $clientByIp = @{}
                foreach ($ck in $script:Clients.Keys) {
                    $cli = $script:Clients[$ck]
                    $cip = $cli.ip
                    if (-not $clientByIp.ContainsKey($cip) -or $cli.lastSeen -gt $clientByIp[$cip].lastSeen) {
                        $clientByIp[$cip] = $cli
                    }
                }
                foreach ($cli in $clientByIp.Values) {
                    $devArr += @{
                        id       = $cli.id
                        name     = $cli.name
                        ip       = $cli.ip
                        port     = 0
                        lastSeen = $cli.lastSeen.ToString('o')
                        type     = 'client'
                    }
                }
                Send-Json $resp $devArr
                continue
            }

            # API: change device name
            if ($rawUrl -eq '/api/device/name' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $body = $bodyStr | ConvertFrom-Json
                    if ($body.name) {
                        $script:DeviceName = $body.name
                        Write-Host "  [$ts] Device name changed to: $($body.name)" -ForegroundColor Cyan
                        Send-Json $resp @{ok=$true; name=$script:DeviceName}
                    } else {
                        Send-Json $resp @{error='name is required'} 400
                    }
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }

            # API: chat messages (GET = poll, POST = send)
            if ($rawUrl -eq '/api/chat/messages' -and $method -eq 'GET') {
                $after = $query['after']
                $msgs = @()
                foreach ($m in $script:Messages) {
                    if (-not $after -or $m.ts -gt $after) { $msgs += $m }
                }
                Send-Json $resp $msgs
                continue
            }
            if ($rawUrl -eq '/api/chat/send' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $msg = $bodyStr | ConvertFrom-Json
                    $chatMsg = @{
                        id       = [guid]::NewGuid().ToString('N').Substring(0, 8)
                        from     = $msg.from
                        fromName = $msg.fromName
                        to       = $msg.to
                        text     = $msg.text
                        ts       = [datetime]::UtcNow.ToString('o')
                        type     = 'text'
                    }
                    [void]$script:Messages.Add($chatMsg)
                    # Forward to target device if it's not local
                    if ($msg.targetIp -and $msg.targetPort) {
                        try {
                            $fwdBytes = [System.Text.Encoding]::UTF8.GetBytes(($chatMsg | ConvertTo-Json -Compress))
                            $fwdReq = [System.Net.WebRequest]::Create("http://$($msg.targetIp):$($msg.targetPort)/api/chat/deliver")
                            $fwdReq.Method = 'POST'
                            $fwdReq.ContentType = 'application/json'
                            $fwdReq.ContentLength = $fwdBytes.Length
                            $fwdReq.Timeout = 5000
                            $fwdStream = $fwdReq.GetRequestStream()
                            $fwdStream.Write($fwdBytes, 0, $fwdBytes.Length)
                            $fwdStream.Close()
                            $fwdReq.GetResponse().Close()
                        } catch { Write-Host "  [$ts] Forward failed: $_" -ForegroundColor DarkYellow }
                    }
                    Send-Json $resp @{ok=$true; id=$chatMsg.id}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }
            # Chat deliver endpoint (receive forwarded messages)
            if ($rawUrl -eq '/api/chat/deliver' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $chatMsg = $bodyStr | ConvertFrom-Json
                    $msgHash = @{
                        id       = $chatMsg.id
                        from     = $chatMsg.from
                        fromName = $chatMsg.fromName
                        to       = $chatMsg.to
                        text     = $chatMsg.text
                        ts       = $chatMsg.ts
                        type     = $chatMsg.type
                    }
                    if ($chatMsg.fileSize) { $msgHash.fileSize = $chatMsg.fileSize }
                    [void]$script:Messages.Add($msgHash)
                    Write-Host "  [$ts] CHAT from $($chatMsg.fromName): $($chatMsg.text)" -ForegroundColor Magenta
                    Send-Json $resp @{ok=$true}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }
            # Chat file receive endpoint
            if ($rawUrl -eq '/api/chat/file' -and $method -eq 'POST') {
                $ct = $req.ContentType
                if ($ct -notmatch 'boundary=(.+)$') {
                    Send-Json $resp @{error='Invalid Content-Type'} 400
                    continue
                }
                $boundary = $matches[1].Trim('"')
                $files = Parse-Multipart $req.InputStream $boundary
                $chatDir = Join-Path $SharePath '.lan-share-chat'
                if (-not (Test-Path $chatDir)) { New-Item -ItemType Directory -Path $chatDir -Force | Out-Null }
                $fromId = $req.Headers['X-Device-Id']
                $fromName = $req.Headers['X-Device-Name']
                $targetId = $req.Headers['X-Target-Id']
                $targetIp = $req.Headers['X-Target-Ip']
                $targetPort = $req.Headers['X-Target-Port']
                $targetTo = if ($targetId) { $targetId } else { $script:DeviceId }
                foreach ($f in $files) {
                    $safeName = [System.IO.Path]::GetFileName($f.Name)
                    $dest = Join-Path $chatDir $safeName
                    [System.IO.File]::WriteAllBytes($dest, $f.Bytes)
                    $chatMsg = @{
                        id       = [guid]::NewGuid().ToString('N').Substring(0, 8)
                        from     = $fromId
                        fromName = $fromName
                        to       = $targetTo
                        text     = $safeName
                        ts       = [datetime]::UtcNow.ToString('o')
                        type     = 'file'
                        fileSize = $f.Bytes.Length
                    }
                    [void]$script:Messages.Add($chatMsg)
                    Write-Host "  [$ts] CHAT FILE from $($chatMsg.fromName): $safeName" -ForegroundColor Magenta
                    # Forward file message to target server if remote
                    if ($targetIp -and $targetPort) {
                        try {
                            $fwdMsg = @{
                                id       = $chatMsg.id
                                from     = $fromId
                                fromName = $fromName
                                to       = $targetTo
                                text     = $safeName
                                ts       = $chatMsg.ts
                                type     = 'file'
                                fileSize = $f.Bytes.Length
                            }
                            $fwdBytes = [System.Text.Encoding]::UTF8.GetBytes(($fwdMsg | ConvertTo-Json -Compress))
                            $fwdReq = [System.Net.WebRequest]::Create("http://$targetIp`:$targetPort/api/chat/deliver")
                            $fwdReq.Method = 'POST'
                            $fwdReq.ContentType = 'application/json'
                            $fwdReq.ContentLength64 = $fwdBytes.Length
                            $fwdReq.Timeout = 5000
                            $fwdStream = $fwdReq.GetRequestStream()
                            $fwdStream.Write($fwdBytes, 0, $fwdBytes.Length)
                            $fwdStream.Close()
                            $fwdReq.GetResponse().Close()
                        } catch { Write-Host "  [$ts] Forward file failed: $_" -ForegroundColor DarkYellow }
                    }
                }
                Send-Json $resp @{ok=$true}
                continue
            }
            # Transfer list endpoint
            if ($rawUrl -eq '/api/transfers' -and $method -eq 'GET') {
                Send-Json $resp @($script:Transfers)
                continue
            }

            # Serve qrcode.min.js
            if ($rawUrl -eq '/qrcode.min.js' -and $method -eq 'GET' -and $qrJsBytes) {
                $resp.StatusCode = 200
                $resp.ContentType = 'application/javascript; charset=utf-8'
                $resp.ContentLength64 = $qrJsBytes.Length
                $resp.OutputStream.Write($qrJsBytes, 0, $qrJsBytes.Length)
                $resp.OutputStream.Close()
                continue
            }

            # Serve index.html at root (SPA)
            if (($rawUrl -eq '/' -or $rawUrl -eq '/index.html') -and $method -eq 'GET' -and $indexHtmlBytes) {
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] SPA index.html" -ForegroundColor DarkGray
                $resp.StatusCode = 200
                $resp.ContentType = 'text/html; charset=utf-8'
                $resp.ContentLength64 = $indexHtmlBytes.Length
                $resp.OutputStream.Write($indexHtmlBytes, 0, $indexHtmlBytes.Length)
                $resp.OutputStream.Close()
                continue
            }

            # File download via /files/... path
            if ($rawUrl -match '^/files/(.+)$' -and $method -eq 'GET') {
                $fileRel = [System.Uri]::UnescapeDataString($matches[1])
                $fileAbs = Join-Path $SharePath $fileRel
                $fileFull = [System.IO.Path]::GetFullPath($fileAbs).TrimEnd('\')
                if (-not $fileFull.StartsWith($fullShare, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $ts = Get-Date -Format 'HH:mm:ss'
                    Write-Host "  [$ts] 403 GET $rawUrl (path traversal)" -ForegroundColor Red
                    Send-Error $resp 403 "禁止访问共享目录之外"
                    continue
                }
                if (-not (Test-Path -LiteralPath $fileFull -PathType Leaf)) {
                    Write-Host "  [$ts] 404 GET $rawUrl" -ForegroundColor DarkYellow
                    Send-Error $resp 404 "文件不存在"
                    continue
                }
                $fi = Get-Item -LiteralPath $fileFull
                $ext = [System.IO.Path]::GetExtension($fileFull)
                $mimeType = Get-MimeType $ext
                $isInline = $mimeType -ne 'application/octet-stream'
                $resp.StatusCode = 200
                $resp.ContentType = $mimeType
                $dispName = [System.IO.Path]::GetFileName($fileFull)
                if ($isInline) {
                    $resp.Headers['Content-Disposition'] = "inline; filename*=UTF-8''" + (Url-Encode $dispName)
                } else {
                    $resp.Headers['Content-Disposition'] = "attachment; filename*=UTF-8''" + (Url-Encode $dispName)
                }
                $resp.ContentLength64 = $fi.Length
                $resp.SendChunked = $false
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] SEND $dispName ($(Format-Size $fi.Length)) $mimeType" -ForegroundColor Green
                $fs = [System.IO.File]::OpenRead($fileFull)
                try { $fs.CopyTo($resp.OutputStream) } finally { $fs.Dispose() }
                $resp.OutputStream.Close()
                continue
            }

            # Force-download endpoint (Content-Disposition: attachment)
            if ($rawUrl -match '^/api/download\??(.*)' -and $method -eq 'GET') {
                $dlPath = $query['path']
                if (-not $dlPath) { $dlPath = '' }
                $dlPath = [System.Uri]::UnescapeDataString($dlPath).TrimStart('/')
                $dlFull = Join-Path $SharePath $dlPath
                $dlFull = [System.IO.Path]::GetFullPath($dlFull).TrimEnd('\')
                if (-not $dlFull.StartsWith($fullShare, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Send-Error $resp 403 "禁止访问共享目录之外"
                    continue
                }
                if (-not (Test-Path -LiteralPath $dlFull -PathType Leaf)) {
                    Send-Error $resp 404 "文件不存在"
                    continue
                }
                $fi = Get-Item -LiteralPath $dlFull
                $resp.StatusCode = 200
                $resp.ContentType = 'application/octet-stream'
                $dispName = [System.IO.Path]::GetFileName($dlFull)
                $resp.Headers['Content-Disposition'] = "attachment; filename*=UTF-8''" + (Url-Encode $dispName)
                $resp.ContentLength64 = $fi.Length
                $resp.SendChunked = $false
                $ts = Get-Date -Format 'HH:mm:ss'
                Write-Host "  [$ts] DOWNLOAD $dispName ($(Format-Size $fi.Length))" -ForegroundColor Cyan
                $fs = [System.IO.File]::OpenRead($dlFull)
                try { $fs.CopyTo($resp.OutputStream) } finally { $fs.Dispose() }
                $resp.OutputStream.Close()
                continue
            }

            # Upload endpoint (matches both "/api/upload" and "__upload" at root)
            if (($rawUrl -eq '/api/upload' -or $relPath -match '(^|/)__upload$') -and $method -eq 'POST') {
                if ($ReadOnly) {
                    Write-Host "  [$ts] 403 POST $rawUrl (read-only)" -ForegroundColor DarkYellow
                    Send-Text $resp "只读模式,禁止上传" 403
                    continue
                }
                # Resolve target dir: new API uses ?path= query param, old uses URL path
                if ($rawUrl -eq '/api/upload') {
                    $uploadRel = $query['path']
                    if (-not $uploadRel) { $uploadRel = '' }
                    $uploadRel = [System.Uri]::UnescapeDataString($uploadRel).TrimStart('/')
                    $targetDir = Join-Path $SharePath $uploadRel
                    $targetDir = [System.IO.Path]::GetFullPath($targetDir).TrimEnd('\')
                } else {
                    $targetDir = $fullAbs -replace '\\__upload$', ''
                }
                if (-not $targetDir.StartsWith($fullShare, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Send-Text $resp "禁止访问共享目录之外" 403
                    continue
                }
                if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
                    Send-Text $resp "目标目录不存在" 404
                    continue
                }
                $ct = $req.ContentType
                if ($ct -notmatch 'boundary=(.+)$') {
                    Send-Text $resp "无效的 Content-Type" 400
                    continue
                }
                $boundary = $matches[1].Trim('"')
                $files = Parse-Multipart $req.InputStream $boundary
                $saved = @()
                foreach ($f in $files) {
                    $safeName = [System.IO.Path]::GetFileName($f.Name)
                    $dest = Join-Path $targetDir $safeName
                    [System.IO.File]::WriteAllBytes($dest, $f.Bytes)
                    $saved += $safeName
                }
                $msg = ($saved -join ', ')
                Write-Host "  [$ts] UPLOAD $saved -> $targetDir" -ForegroundColor Cyan
                Send-Text $resp $msg 200
                continue
            }

            # Directory listing
            if (Test-Path -LiteralPath $fullAbs -PathType Container) {
                $relUrl = '/' + $relPath
                if ($relUrl -ne '/' -and $relUrl -notmatch '/$') {
                    # Redirect to add trailing slash so relative URLs work
                    $resp.Redirect($req.Url.AbsolutePath + '/')
                    $resp.Close()
                    continue
                }
                Write-Host "  [$ts] LIST $relUrl" -ForegroundColor DarkGray
                $html = Build-ListingHtml $fullAbs $relUrl
                Send-Html $resp $html
                continue
            }

            # File download (fallback route for legacy SPA mode)
            if (Test-Path -LiteralPath $fullAbs -PathType Leaf) {
                $fi = Get-Item -LiteralPath $fullAbs
                $ext = [System.IO.Path]::GetExtension($fullAbs)
                $mimeType = Get-MimeType $ext
                $isInline = $mimeType -ne 'application/octet-stream'
                $resp.StatusCode = 200
                $resp.ContentType = $mimeType
                $dispName = [System.IO.Path]::GetFileName($fullAbs)
                if ($isInline) {
                    $resp.Headers['Content-Disposition'] = "inline; filename*=UTF-8''" + (Url-Encode $dispName)
                } else {
                    $resp.Headers['Content-Disposition'] = "attachment; filename*=UTF-8''" + (Url-Encode $dispName)
                }
                $resp.ContentLength64 = $fi.Length
                $resp.SendChunked = $false
                Write-Host "  [$ts] SEND $dispName ($(Format-Size $fi.Length)) $mimeType" -ForegroundColor Green
                $fs = [System.IO.File]::OpenRead($fullAbs)
                try {
                    $fs.CopyTo($resp.OutputStream)
                } finally {
                    $fs.Dispose()
                }
                $resp.OutputStream.Close()
                continue
            }

            Write-Host "  [$ts] 404 $method $rawUrl" -ForegroundColor DarkYellow
            Send-Error $resp 404 "文件或目录不存在"
        } catch {
            try {
                Send-Error $resp 500 $_.Exception.Message
            } catch {}
        } finally {
            try { $resp.Close() } catch {}
        }
    }
} finally {
    if ($listener) { try { $listener.Stop() } catch {} }
    if ($udpListener) { try { $udpListener.Close() } catch {} }
    Write-Host "  Service stopped." -ForegroundColor Yellow
}
