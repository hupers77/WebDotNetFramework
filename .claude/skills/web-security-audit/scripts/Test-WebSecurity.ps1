<#
.SYNOPSIS
  웹 사이트 접속 주소에 안전한 요청만 보내 보안 설정을 확인한다 (주통기반 웹 항목 3·4·5·6·7·16·17·18·19·21).

.DESCRIPTION
  보내는 요청: GET·HEAD·OPTIONS·TRACE 각각 몇 번 (모두 60건 안팎, 요청 사이 0.1초 쉼).
  로그인 시도, 공격 문자열, 데이터 변경, 부하는 보내지 않는다. 그래도 운영 서버는 운영 담당 동의 후 실행한다.

  확인하는 것:
   - HTTPS 사용, HTTP→HTTPS 리디렉트, HSTS 기간
   - 보안 헤더 (CSP, X-Content-Type-Options, X-Frame-Options/frame-ancestors, Referrer-Policy)
   - 버전 노출 헤더 (Server, X-Powered-By, X-AspNet-Version ...)
   - 쿠키 속성 (Secure, HttpOnly, SameSite)
   - TRACE 메서드, OPTIONS 허용 메서드
   - 노출되면 안 되는 경로 (.git, .env, web.config, 백업 파일, swagger, actuator, 관리 도구 ...)
   - 디렉터리 목록, 오류 화면의 스택 추적·버전
   - TLS 1.0/1.1 허용 여부, 인증서 만료일

  PowerShell 5.1(Windows 기본)과 PowerShell 7에서 동작한다.

.PARAMETER Url
  점검할 사이트 주소 (예: https://dev-portal.example.local). 경로가 있으면 그 경로를 첫 화면으로 본다.
.PARAMETER Paths
  쿠키·헤더를 더 볼 화면 경로 (예: /Account/Login). 로그인 화면을 넣으면 세션·CSRF 쿠키 속성까지 본다.
.PARAMETER OutFile
  결과 Markdown 파일. 없으면 화면에만 쓴다.
.PARAMETER SkipCertificateCheck
  사내 인증서(신뢰 안 되는 인증서)인 개발 서버에서 쓴다. 인증서 문제는 결과에 따로 적는다.
.PARAMETER NoTls
  TLS 버전 확인을 건너뛴다.

.EXAMPLE
  pwsh -File Test-WebSecurity.ps1 -Url https://dev-portal.example.local -Paths /Account/Login -OutFile .\web-check.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Url,
    [string[]]$Paths = @(),
    [string]$OutFile,
    [switch]$SkipCertificateCheck,
    [switch]$NoTls,
    [int]$TimeoutSec = 15
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$base = [Uri]$Url
if (-not $base.IsAbsoluteUri) { throw "주소는 http:// 또는 https:// 로 시작해야 합니다: $Url" }
$isCore = $PSVersionTable.PSEdition -eq 'Core'

# .NET Framework(PowerShell 5.1)는 TLS 1.2를 직접 켜야 한다
if (-not $isCore) {
    $p = [Net.SecurityProtocolType]::Tls12
    if ([enum]::GetNames([Net.SecurityProtocolType]) -contains 'Tls13') { $p = $p -bor [Net.SecurityProtocolType]::Tls13 }
    [Net.ServicePointManager]::SecurityProtocol = $p
}

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
if ($SkipCertificateCheck) {
    if ($isCore) {
        $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    } else {
        if (-not ('WsaTrustAllPolicy' -as [type])) {
            Add-Type @"
using System.Net; using System.Security.Cryptography.X509Certificates;
public class WsaTrustAllPolicy : ICertificatePolicy {
  public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
        }
        [Net.ServicePointManager]::CertificatePolicy = New-Object WsaTrustAllPolicy
    }
}
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('web-security-audit/1.0 (self-check)')

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$ju, [string]$item, [string]$verdict, [string]$detail) {
    $results.Add([pscustomobject]@{ Ju = $ju; Item = $item; Verdict = $verdict; Detail = $detail })
}

function Invoke-Req([string]$method, [string]$pathOrUrl, [Uri]$baseUri = $base) {
    Start-Sleep -Milliseconds 100
    $target = if ($pathOrUrl -match '^https?://') { [Uri]$pathOrUrl } else { New-Object Uri($baseUri, $pathOrUrl) }
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $target)
    try {
        $resp = $client.SendAsync($req).GetAwaiter().GetResult()
    } catch {
        $msg = $_.Exception.Message
        $inner = $_.Exception.InnerException
        while ($null -ne $inner) { $msg = $inner.Message; $inner = $inner.InnerException }
        return [pscustomobject]@{ Ok = $false; Status = 0; Headers = @{}; Cookies = @(); Body = ''; Error = $msg; Uri = $target }
    }
    $headers = @{}
    foreach ($h in $resp.Headers) { $headers[$h.Key.ToLowerInvariant()] = ($h.Value -join ', ') }
    foreach ($h in $resp.Content.Headers) { $headers[$h.Key.ToLowerInvariant()] = ($h.Value -join ', ') }
    $cookies = @()
    $vals = $null
    if ($resp.Headers.TryGetValues('Set-Cookie', [ref]$vals)) { $cookies = @($vals) }
    $body = ''
    if ($method -ne 'HEAD') {
        try {
            $bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $len = [Math]::Min($bytes.Length, 65536)
            $body = [System.Text.Encoding]::UTF8.GetString($bytes, 0, $len)
        } catch { $body = '' }
    }
    $status = [int]$resp.StatusCode
    $resp.Dispose()
    return [pscustomobject]@{ Ok = $true; Status = $status; Headers = $headers; Cookies = $cookies; Body = $body; Error = ''; Uri = $target }
}
function Get-H($r, [string]$name) { if ($r.Headers.ContainsKey($name)) { return $r.Headers[$name] } return $null }

