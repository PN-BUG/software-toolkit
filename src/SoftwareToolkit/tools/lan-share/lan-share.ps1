# ============================================
#  lan-share.ps1
#  LAN file sharing server (pure PowerShell + .NET HttpListener)
#
#  Usage:
#    .\lan-share.ps1                              (share package root, port 8088)
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
    [string]$Token = "",
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

# ---------- Validate port ----------
if ($Port -lt 1 -or $Port -gt 65535) {
    Write-Host "  WARNING: Invalid port $Port, using default 8088" -ForegroundColor Yellow
    $Port = 8088
}

# ---------- Resolve share path ----------
if ([string]::IsNullOrWhiteSpace($SharePath)) {
    $scriptLocation = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
    $toolsDirectory = [System.IO.Path]::GetDirectoryName($scriptLocation)
    if ([System.IO.Path]::GetFileName($scriptLocation) -eq 'lan-share' -and
        [System.IO.Path]::GetFileName($toolsDirectory) -eq 'tools') {
        $SharePath = [System.IO.Path]::GetDirectoryName($toolsDirectory)
    } else {
        $SharePath = $scriptLocation
    }
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

function Get-QueryValueUtf8([string]$rawQuery, [string]$name) {
    if ([string]::IsNullOrEmpty($rawQuery)) { return $null }
    foreach ($pair in $rawQuery.TrimStart('?').Split('&')) {
        $parts = $pair -split '=', 2
        $rawKey = $parts[0].Replace('+', ' ')
        $key = [System.Uri]::UnescapeDataString($rawKey)
        if (-not [string]::Equals($key, $name, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($parts.Length -lt 2) { return '' }
        return [System.Uri]::UnescapeDataString($parts[1].Replace('+', ' '))
    }
    return $null
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

        # Parse Content-Disposition. Mobile browsers may send either the classic
        # filename="..." form or RFC 5987's filename*=UTF-8''... form.
        $filename = $null
        if ($headerStr -match '(?i)filename\*\s*=\s*"?UTF-8''''([^;"\r\n]+)') {
            $filename = [System.Uri]::UnescapeDataString($matches[1])
        } elseif ($headerStr -match '(?i)filename\s*=\s*"([^"]*)"') {
            $filename = $matches[1]
        } elseif ($headerStr -match '(?i)filename\s*=\s*([^;\r\n]+)') {
            $filename = $matches[1].Trim().Trim('"')
        }
        if (-not [string]::IsNullOrWhiteSpace($filename)) {
            # Trim trailing CRLF if any
            $len = $bodyEnd - $bodyStart
            $fileBytes = New-Object byte[] $len
            [Array]::Copy($data, $bodyStart, $fileBytes, 0, $len)
            $files += [PSCustomObject]@{ Name = $filename; Bytes = $fileBytes }
        }
    }
    return $files
}

function Get-MultipartBoundary([string]$contentType) {
    if ([string]::IsNullOrWhiteSpace($contentType)) { return $null }
    $match = [regex]::Match(
        $contentType,
        'boundary\s*=\s*(?:"([^"]+)"|([^;]+))',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return $null }
    $value = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
    return $value.Trim()
}

function Save-RequestStream(
    [System.Net.HttpListenerRequest]$request,
    [string]$destination,
    [System.Collections.IDictionary]$transfer = $null) {
    $tempName = '.lan-share-' + [guid]::NewGuid().ToString('N') + '.uploading'
    $tempPath = Join-Path ([System.IO.Path]::GetDirectoryName($destination)) $tempName
    $output = $null
    try {
        $output = New-Object System.IO.FileStream(
            $tempPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            1048576,
            [System.IO.FileOptions]::SequentialScan)
        $buffer = New-Object byte[] 1048576
        [long]$written = 0
        while (($read = $request.InputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $read)
            $written += $read
            if ($transfer) { Update-TransferRecord $transfer $written 'sending' }
        }
        $output.Flush()
        $output.Dispose()
        $output = $null
        Move-Item -LiteralPath $tempPath -Destination $destination -Force
    } catch {
        if ($output) { try { $output.Dispose() } catch {} }
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Copy-StreamWithProgress(
    [System.IO.Stream]$inputStream,
    [System.IO.Stream]$outputStream,
    [System.Collections.IDictionary]$transfer = $null) {
    $buffer = New-Object byte[] 1048576
    [long]$written = 0
    while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $outputStream.Write($buffer, 0, $read)
        $written += $read
        if ($transfer) { Update-TransferRecord $transfer $written 'sending' }
    }
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
    $resp.Headers['Cache-Control'] = 'no-store'
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
$bridgePagePath = Join-Path $scriptDir 'bridge.html'
$bridgePageBytes = $null
if (Test-Path -LiteralPath $bridgePagePath -PathType Leaf) {
    $bridgePageBytes = [System.IO.File]::ReadAllBytes($bridgePagePath)
}

# Load qrcode.min.js for QR code generation
$qrJsPath = Join-Path $scriptDir 'qrcode.min.js'
$qrJsBytes = $null
if (Test-Path -LiteralPath $qrJsPath -PathType Leaf) {
    $qrJsBytes = [System.IO.File]::ReadAllBytes($qrJsPath)
    Write-Host "  [OK] qrcode.min.js loaded ($([math]::Round($qrJsBytes.Length/1KB,1)) KB)" -ForegroundColor DarkGray
}
$bridgeApkPath = Join-Path ([System.IO.Path]::GetDirectoryName($scriptDir)) 'lan-bridge-android\DandelionLanding.apk'
$bridgeApkAvailable = Test-Path -LiteralPath $bridgeApkPath -PathType Leaf

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
if (-not $NoBrowser) {
    try {
        $openUrl = "http://127.0.0.1:$Port/"
        Start-Process $openUrl
        Write-Host "  [OK] 已打开浏览器: $openUrl" -ForegroundColor DarkGray
    } catch {
        Write-Host "  [Info] 请手动打开浏览器访问 http://127.0.0.1:$Port/" -ForegroundColor DarkGray
    }
}
Write-Host ""

# ---------- Device discovery, connection requests & chat globals ----------
$deviceStateDir = Join-Path $env:LOCALAPPDATA 'SoftwareToolkit'
$deviceStatePath = Join-Path $deviceStateDir 'lan-share-device.json'
$script:DeviceId = $null
$script:DeviceName = $env:COMPUTERNAME
if ([string]::IsNullOrWhiteSpace($script:DeviceName)) {
    try { $script:DeviceName = [System.Net.Dns]::GetHostName() } catch { $script:DeviceName = 'Windows PC' }
}
try {
    if (Test-Path -LiteralPath $deviceStatePath -PathType Leaf) {
        $savedDevice = Get-Content -LiteralPath $deviceStatePath -Raw | ConvertFrom-Json
        if ($savedDevice.id -match '^[a-f0-9]{8,32}$') { $script:DeviceId = [string]$savedDevice.id }
        if (-not [string]::IsNullOrWhiteSpace([string]$savedDevice.name)) { $script:DeviceName = [string]$savedDevice.name }
    }
} catch {}
if (-not $script:DeviceId) {
    $script:DeviceId = [guid]::NewGuid().ToString('N').Substring(0, 12)
}
try {
    New-Item -ItemType Directory -Path $deviceStateDir -Force | Out-Null
    @{id=$script:DeviceId;name=$script:DeviceName} | ConvertTo-Json -Compress |
        Set-Content -LiteralPath $deviceStatePath -Encoding UTF8
} catch {}
$script:Devices = @{}   # key=deviceId, value=@{id,name,ip,port,lastSeen}
$script:Clients = @{}   # key=clientId, value=@{id,name,ip,lastSeen} — browser clients
$script:Messages = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$script:Transfers = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$script:Connections = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$script:UdpBroadcastPort = 8098
$script:LastBroadcast = [datetime]::MinValue

function New-TransferRecord(
    [string]$id,
    [string]$name,
    [long]$size,
    [string]$direction,
    [string]$kind,
    [string]$from,
    [string]$to) {
    if ([string]::IsNullOrWhiteSpace($id)) { $id = 'server-' + [guid]::NewGuid().ToString('N') }
    if ($size -lt 0) { $size = 0 }
    for ($i = $script:Transfers.Count - 1; $i -ge 0; $i--) {
        if ($script:Transfers[$i].id -eq $id) { $script:Transfers.RemoveAt($i) }
    }
    $now = [datetime]::UtcNow.ToString('o')
    $record = [ordered]@{
        id = $id; name = $name; size = $size; bytesDone = 0; percent = 0; speed = 0
        direction = $direction; kind = $kind; from = $from; to = $to
        status = 'sending'; ts = $now; updatedAt = $now; error = ''
    }
    $script:Transfers.Insert(0, $record)
    while ($script:Transfers.Count -gt 100) { $script:Transfers.RemoveAt($script:Transfers.Count - 1) }
    return $record
}

function Update-TransferRecord(
    [System.Collections.IDictionary]$record,
    [long]$bytesDone,
    [string]$status = 'sending',
    [string]$errorMessage = '') {
    if (-not $record) { return }
    $record['bytesDone'] = [Math]::Max([long]0, $bytesDone)
    $record['status'] = $status
    $record['error'] = $errorMessage
    $record['updatedAt'] = [datetime]::UtcNow.ToString('o')
    $size = [long]$record['size']
    if ($status -eq 'done' -and $size -le 0 -and $record['bytesDone'] -gt 0) {
        $record['size'] = [long]$record['bytesDone']
        $size = [long]$record['size']
    }
    if ($size -gt 0) {
        $record['percent'] = [Math]::Min(100, [Math]::Round(([double]$record['bytesDone'] / $size) * 100))
    }
    $started = [datetime]::Parse([string]$record['ts']).ToUniversalTime()
    $seconds = ([datetime]::UtcNow - $started).TotalSeconds
    if ($seconds -gt 0.2) { $record['speed'] = [Math]::Round([double]$record['bytesDone'] / $seconds) }
}
$script:BroadcastInterval = 5  # seconds

function Invoke-LanJsonPost([string]$url, $data) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($data | ConvertTo-Json -Depth 8 -Compress))
    $forward = [System.Net.WebRequest]::Create($url)
    $forward.Method = 'POST'
    $forward.ContentType = 'application/json; charset=utf-8'
    $forward.ContentLength = $bytes.Length
    $forward.Timeout = 5000
    $stream = $forward.GetRequestStream()
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    $response = $forward.GetResponse()
    try { return [int]$response.StatusCode } finally { $response.Dispose() }
}

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
            type     = if ($data.type) { [string]$data.type } else { 'server' }
            capability = [string]$data.capability
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

        # --- Prune old connection events (>24 hours) ---
        for ($ci = $script:Connections.Count - 1; $ci -ge 0; $ci--) {
            try {
                $updated = [datetime]::Parse([string]$script:Connections[$ci].updated).ToUniversalTime()
                if (($now - $updated).TotalHours -gt 24) { $script:Connections.RemoveAt($ci) }
            } catch { $script:Connections.RemoveAt($ci) }
        }

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
                    bridgeAvailable = [bool]$bridgeApkAvailable
                }
                Send-Json $resp $info
                continue
            }

            # Optional standalone Android bridge. LAN sharing remains fully usable without it.
            if ($rawUrl -eq '/api/bridge-apk' -and $method -eq 'GET') {
                if (-not $bridgeApkAvailable) {
                    Send-Error $resp 404 'Android bridge APK is not bundled'
                    continue
                }
                $apk = Get-Item -LiteralPath $bridgeApkPath
                $resp.StatusCode = 200
                $resp.ContentType = 'application/vnd.android.package-archive'
                $resp.Headers['Content-Disposition'] = "attachment; filename=DandelionLanding.apk"
                $resp.ContentLength64 = $apk.Length
                $resp.SendChunked = $false
                $apkStream = [System.IO.File]::OpenRead($bridgeApkPath)
                try { $apkStream.CopyTo($resp.OutputStream) } finally { $apkStream.Dispose() }
                $resp.OutputStream.Close()
                continue
            }

            # Download landing page: local download plus QR download for another device.
            if (($rawUrl -eq '/bridge' -or $rawUrl -eq '/bridge/') -and $method -eq 'GET') {
                if (-not $bridgePageBytes -or -not $bridgeApkAvailable) {
                    Send-Error $resp 404 'Dandelion Landing download page is unavailable'
                    continue
                }
                $resp.StatusCode = 200
                $resp.ContentType = 'text/html; charset=utf-8'
                $resp.Headers['Cache-Control'] = 'no-store, no-cache, must-revalidate'
                $resp.ContentLength64 = $bridgePageBytes.Length
                $resp.OutputStream.Write($bridgePageBytes, 0, $bridgePageBytes.Length)
                $resp.OutputStream.Close()
                continue
            }

            # API: directory listing
            if ($rawUrl -eq '/api/list' -and $method -eq 'GET') {
                $queryPath = Get-QueryValueUtf8 $req.Url.Query 'path'
                if (-not $queryPath) { $queryPath = '' }
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
                # Always expose this host as a chat target. Without this entry a
                # phone is left with no selectable peer unless a second browser
                # happens to be open on the PC.
                $devArr = @(@{
                    id       = $script:DeviceId
                    name     = $script:DeviceName
                    ip       = $primaryIp
                    port     = $Port
                    lastSeen = [datetime]::UtcNow.ToString('o')
                    type     = 'server'
                    isLocal  = $true
                })
                foreach ($dk in $script:Devices.Keys) {
                    $dev = $script:Devices[$dk]
                    if ($dev.id -eq $script:DeviceId) { continue }
                    $devArr += @{
                        id       = $dev.id
                        name     = $dev.name
                        ip       = $dev.ip
                        port     = $dev.port
                        lastSeen = $dev.lastSeen.ToString('o')
                        type     = if ($dev.type) { $dev.type } else { 'server' }
                        capability = $dev.capability
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

            # API: initiate a connection request. Requests to clients on this
            # server stay local; requests to remembered/discovered servers are
            # forwarded to their lan-share endpoint.
            if ($rawUrl -eq '/api/connect/request' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $body = $bodyStr | ConvertFrom-Json
                    if ([string]::IsNullOrWhiteSpace([string]$body.from) -or
                        [string]::IsNullOrWhiteSpace([string]$body.to)) {
                        throw 'from and to are required'
                    }
                    $isLocalTarget = $body.to -eq $script:DeviceId -or $script:Clients.ContainsKey([string]$body.to)
                    if (-not $isLocalTarget -and (-not $body.targetIp -or -not $body.targetPort)) {
                        Send-Json $resp @{error='设备当前离线，且没有可用的历史服务地址'} 409
                        continue
                    }
                    # Replace an older pending request for the same pair.
                    for ($ci = $script:Connections.Count - 1; $ci -ge 0; $ci--) {
                        $old = $script:Connections[$ci]
                        if ($old.from -eq $body.from -and $old.to -eq $body.to -and $old.status -eq 'pending') {
                            $script:Connections.RemoveAt($ci)
                        }
                    }
                    $nowIso = [datetime]::UtcNow.ToString('o')
                    $connection = [ordered]@{
                        id             = [guid]::NewGuid().ToString('N').Substring(0, 12)
                        from           = [string]$body.from
                        fromName       = [string]$body.fromName
                        to             = [string]$body.to
                        targetName     = [string]$body.targetName
                        targetIp       = [string]$body.targetIp
                        targetPort     = [int]$body.targetPort
                        targetType     = [string]$body.targetType
                        originIp       = $primaryIp
                        originPort     = $Port
                        originUrl      = if ($Token) { "http://${primaryIp}:$Port/?t=$(Url-Encode $Token)" } else { "http://${primaryIp}:$Port/" }
                        originServerId = $script:DeviceId
                        status         = 'pending'
                        ts             = $nowIso
                        updated        = $nowIso
                    }
                    [void]$script:Connections.Add($connection)
                    if (-not $isLocalTarget) {
                        try {
                            [void](Invoke-LanJsonPost "http://$($connection.targetIp):$($connection.targetPort)/api/connect/deliver" $connection)
                        } catch {
                            $connection.status = 'failed'
                            $connection.updated = [datetime]::UtcNow.ToString('o')
                            Send-Json $resp @{error=('无法联系设备: ' + $_.Exception.Message)} 502
                            continue
                        }
                    }
                    Write-Host "  [$ts] CONNECT request $($connection.fromName) -> $($connection.targetName)" -ForegroundColor Cyan
                    Send-Json $resp @{ok=$true; id=$connection.id; status=$connection.status}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }

            # API: receive a connection request forwarded by another server.
            if ($rawUrl -eq '/api/connect/deliver' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $incoming = $bodyStr | ConvertFrom-Json
                    if (-not $incoming.id -or -not $incoming.from -or -not $incoming.to) { throw 'Invalid connection request' }
                    $exists = $false
                    foreach ($item in $script:Connections) { if ($item.id -eq $incoming.id) { $exists = $true; break } }
                    if (-not $exists) {
                        $copy = [ordered]@{}
                        foreach ($property in $incoming.PSObject.Properties) { $copy[$property.Name] = $property.Value }
                        $copy.status = 'pending'
                        $copy.updated = [datetime]::UtcNow.ToString('o')
                        [void]$script:Connections.Add($copy)
                    }
                    Send-Json $resp @{ok=$true}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }

            # API: browsers poll incoming requests and outgoing responses.
            if ($rawUrl -eq '/api/connect/events' -and $method -eq 'GET') {
                $clientId = Get-QueryValueUtf8 $req.Url.Query 'clientId'
                $events = @()
                if ($clientId) {
                    foreach ($item in $script:Connections) {
                        $isIncoming = $item.status -eq 'pending' -and $item.from -ne $clientId -and
                            ($item.to -eq $clientId -or $item.to -eq $script:DeviceId)
                        $isResponse = $item.from -eq $clientId -and $item.status -ne 'pending'
                        if ($isIncoming -or $isResponse) { $events += $item }
                    }
                }
                Send-Json $resp $events
                continue
            }

            # API: accept or reject a connection request.
            if ($rawUrl -eq '/api/connect/respond' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $body = $bodyStr | ConvertFrom-Json
                    $connection = $null
                    foreach ($item in $script:Connections) { if ($item.id -eq $body.id) { $connection = $item; break } }
                    if (-not $connection) {
                        Send-Json $resp @{error='连接请求不存在或已过期'} 404
                        continue
                    }
                    $newStatus = if ([bool]$body.accepted) { 'accepted' } else { 'rejected' }
                    $responseEvent = [ordered]@{}
                    foreach ($key in $connection.Keys) { $responseEvent[$key] = $connection[$key] }
                    $responseEvent.status = $newStatus
                    $responseEvent.responderId = [string]$body.responderId
                    $responseEvent.responderName = [string]$body.responderName
                    $responseEvent.responderIp = $primaryIp
                    $responseEvent.responderPort = $Port
                    $responseEvent.updated = [datetime]::UtcNow.ToString('o')
                    if ($connection.originServerId -ne $script:DeviceId) {
                        try {
                            [void](Invoke-LanJsonPost "http://$($connection.originIp):$($connection.originPort)/api/connect/response/deliver" $responseEvent)
                        } catch {
                            Send-Json $resp @{error=('无法回复发起设备: ' + $_.Exception.Message)} 502
                            continue
                        }
                    }
                    foreach ($key in $responseEvent.Keys) { $connection[$key] = $responseEvent[$key] }
                    Send-Json $resp @{ok=$true; status=$newStatus}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 400
                }
                continue
            }

            # API: receive the accept/reject result on the originating server.
            if ($rawUrl -eq '/api/connect/response/deliver' -and $method -eq 'POST') {
                $bodyMs = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($bodyMs)
                $bodyStr = [System.Text.Encoding]::UTF8.GetString($bodyMs.ToArray())
                $bodyMs.Dispose()
                try {
                    $incoming = $bodyStr | ConvertFrom-Json
                    $connection = $null
                    foreach ($item in $script:Connections) { if ($item.id -eq $incoming.id) { $connection = $item; break } }
                    if (-not $connection) { throw 'Connection request not found' }
                    foreach ($property in $incoming.PSObject.Properties) { $connection[$property.Name] = $property.Value }
                    $connection.updated = [datetime]::UtcNow.ToString('o')
                    Send-Json $resp @{ok=$true}
                } catch {
                    Send-Json $resp @{error=$_.Exception.Message} 404
                }
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
                        try {
                            @{id=$script:DeviceId;name=$script:DeviceName} | ConvertTo-Json -Compress |
                                Set-Content -LiteralPath $deviceStatePath -Encoding UTF8
                        } catch {}
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
                    if ([string]::IsNullOrWhiteSpace([string]$msg.from) -or
                        [string]::IsNullOrWhiteSpace([string]$msg.to) -or
                        [string]::IsNullOrWhiteSpace([string]$msg.text)) {
                        throw 'from, to and text are required'
                    }
                    $messageId = [string]$msg.id
                    if ($messageId -notmatch '^[a-zA-Z0-9_-]{8,80}$') {
                        $messageId = [guid]::NewGuid().ToString('N').Substring(0, 12)
                    }
                    $existingMessage = $null
                    foreach ($existing in $script:Messages) {
                        if ([string]$existing.id -eq $messageId) { $existingMessage = $existing; break }
                    }
                    if ($existingMessage) {
                        Send-Json $resp @{ok=$true; id=$existingMessage.id; ts=$existingMessage.ts; duplicate=$true}
                        continue
                    }
                    $chatMsg = @{
                        id       = $messageId
                        from     = $msg.from
                        fromName = $msg.fromName
                        to       = $msg.to
                        text     = $msg.text
                        ts       = [datetime]::UtcNow.ToString('o')
                        type     = 'text'
                    }
                    [void]$script:Messages.Add($chatMsg)
                    # Forward to target device if it's not local
                    if ($msg.to -ne $script:DeviceId -and $msg.targetIp -and $msg.targetPort) {
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
                    Send-Json $resp @{ok=$true; id=$chatMsg.id; ts=$chatMsg.ts}
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
                    $alreadyDelivered = $false
                    foreach ($existing in $script:Messages) {
                        if ([string]$existing.id -eq [string]$chatMsg.id) { $alreadyDelivered = $true; break }
                    }
                    if ($alreadyDelivered) {
                        Send-Json $resp @{ok=$true; duplicate=$true}
                        continue
                    }
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
                $chatDir = Join-Path $SharePath '.lan-share-chat'
                if (-not (Test-Path $chatDir)) { New-Item -ItemType Directory -Path $chatDir -Force | Out-Null }
                $fromId = $req.Headers['X-Device-Id']
                $fromName = $req.Headers['X-Device-Name']
                if ($fromName) { $fromName = [System.Uri]::UnescapeDataString($fromName) }
                $targetId = $req.Headers['X-Target-Id']
                $targetIp = $req.Headers['X-Target-Ip']
                $targetPort = $req.Headers['X-Target-Port']
                $messageId = [string]$req.Headers['X-Message-Id']
                if ($messageId -match '^[a-zA-Z0-9_-]{8,80}$') {
                    $duplicateFileMessage = $false
                    foreach ($existing in $script:Messages) {
                        if ([string]$existing.id -eq $messageId) { $duplicateFileMessage = $true; break }
                    }
                    if ($duplicateFileMessage) {
                        Send-Json $resp @{ok=$true; id=$messageId; duplicate=$true}
                        continue
                    }
                } else {
                    $messageId = $null
                }
                $targetTo = if ($targetId) { $targetId } else { $script:DeviceId }
                $receivedFiles = @()
                $rawFileName = Get-QueryValueUtf8 $req.Url.Query 'name'
                if ($rawFileName) {
                    $safeName = [System.IO.Path]::GetFileName($rawFileName)
                    if ([string]::IsNullOrWhiteSpace($safeName)) {
                        Send-Json $resp @{error='Invalid file name'} 400
                        continue
                    }
                    $dest = Join-Path $chatDir $safeName
                    $transferId = [string]$req.Headers['X-Transfer-Id']
                    $transfer = New-TransferRecord $transferId $safeName $req.ContentLength64 'upload' 'chat' $fromName $script:DeviceName
                    try {
                        Save-RequestStream $req $dest $transfer
                        Update-TransferRecord $transfer (Get-Item -LiteralPath $dest).Length 'done'
                    } catch {
                        Update-TransferRecord $transfer ([long]$transfer['bytesDone']) 'failed' $_.Exception.Message
                        throw
                    }
                    $receivedFiles += [PSCustomObject]@{ Name = $safeName; Length = (Get-Item -LiteralPath $dest).Length }
                } else {
                    # Backward-compatible multipart path for older pages.
                    $boundary = Get-MultipartBoundary $req.ContentType
                    if (-not $boundary) {
                        Send-Json $resp @{error='Invalid Content-Type'} 400
                        continue
                    }
                    $files = Parse-Multipart $req.InputStream $boundary
                    foreach ($f in $files) {
                        $safeName = [System.IO.Path]::GetFileName($f.Name)
                        $dest = Join-Path $chatDir $safeName
                        [System.IO.File]::WriteAllBytes($dest, $f.Bytes)
                        $receivedFiles += [PSCustomObject]@{ Name = $safeName; Length = $f.Bytes.Length }
                    }
                }
                if ($receivedFiles.Count -eq 0) {
                    Send-Json $resp @{error='No valid file was received'} 400
                    continue
                }
                $fileMessageIndex = 0
                $sentMessageIds = @()
                foreach ($f in $receivedFiles) {
                    $safeName = $f.Name
                    $fileMessageId = if ($messageId -and $fileMessageIndex -eq 0) { $messageId } else { [guid]::NewGuid().ToString('N').Substring(0, 12) }
                    $chatMsg = @{
                        id       = $fileMessageId
                        from     = $fromId
                        fromName = $fromName
                        to       = $targetTo
                        text     = $safeName
                        ts       = [datetime]::UtcNow.ToString('o')
                        type     = 'file'
                        fileSize = $f.Length
                    }
                    [void]$script:Messages.Add($chatMsg)
                    $sentMessageIds += $chatMsg.id
                    $fileMessageIndex++
                    Write-Host "  [$ts] CHAT FILE from $($chatMsg.fromName): $safeName" -ForegroundColor Magenta
                    # Forward file message to target server if remote
                    if ($targetTo -ne $script:DeviceId -and $targetIp -and $targetPort) {
                        try {
                            $fwdMsg = @{
                                id       = $chatMsg.id
                                from     = $fromId
                                fromName = $fromName
                                to       = $targetTo
                                text     = $safeName
                                ts       = $chatMsg.ts
                                type     = 'file'
                                fileSize = $f.Length
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
                Send-Json $resp @{ok=$true; ids=$sentMessageIds}
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
                $resp.Headers['Cache-Control'] = 'no-store, no-cache, must-revalidate'
                $resp.Headers['Pragma'] = 'no-cache'
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
                $dlPath = Get-QueryValueUtf8 $req.Url.Query 'path'
                if (-not $dlPath) { $dlPath = '' }
                $dlPath = $dlPath.TrimStart('/')
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
                $transferId = Get-QueryValueUtf8 $req.Url.Query 'transferId'
                $clientName = Get-QueryValueUtf8 $req.Url.Query 'clientName'
                $transferKind = Get-QueryValueUtf8 $req.Url.Query 'kind'
                if ([string]::IsNullOrWhiteSpace($clientName)) { $clientName = $req.RemoteEndPoint.Address.ToString() }
                if ([string]::IsNullOrWhiteSpace($transferKind)) { $transferKind = 'file' }
                $transfer = New-TransferRecord $transferId $dispName $fi.Length 'download' $transferKind $script:DeviceName $clientName
                $fs = [System.IO.File]::OpenRead($dlFull)
                try {
                    Copy-StreamWithProgress $fs $resp.OutputStream $transfer
                    Update-TransferRecord $transfer $fi.Length 'done'
                } catch {
                    Update-TransferRecord $transfer ([long]$transfer['bytesDone']) 'failed' $_.Exception.Message
                    throw
                } finally { $fs.Dispose() }
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
                    $uploadRel = Get-QueryValueUtf8 $req.Url.Query 'path'
                    if (-not $uploadRel) { $uploadRel = '' }
                    $uploadRel = $uploadRel.TrimStart('/')
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
                $boundary = Get-MultipartBoundary $req.ContentType
                $saved = @()
                $rawFileName = Get-QueryValueUtf8 $req.Url.Query 'name'
                if ($rawFileName) {
                    $safeName = [System.IO.Path]::GetFileName($rawFileName)
                    if ([string]::IsNullOrWhiteSpace($safeName)) {
                        Send-Text $resp "无效的文件名" 400
                        continue
                    }
                    $dest = Join-Path $targetDir $safeName
                    $transferId = [string]$req.Headers['X-Transfer-Id']
                    $clientName = [string]$req.Headers['X-Client-Name']
                    if ($clientName) { $clientName = [System.Uri]::UnescapeDataString($clientName) }
                    if ([string]::IsNullOrWhiteSpace($clientName)) { $clientName = $req.RemoteEndPoint.Address.ToString() }
                    $transfer = New-TransferRecord $transferId $safeName $req.ContentLength64 'upload' 'file' $clientName $script:DeviceName
                    try {
                        Save-RequestStream $req $dest $transfer
                        Update-TransferRecord $transfer (Get-Item -LiteralPath $dest).Length 'done'
                    } catch {
                        Update-TransferRecord $transfer ([long]$transfer['bytesDone']) 'failed' $_.Exception.Message
                        throw
                    }
                    $saved += $safeName
                } else {
                    # Backward-compatible multipart path for older pages.
                    if (-not $boundary) {
                        Send-Text $resp "无效的 Content-Type" 400
                        continue
                    }
                    $files = Parse-Multipart $req.InputStream $boundary
                    foreach ($f in $files) {
                        $safeName = [System.IO.Path]::GetFileName($f.Name)
                        $dest = Join-Path $targetDir $safeName
                        [System.IO.File]::WriteAllBytes($dest, $f.Bytes)
                        $saved += $safeName
                    }
                }
                if ($saved.Count -eq 0) {
                    Send-Text $resp "没有收到有效文件" 400
                    continue
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