$isLoopback = $base.IsLoopback
$loopNote = if ($isLoopback) { ' / 서버 자신(localhost)에서 실행해 보이는 것일 수 있음. 다른 PC에서 다시 확인' } else { '' }
$startPath = if ($base.AbsolutePath -and $base.AbsolutePath -ne '/') { $base.PathAndQuery } else { '/' }
$first = Invoke-Req 'GET' $startPath
if (-not $first.Ok) { throw "사이트에 연결하지 못했습니다: $($first.Error)" }
# 첫 화면이 로그인 화면으로 보내면 따라가서 그 화면도 본다 (쿠키 확인)
$pages = New-Object System.Collections.Generic.List[object]
$pages.Add($first)
if ($first.Status -ge 300 -and $first.Status -lt 400 -and (Get-H $first 'location')) {
    $loc = Get-H $first 'location'
    $next = Invoke-Req 'GET' $loc
    if ($next.Ok) { $pages.Add($next) }
}
foreach ($p in $Paths) { $r = Invoke-Req 'GET' $p; if ($r.Ok) { $pages.Add($r) } }
$main = $pages | Where-Object { $_.Status -ge 200 -and $_.Status -lt 300 } | Select-Object -First 1
if ($null -eq $main) { $main = $pages[$pages.Count - 1] }

# ---------------------------------------------------------------- 17 전송 구간
if ($base.Scheme -eq 'https') {
    Add-Result '17' 'HTTPS 사용' '양호' "$($base.Scheme)://$($base.Authority)"
    $httpUri = New-Object UriBuilder($base)
    $httpUri.Scheme = 'http'; $httpUri.Port = 80
    $plain = Invoke-Req 'GET' $httpUri.Uri.AbsoluteUri
    if (-not $plain.Ok) { Add-Result '17' 'HTTP(80) 접속' '양호' "HTTP 연결 안 됨 ($($plain.Error))" }
    elseif ($plain.Status -ge 300 -and $plain.Status -lt 400 -and ((Get-H $plain 'location') -match '^https://')) { Add-Result '17' 'HTTP→HTTPS 리디렉트' '양호' "$($plain.Status) → $(Get-H $plain 'location')" }
    else { Add-Result '17' 'HTTP→HTTPS 리디렉트' '취약' "HTTP 요청이 $($plain.Status)로 응답 (HTTPS로 보내지 않음)" }

    $hsts = Get-H $main 'strict-transport-security'
    if (-not $hsts) { Add-Result '17' 'HSTS' '취약' ('헤더 없음 (max-age=31536000; includeSubDomains 권고)' + $(if ($isLoopback) { ' / localhost에는 HSTS를 보내지 않는 서버가 많으므로(ASP.NET 기본) 실제 호스트 이름으로 다시 확인' } else { '' })) }
    elseif ($hsts -match 'max-age=(\d+)' -and [int64]$Matches[1] -ge 31536000) { Add-Result '17' 'HSTS' '양호' $hsts }
    else { Add-Result '17' 'HSTS' '권고' "$hsts (1년 이상 권고)" }
} else {
    Add-Result '17' 'HTTPS 사용' '취약' 'HTTP로 서비스 (로그인·개인정보가 평문 전송)'
}

# ---------------------------------------------------------------- 6 보안 헤더
$csp = Get-H $main 'content-security-policy'
if (-not $csp) { Add-Result '6' 'Content-Security-Policy' '권고' '헤더 없음' }
elseif ($csp -match "script-src[^;]*'unsafe-(inline|eval)'") { Add-Result '6' 'Content-Security-Policy' '권고' "script-src에 unsafe-inline/eval: $csp" }
else { Add-Result '6' 'Content-Security-Policy' '양호' $csp }
$xcto = Get-H $main 'x-content-type-options'
if ($xcto -match 'nosniff') { Add-Result '6' 'X-Content-Type-Options' '양호' $xcto } else { Add-Result '6' 'X-Content-Type-Options' '권고' '없음 (nosniff 권고)' }
$xfo = Get-H $main 'x-frame-options'
if ($xfo -match '(?i)deny|sameorigin' -or ($csp -and $csp -match 'frame-ancestors')) { Add-Result '6' '클릭재킹 방지' '양호' $(if ($xfo) { "X-Frame-Options: $xfo" } else { 'CSP frame-ancestors' }) }
else { Add-Result '6' '클릭재킹 방지' '권고' 'X-Frame-Options·frame-ancestors 없음' }
$rp = Get-H $main 'referrer-policy'
if ($rp) { Add-Result '5' 'Referrer-Policy' '양호' $rp } else { Add-Result '5' 'Referrer-Policy' '권고' '없음 (strict-origin-when-cross-origin 등)' }

# ---------------------------------------------------------------- 5 버전 노출 헤더
$server = Get-H $main 'server'
if (-not $server) { Add-Result '5' 'Server 헤더' '양호' '없음' }
elseif ($server -match '\d') { Add-Result '5' 'Server 헤더' '취약' "버전 노출: $server" }
else { Add-Result '5' 'Server 헤더' '권고' "제품 이름 노출: $server" }
$leakNames = @('x-powered-by', 'x-aspnet-version', 'x-aspnetmvc-version', 'x-generator', 'x-runtime', 'x-version', 'x-debug-token')
$leaks = @($leakNames | Where-Object { $main.Headers.ContainsKey($_) } | ForEach-Object { "$($_): $($main.Headers[$_])" })
if ($leaks.Count -gt 0) { Add-Result '5' '기술·버전 노출 헤더' '취약' ($leaks -join '; ') } else { Add-Result '5' '기술·버전 노출 헤더' '양호' '없음' }

# ---------------------------------------------------------------- 16·18 쿠키
$allCookies = @($pages | ForEach-Object { $_.Cookies } | Where-Object { $_ })
if ($allCookies.Count -eq 0) { Add-Result '18' '쿠키 속성' '확인' '본 화면에서 쿠키가 없음. -Paths 에 로그인 화면을 넣어 다시 확인' }
$seenCookies = @{}
foreach ($c in $allCookies) {
    $name = ($c -split '=', 2)[0].Trim()
    if ($seenCookies.ContainsKey($name)) { continue }
    $seenCookies[$name] = $true
    $miss = @()
    if ($base.Scheme -eq 'https' -and $c -notmatch '(?i);\s*secure') { $miss += 'Secure' }
    if ($c -notmatch '(?i);\s*httponly') { $miss += 'HttpOnly' }
    if ($c -notmatch '(?i);\s*samesite=') { $miss += 'SameSite' }
    if ($miss.Count -eq 0) { Add-Result '18' "쿠키 $name" '양호' '' }
    elseif ($miss -contains 'Secure') { Add-Result '18' "쿠키 $name" '취약' "빠진 속성: $($miss -join ', ')" }
    else { Add-Result '18' "쿠키 $name" '확인' "빠진 속성: $($miss -join ', ') (화면 JS가 읽어야 하는 쿠키가 아니면 HttpOnly)" }
}

# ---------------------------------------------------------------- 21 메서드
$trace = Invoke-Req 'TRACE' '/'
if (-not $trace.Ok) { Add-Result '21' 'TRACE 메서드' '양호' "연결 거부 ($($trace.Error))" }
elseif ($trace.Status -eq 200 -and $trace.Body -match '(?i)^TRACE ') { Add-Result '21' 'TRACE 메서드' '취약' '요청을 그대로 되돌려 줌 (XST)' }
elseif ($trace.Status -ge 200 -and $trace.Status -lt 300) { Add-Result '21' 'TRACE 메서드' '확인' "$($trace.Status) 응답" }
else { Add-Result '21' 'TRACE 메서드' '양호' "$($trace.Status)" }
$opt = Invoke-Req 'OPTIONS' '/'
$allow = if ($opt.Ok) { (@((Get-H $opt 'allow'), (Get-H $opt 'public')) | Where-Object { $_ }) -join ', ' } else { '' }
if ($allow -match '(?i)\b(PUT|DELETE|PROPFIND|PROPPATCH|MKCOL|COPY|MOVE|LOCK|UNLOCK|TRACE)\b') { Add-Result '21' 'OPTIONS 허용 메서드' '확인' "Allow: $allow (쓰지 않는 메서드는 막기)" }
else { Add-Result '21' 'OPTIONS 허용 메서드' '양호' $(if ($allow) { "Allow: $allow" } else { "$($opt.Status), Allow 헤더 없음" }) }

# ---------------------------------------------------------------- 3·5·19 노출 경로
$guid = [Guid]::NewGuid().ToString('N')
$soft = Invoke-Req 'GET' "/$guid"
$softBody = if ($soft.Ok -and $soft.Status -eq 200) { $soft.Body } else { $null }
$probes = @(
    @{ P='/.git/HEAD'; Ju='5'; Sev='취약'; Sig='ref:\s*refs/' }
    @{ P='/.git/config'; Ju='5'; Sev='취약'; Sig='\[core\]' }
    @{ P='/.svn/entries'; Ju='5'; Sev='취약'; Sig='' }
    @{ P='/.env'; Ju='5'; Sev='취약'; Sig='=' }
    @{ P='/web.config'; Ju='5'; Sev='취약'; Sig='<configuration' }
    @{ P='/appsettings.json'; Ju='5'; Sev='취약'; Sig='\{' }
    @{ P='/appsettings.Development.json'; Ju='5'; Sev='취약'; Sig='\{' }
    @{ P='/WEB-INF/web.xml'; Ju='5'; Sev='취약'; Sig='<web-app' }
    @{ P='/backup.zip'; Ju='5'; Sev='취약'; Sig='' }
    @{ P='/backup.sql'; Ju='5'; Sev='취약'; Sig='' }
    @{ P='/db.sql'; Ju='5'; Sev='취약'; Sig='' }
    @{ P='/www.zip'; Ju='5'; Sev='취약'; Sig='' }
    @{ P='/.DS_Store'; Ju='5'; Sev='권고'; Sig='' }
    @{ P='/phpinfo.php'; Ju='5'; Sev='취약'; Sig='phpinfo|PHP Version' }
    @{ P='/info.php'; Ju='5'; Sev='취약'; Sig='PHP Version' }
    @{ P='/server-status'; Ju='5'; Sev='취약'; Sig='Server Status|Apache' }
    @{ P='/elmah.axd'; Ju='5'; Sev='취약'; Sig='(?i)error log' }
    @{ P='/trace.axd'; Ju='5'; Sev='취약'; Sig='(?i)trace' }
    @{ P='/actuator/env'; Ju='5'; Sev='취약'; Sig='propertySources|activeProfiles' }
    @{ P='/actuator'; Ju='5'; Sev='확인'; Sig='_links' }
    @{ P='/h2-console'; Ju='19'; Sev='취약'; Sig='' }
    @{ P='/manager/html'; Ju='19'; Sev='취약'; Sig='' }
    @{ P='/phpmyadmin/'; Ju='19'; Sev='취약'; Sig='' }
    @{ P='/swagger/index.html'; Ju='5'; Sev='확인'; Sig='(?i)swagger' }
    @{ P='/swagger-ui.html'; Ju='5'; Sev='확인'; Sig='(?i)swagger' }
    @{ P='/v2/api-docs'; Ju='5'; Sev='확인'; Sig='swagger' }
    @{ P='/iisstart.htm'; Ju='5'; Sev='권고'; Sig='(?i)iis' }
    @{ P='/health'; Ju='5'; Sev='health'; Sig='' }
    @{ P='/admin'; Ju='19'; Sev='admin'; Sig='' }
    @{ P='/robots.txt'; Ju='5'; Sev='info'; Sig='(?i)disallow' }
)
$exposed = 0
foreach ($pr in $probes) {
    $r = Invoke-Req 'GET' $pr.P
    if (-not $r.Ok -or $r.Status -eq 404 -or $r.Status -eq 410 -or $r.Status -eq 400) { continue }
    $isSoft404 = ($null -ne $softBody -and $r.Status -eq 200 -and $r.Body -eq $softBody)
    if ($isSoft404) { continue }
    $sigOk = (-not $pr.Sig) -or ($r.Body -match $pr.Sig)
    if ($r.Status -eq 200 -and -not $sigOk) { continue }
    switch ($pr.Sev) {
        'health' {
            if ($r.Status -eq 200 -and $r.Body -match '(?i)"?version"?\s*[:=]') { Add-Result '5' "노출 경로 $($pr.P)" '취약' ('로그인 없이 버전을 알려 줌' + $loopNote) }
            elseif ($r.Status -eq 200) { Add-Result '5' "노출 경로 $($pr.P)" '양호' '상태만 응답' }
        }
        'admin' {
            if ($r.Status -eq 200) { Add-Result '19' "관리 경로 $($pr.P)" '확인' '로그인 없이 열림. 사내 IP 제한 권고' }
            elseif ($r.Status -ge 300 -and $r.Status -lt 400) { Add-Result '19' "관리 경로 $($pr.P)" '확인' "$($r.Status) → $(Get-H $r 'location') (외부에서 접근되면 IP 제한 권고)" }
        }
        'info' { if ($r.Status -eq 200) { Add-Result '5' 'robots.txt' '확인' ((($r.Body -split "`r?`n") | Where-Object { $_ -match '(?i)disallow' } | Select-Object -First 8) -join ' / ') } }
        default {
            if ($r.Status -eq 200) { Add-Result $pr.Ju "노출 경로 $($pr.P)" $pr.Sev "200 ($($r.Body.Length) bytes)"; $exposed++ }
            elseif ($r.Status -eq 401 -or $r.Status -eq 403) { if ($pr.P -match '^/\.(git|svn|env)') { Add-Result $pr.Ju "노출 경로 $($pr.P)" '확인' "$($r.Status): 파일이 서버에 있을 수 있음. 웹 루트에서 지우기" } }
        }
    }
}
if ($exposed -eq 0) { Add-Result '5' '노출 경로 (백업·설정·소스 관리 파일)' '양호' "$($probes.Count)개 경로 확인, 열리는 파일 없음" }

# ---------------------------------------------------------------- 3 디렉터리 목록
$dirs = @('/', '/css/', '/js/', '/images/', '/img/', '/lib/', '/upload/', '/uploads/', '/files/', '/static/', '/assets/') + @($Paths | Where-Object { $_.EndsWith('/') })
$listing = @()
foreach ($d in ($dirs | Select-Object -Unique)) {
    $r = Invoke-Req 'GET' $d
    if ($r.Ok -and $r.Status -eq 200 -and $r.Body -match '(?i)<title>\s*(Index of|Directory Listing)|\[To Parent Directory\]|Directory listing for|<h1>Index of') { $listing += $d }
}
if ($listing.Count -gt 0) { Add-Result '3' '디렉터리 목록' '취약' "목록이 보임: $($listing -join ', ')" } else { Add-Result '3' '디렉터리 목록' '양호' "$(@($dirs | Select-Object -Unique).Count)개 폴더 확인" }

# ---------------------------------------------------------------- 4·5 오류 화면
$errPatterns = '(?i)Server Error in ''/|Stack Trace:|stack trace|\bat System\.[A-Z]|\bat java\.|\bat org\.springframework|Exception:|SQLSTATE|ORA-\d{5}|Microsoft OLE DB|Whitelabel Error Page|Traceback \(most recent call last\)|Fatal error:|Warning:.* on line \d+|Apache Tomcat/\d|Microsoft-IIS/\d|Apache/\d|nginx/\d|ASP\.NET is configured|Version Information:'
$errHits = @()
foreach ($ep in @("/$guid.aspx", "/$guid.jsp", "/$guid.php", "/$guid/%2e%2e/", "/?$guid=%00")) {
    $r = Invoke-Req 'GET' $ep
    if ($r.Ok -and $r.Body -match $errPatterns) { $errHits += "$ep ($($r.Status)): $($Matches[0])" }
}
if ($errHits.Count -gt 0) { Add-Result '4' '오류 화면 정보 노출' '취약' ($errHits -join '; ') } else { Add-Result '4' '오류 화면 정보 노출' '양호' '없는 주소·잘못된 주소에서 스택 추적·버전 없음' }

# ---------------------------------------------------------------- 17 TLS
if (-not $NoTls -and $base.Scheme -eq 'https') {
    $port = if ($base.Port -gt 0) { $base.Port } else { 443 }
    $names = [enum]::GetNames([System.Security.Authentication.SslProtocols])
    $script:certInfo = $null
    foreach ($proto in @('Tls', 'Tls11', 'Tls12', 'Tls13')) {
        if ($names -notcontains $proto) { continue }
        $tcp = New-Object System.Net.Sockets.TcpClient
        $ok = $false; $why = ''
        try {
            $tcp.Connect($base.Host, $port)
            $cb = [System.Net.Security.RemoteCertificateValidationCallback] {
                param($s, $cert, $chain, $errors)
                if ($null -ne $cert -and $null -eq $script:certInfo) {
                    $c2 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cert)
                    $script:certInfo = [pscustomobject]@{ Subject = $c2.Subject; NotAfter = $c2.NotAfter; Errors = [string]$errors }
                }
                return $true
            }
            $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, $cb)
            $ssl.AuthenticateAsClient($base.Host, $null, [System.Security.Authentication.SslProtocols]::$proto, $false)
            $ok = $true
            $ssl.Dispose()
        } catch { $why = $_.Exception.Message; if ($_.Exception.InnerException) { $why = $_.Exception.InnerException.Message } }
        finally { $tcp.Dispose() }
        $label = @{ Tls = 'TLS 1.0'; Tls11 = 'TLS 1.1'; Tls12 = 'TLS 1.2'; Tls13 = 'TLS 1.3' }[$proto]
        if ($proto -in @('Tls', 'Tls11')) {
            if ($ok) { Add-Result '17' $label '취약' '서버가 허용함 (TLS 1.2 이상만 허용)' }
            else { Add-Result '17' $label '양호' '연결 안 됨 (서버가 막았거나 이 PC가 지원하지 않음)' }
        } else {
            Add-Result '17' $label '정보' $(if ($ok) { '연결됨' } else { "연결 안 됨: $why" })
        }
    }
    if ($script:certInfo) {
        $days = [int]([datetime]$script:certInfo.NotAfter - (Get-Date)).TotalDays
        $v = if ($days -lt 0) { '취약' } elseif ($days -lt 30) { '권고' } else { '양호' }
        $extra = if ($script:certInfo.Errors -ne 'None') { " / 검증 오류: $($script:certInfo.Errors) (사내 인증서면 PC 신뢰 저장소 확인)" } else { '' }
        Add-Result '17' '서버 인증서' $v "$($script:certInfo.Subject), 만료 $(([datetime]$script:certInfo.NotAfter).ToString('yyyy-MM-dd')) ($($days)일 남음)$extra"
    }
}

# ---------------------------------------------------------------- 출력
$order = @{ '취약' = 0; '권고' = 1; '확인' = 2; '양호' = 3; '정보' = 4 }
$sorted = $results | Sort-Object @{ Expression = { $order[$_.Verdict] } }, @{ Expression = { [int]($_.Ju -replace '\D', '0') } }
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('# 접속 점검 결과 (자동)')
[void]$md.AppendLine()
[void]$md.AppendLine("- 대상: $($base.AbsoluteUri)")
[void]$md.AppendLine("- 시각: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$md.AppendLine("- 본 화면: $(@($pages | ForEach-Object { "$($_.Uri.PathAndQuery) ($($_.Status))" }) -join ', ')")
[void]$md.AppendLine("- 결과: 취약 $(@($results | Where-Object Verdict -eq '취약').Count), 권고 $(@($results | Where-Object Verdict -eq '권고').Count), 확인 $(@($results | Where-Object Verdict -eq '확인').Count), 양호 $(@($results | Where-Object Verdict -eq '양호').Count)")
[void]$md.AppendLine('- 안전한 요청(GET·HEAD·OPTIONS·TRACE)만 보냈다. SQL 삽입·XSS·권한 우회 같은 항목은 소스 점검과 전문기관 진단으로 확인한다.')
[void]$md.AppendLine()
[void]$md.AppendLine('| 주통기반 | 항목 | 결과 | 내용 |')
[void]$md.AppendLine('| --- | --- | --- | --- |')
foreach ($x in $sorted) { [void]$md.AppendLine("| $($x.Ju) | $($x.Item) | $($x.Verdict) | $(([string]$x.Detail).Replace('|', '\|')) |") }
$text = $md.ToString()
if ($OutFile) {
    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutFile), $text, (New-Object System.Text.UTF8Encoding $true))
    Write-Host "결과: $OutFile"
}
$sorted | Format-Table Ju, Verdict, Item, Detail -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$client.Dispose()
