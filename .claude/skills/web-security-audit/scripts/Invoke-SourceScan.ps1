<#
.SYNOPSIS
  소스 저장소에서 알려진 취약 패키지와 위험한 코드 패턴 "후보"를 찾는다 (KISA 보안약점·주통기반 항목별).

.DESCRIPTION
  1) 런타임·프레임워크 버전 정보 (global.json, TargetFramework, package.json engines 등)
  2) 취약 패키지 검사: dotnet list package --vulnerable / npm audit / pip-audit / composer audit (설치된 도구만)
  3) 위험 패턴 검사: SQL 문자열 조립, 인코딩 없는 출력, 명령 실행, 역직렬화, 약한 암호, 비밀값 하드코딩,
     인증서 검증 끄기, CSRF 예외, 디버그 설정, 쿠키 속성, CORS, 정규식 중첩 반복 등
  결과는 판정이 아니라 사람이(또는 Claude가) 코드를 따라가 확인할 후보 목록이다.
  비밀값으로 보이는 값은 *** 로 가려서 남긴다.

  PowerShell 5.1(Windows 기본)과 PowerShell 7에서 동작한다. 소스는 읽기만 한다.

.PARAMETER Path
  점검할 저장소 폴더. 기본은 현재 폴더.
.PARAMETER OutDir
  결과 폴더. scan-findings.csv(엑셀에서 열림), scan-summary.md 를 쓴다. 기본 ./security-scan
.PARAMETER Exclude
  더 뺄 경로 조각(예: 'legacy','samples'). 기본으로 .git, bin, obj, node_modules, vendor, 빌드 결과, 압축 JS는 뺀다.
.PARAMETER SkipDependencies
  패키지 검사를 건너뛴다 (인터넷이 막힌 PC 등).
.PARAMETER NoRestore
  dotnet 패키지 검사 전에 restore 하지 않는다 (이미 빌드한 저장소).

.EXAMPLE
  pwsh -File Invoke-SourceScan.ps1 -Path C:\src\gate-portal -OutDir C:\temp\scan
#>
[CmdletBinding()]
param(
    [string]$Path = '.',
    [string]$OutDir = './security-scan',
    [string[]]$Exclude = @(),
    [switch]$SkipDependencies,
    [switch]$NoRestore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Path).ProviderPath
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$outRoot = (Resolve-Path -LiteralPath $OutDir).ProviderPath
$utf8Bom = New-Object System.Text.UTF8Encoding $true

# ---------------------------------------------------------------- 대상 파일
$excludeParts = @('\.git', '\.svn', '\.vs', '\.idea', 'bin', 'obj', 'node_modules', 'vendor', 'packages', 'dist', 'build', 'target',
                  'out', 'TestResults', 'coverage', 'wwwroot[\\/]lib', 'security-scan', 'web-security-audit') + $Exclude
$excludeRegex = '(^|[\\/])(' + (($excludeParts | ForEach-Object { $_ }) -join '|') + ')([\\/]|$)'

$codeExt   = @('cs','vb','cshtml','vbhtml','razor','aspx','ascx','asmx','ashx','master','asp','java','jsp','jspx','kt','php','inc','py','js','mjs','cjs','ts','tsx','jsx','vue','html','htm','sql','xml','config','json','properties','yml','yaml','ini','env','conf','gradle')

$files = Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
    $rel = $_.FullName.Substring($root.Length)
    $ext = $_.Extension.TrimStart('.').ToLowerInvariant()
    ($codeExt -contains $ext -or $_.Name -like '.env*') -and
    ($rel -notmatch $excludeRegex) -and
    ($_.Name -notmatch '\.min\.(js|css)$') -and
    ($_.Length -lt 2MB)
}

# ---------------------------------------------------------------- 규칙
# Ext: 적용 확장자, Need: 줄에 이것도 있어야 함(소음 줄이기), Sev: 높음/중간/낮음/확인 (후보의 무게, 판정 아님)
$code  = 'cs,vb,cshtml,vbhtml,razor,aspx,ascx,asmx,ashx,master,asp,java,jsp,jspx,kt,php,inc,py,js,mjs,cjs,ts,tsx,jsx,vue,html,htm'
$cfg   = 'config,xml,json,properties,yml,yaml,ini,env,conf'
$rules = @(
    # --- 입력데이터 검증: SQL 삽입 (주통기반 2)
    @{ Id='SQL-CONCAT';    Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext=$code; Pat='(?i)("[^"\r\n]*\b(select\s[^"\r\n]+\sfrom|insert\s+into|update\s+\w+\s+set|delete\s+from)\b[^"\r\n]*"|''[^''\r\n]*\b(select\s[^''\r\n]+\sfrom|insert\s+into|update\s+\w+\s+set|delete\s+from)\b[^''\r\n]*'')\s*(\+|&|\.)\s*[\w$(]'; Hint='SQL 문자열에 값을 이어 붙임. 파라미터 바인딩으로' }
    @{ Id='SQL-INTERP';    Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext=$code; Pat='(?i)(\$@?"[^"\r\n]*|\bf"[^"\r\n]*|\bf''[^''\r\n]*|`[^`\r\n]*)\b(select\s.+\sfrom|insert\s+into|update\s+\w+\s+set|delete\s+from|where)\b[^\r\n]*(\{|\$\{)'; Hint='보간 문자열 SQL' }
    @{ Id='SQL-FORMAT';    Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext=$code; Pat='(?i)(String\.Format|sprintf|\.format)\s*\(\s*["''][^"'']*\b(select|insert\s+into|update|delete\s+from)\b'; Hint='서식 문자열 SQL' }
    @{ Id='SQL-JAVA-EXEC'; Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext='java,jsp,kt'; Pat='\.(executeQuery|executeUpdate|execute|prepareStatement|createQuery|createNativeQuery)\s*\([^)]*\+'; Hint='JDBC/JPA 쿼리에 문자열 이어 붙임' }
    @{ Id='SQL-PHP-INPUT'; Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext='php,inc'; Pat='(?i)(mysqli?_query|->query|pg_query|sqlsrv_query)\s*\([^;]*\$_(GET|POST|REQUEST|COOKIE)'; Hint='요청 값을 바로 쿼리에' }
    @{ Id='SQL-MYBATIS';   Cat='SQL 삽입'; Ju='2'; Sev='높음'; Ext='xml'; Pat='\$\{[^}]+\}'; Need='(?i)(select|insert|update|delete|where|order\s+by|and|or|in\s*\()'; Hint='MyBatis ${} 는 그대로 삽입됨. #{} 로' }
    @{ Id='SQL-DYNAMIC';   Cat='SQL 삽입'; Ju='2'; Sev='확인'; Ext='sql'; Pat='(?i)\bexec(ute)?\s*\(\s*@|\bsp_executesql\b'; Hint='동적 SQL. 사용자 값이 들어가면 QUOTENAME·파라미터로' }

    # --- XSS (주통기반 6)
    @{ Id='XSS-RAW';       Cat='크로스사이트 스크립트'; Ju='6'; Sev='중간'; Ext='cs,cshtml,vbhtml,razor,aspx,ascx'; Pat='Html\.Raw\s*\(|new\s+HtmlString\s*\(|MvcHtmlString\.Create'; Hint='인코딩 없는 출력. 값이 사용자 입력이면 필터(화이트리스트)를 거쳤는지' }
    @{ Id='XSS-DOM';       Cat='크로스사이트 스크립트'; Ju='6'; Sev='중간'; Ext='js,mjs,ts,tsx,jsx,vue,html,htm,cshtml,jsp,php,aspx'; Pat='\.(innerHTML|outerHTML)\s*\+?=|insertAdjacentHTML\s*\(|document\.write(ln)?\s*\('; Hint='DOM에 HTML 문자열. textContent 로' }
    @{ Id='XSS-FRAMEWORK'; Cat='크로스사이트 스크립트'; Ju='6'; Sev='중간'; Ext='js,ts,tsx,jsx,vue,html'; Pat='\bv-html\s*=|dangerouslySetInnerHTML|\[innerHTML\]|bypassSecurityTrust'; Hint='프레임워크의 인코딩 끄기' }
    @{ Id='XSS-JQUERY';    Cat='크로스사이트 스크립트'; Ju='6'; Sev='확인'; Ext='js,ts,html,htm,cshtml,jsp,php,aspx'; Pat='\.html\(\s*[A-Za-z_$][\w$.\[\]]*\s*\)'; Hint='jQuery .html(값)' }
    @{ Id='XSS-SERVER';    Cat='크로스사이트 스크립트'; Ju='6'; Sev='높음'; Ext='jsp,jspx,asp,aspx,php,inc'; Pat='(?i)<%=\s*request\.getParameter|<%=\s*Request(\.QueryString|\.Form)?\s*[\[(]|\becho\s+\$_(GET|POST|REQUEST|COOKIE)|\bprint\s+\$_(GET|POST|REQUEST)|\$\{\s*param\.'; Hint='요청 값을 인코딩 없이 출력' }

    # --- 코드·명령 삽입 (주통기반 1)
    @{ Id='CODE-EVAL';     Cat='코드 삽입'; Ju='1'; Sev='높음'; Ext='js,mjs,ts,tsx,jsx,vue,html,htm,php,py,cshtml,jsp'; Pat='(?<![\w.])eval\s*\(|new\s+Function\s*\(|set(Timeout|Interval)\s*\(\s*["'']'; Hint='문자열을 코드로 실행' }
    @{ Id='CMD-DOTNET';    Cat='운영체제 명령어 삽입'; Ju='1'; Sev='확인'; Ext='cs,vb'; Pat='Process\.Start\s*\(|new\s+ProcessStartInfo|\.Arguments\s*='; Hint='외부 프로세스. 셸 없이 ArgumentList, 사용자 값 금지' }
    @{ Id='CMD-JAVA';      Cat='운영체제 명령어 삽입'; Ju='1'; Sev='높음'; Ext='java,jsp,kt'; Pat='Runtime\.getRuntime\(\)\.exec|new\s+ProcessBuilder'; Hint='외부 명령 실행' }
    @{ Id='CMD-PHP';       Cat='운영체제 명령어 삽입'; Ju='1'; Sev='높음'; Ext='php,inc'; Pat='(?<![\w>:])(shell_exec|passthru|proc_open|popen|system|exec)\s*\(|`[^`]*\$'; Hint='외부 명령 실행' }
    @{ Id='CMD-PY';        Cat='운영체제 명령어 삽입'; Ju='1'; Sev='높음'; Ext='py'; Pat='os\.(system|popen)\s*\(|subprocess\.\w+\([^)]*shell\s*=\s*True'; Hint='셸 명령 실행' }
    @{ Id='CMD-ASP';       Cat='운영체제 명령어 삽입'; Ju='1'; Sev='높음'; Ext='asp,aspx,vb'; Pat='(?i)WScript\.Shell'; Hint='셸 객체' }

    # --- 경로 조작·다운로드 (주통기반 14·15)
    @{ Id='PATH-DOTNET';   Cat='경로 조작 및 자원 삽입'; Ju='15'; Sev='중간'; Ext='cs,vb,aspx,ashx'; Pat='(Path\.Combine|File\.(Open\w*|Read\w*|Write\w*|Delete|Copy|Move|Exists)|new\s+FileStream|PhysicalFile|Server\.MapPath)\s*\([^)]*(Request\b|QueryString|\.Form\[)'; Hint='요청 값으로 파일 경로' }
    @{ Id='PATH-JAVA';     Cat='경로 조작 및 자원 삽입'; Ju='15'; Sev='중간'; Ext='java,jsp,kt'; Pat='new\s+File(InputStream|OutputStream|Reader|Writer)?\s*\([^)]*getParameter|Paths\.get\s*\([^)]*getParameter'; Hint='요청 값으로 파일 경로' }
    @{ Id='PATH-PHP';      Cat='경로 조작 및 자원 삽입'; Ju='15'; Sev='높음'; Ext='php,inc'; Pat='(?i)\b(include|require)(_once)?\s*\(?\s*\$_(GET|POST|REQUEST)|\b(fopen|readfile|file_get_contents|unlink)\s*\([^)]*\$_(GET|POST|REQUEST)'; Hint='요청 값으로 파일 열기·포함' }
    @{ Id='UPLOAD';        Cat='위험한 형식 파일 업로드'; Ju='14'; Sev='확인'; Ext='cs,vb,aspx,java,jsp,kt,php,py,js,ts'; Pat='\.SaveAs\s*\(|IFormFile|move_uploaded_file\s*\(|\.transferTo\s*\(|MultipartFile|multer\s*\(|request\.files'; Hint='업로드 처리: 확장자 허용 목록, 내용 확인, 웹 루트 밖 저장, 무작위 이름, 크기' }

    # --- 리디렉트 (KISA 신뢰되지 않는 URL 자동접속)
    @{ Id='REDIRECT';      Cat='신뢰되지 않는 URL 자동접속'; Ju='-'; Sev='확인'; Ext='cs,vb,aspx,java,jsp,kt,php,py'; Pat='(?i)\bRedirect\s*\(\s*(returnUrl|redirectUrl|url|next|Request\b)|sendRedirect\s*\([^)]*getParameter|header\s*\(\s*["'']Location:[^)]*\$_(GET|REQUEST)'; Hint='사용자 값으로 리디렉트. 사이트 안 주소인지(LocalRedirect, IsLocalUrl)' }
    @{ Id='REDIRECT-JS';   Cat='신뢰되지 않는 URL 자동접속'; Ju='-'; Sev='확인'; Ext='js,ts,html,htm,cshtml,jsp,php'; Pat='location(\.href)?\s*=\s*[^;]*(location\.(search|hash)|URLSearchParams|getParameter)'; Hint='주소 값으로 이동' }

    # --- CSRF·요청 검증 끄기 (주통기반 7)
    @{ Id='CSRF-OFF';      Cat='크로스사이트 요청 위조'; Ju='7'; Sev='중간'; Ext="$code,$cfg"; Pat='IgnoreAntiforgeryToken|csrf\(\)\.disable\(\)|\.csrf\([^)]*disable|@csrf_exempt|(?i)validateRequest\s*=\s*"false"|(?i)enableEventValidation\s*=\s*"false"|ValidateInput\s*\(\s*false|requestValidationMode\s*=\s*"2\.0"'; Hint='CSRF·요청 검증 예외' }
    @{ Id='AUTH-ANON';     Cat='적절한 인증 없는 중요 기능 허용'; Ju='11'; Sev='확인'; Ext='cs,vb,java,kt,py'; Pat='\[AllowAnonymous\]|@PermitAll|permitAll\(\)|\.anonymous\(\)'; Hint='로그인 없이 열리는 기능 목록(의도한 것만인지)' }

    # --- 역직렬화·XXE
    @{ Id='DESER';         Cat='신뢰할 수 없는 데이터의 역직렬화'; Ju='1'; Sev='높음'; Ext='cs,vb,aspx,java,jsp,kt,php,py'; Pat='BinaryFormatter|NetDataContractSerializer|LosFormatter|ObjectStateFormatter|SoapFormatter|TypeNameHandling\s*=\s*TypeNameHandling\.(All|Auto|Objects|Arrays)|SimpleTypeResolver|new\s+ObjectInputStream|XMLDecoder|\bXStream\b|\bunserialize\s*\(|pickle\.loads?\s*\(|yaml\.load\s*\((?![^)]*SafeLoader)'; Hint='형식 정보를 담은 역직렬화' }
    @{ Id='XXE';           Cat='부적절한 XML 외부개체 참조'; Ju='1'; Sev='중간'; Ext='cs,vb,java,kt,php'; Pat='DtdProcessing\s*=\s*DtdProcessing\.Parse|ProhibitDtd\s*=\s*false|XmlResolver\s*=\s*new\s+XmlUrlResolver|DocumentBuilderFactory\.newInstance|SAXParserFactory\.newInstance|XMLInputFactory\.newInstance|LIBXML_NOENT|libxml_disable_entity_loader\s*\(\s*false'; Hint='DTD·외부 개체 처리 끄기 확인' }

    # --- 암호·난수·비밀값
    @{ Id='CRYPTO-HASH';   Cat='취약한 암호화 알고리즘'; Ju='-'; Sev='중간'; Ext=$code; Pat='\b(MD5|SHA1)\.(Create|HashData)\b|new\s+(MD5|SHA1)(CryptoServiceProvider|Managed)|MessageDigest\.getInstance\(\s*"(MD5|SHA-?1)"|hashlib\.(md5|sha1)\b|(?<![\w.])(md5|sha1)\s*\(|FormsAuthentication\.HashPasswordForStoringInConfigFile'; Hint='MD5·SHA-1. 비밀번호면 PBKDF2·bcrypt·Argon2 + 솔트' }
    @{ Id='CRYPTO-CIPHER'; Cat='취약한 암호화 알고리즘'; Ju='-'; Sev='높음'; Ext=$code; Pat='\b(DES|TripleDES|RC2)\.Create\b|new\s+(DES|TripleDES|RC2)CryptoServiceProvider|Cipher\.getInstance\(\s*"(DES|DESede|RC4|AES/ECB|AES")|CipherMode\.ECB|\bmcrypt_|createCipher\('; Hint='DES·3DES·RC4·ECB. AES-GCM 등으로' }
    @{ Id='RANDOM';        Cat='적절하지 않은 난수'; Ju='-'; Sev='확인'; Ext=$code; Pat='new\s+Random\s*\(|Math\.random\s*\(|java\.util\.Random|(?<![\w>])(rand|mt_rand)\s*\(|random\.(random|randint|choice)\s*\('; Hint='토큰·비밀번호·OTP에 쓰면 보안 난수(RandomNumberGenerator, SecureRandom, secrets)' }
    @{ Id='SECRET';        Cat='하드코드된 중요정보'; Ju='-'; Sev='높음'; Ext="$code,$cfg"; Pat='(?i)\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|client[_-]?secret|private[_-]?key|auth[_-]?token)\b["'']?\s*[:=]\s*["''][^"''\s$%{<]{6,}["'']'; Hint='비밀값 하드코딩. 환경 변수·비밀 저장소로' }
    @{ Id='SECRET-CONN';   Cat='하드코드된 중요정보'; Ju='-'; Sev='높음'; Ext="$code,$cfg"; Pat='(?i)(;|")\s*(password|pwd)\s*=\s*[^;"''\s{$%<]{3,}'; Hint='연결 문자열의 비밀번호' }
    @{ Id='SECRET-KEY';    Cat='하드코드된 중요정보'; Ju='-'; Sev='높음'; Ext="$code,$cfg,pem,key"; Pat='-----BEGIN (RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----'; Hint='개인 키 파일·문자열' }
    @{ Id='TLS-NOVERIFY';  Cat='부적절한 인증서 유효성 검증'; Ju='17'; Sev='높음'; Ext=$code; Pat='ServerCertificateCustomValidationCallback\s*=|ServerCertificateValidationCallback\s*\+?=|DangerousAcceptAnyServerCertificateValidator|CertificatePolicy\s*=|X509TrustManager|ALLOW_ALL_HOSTNAME_VERIFIER|HostnameVerifier|verify\s*=\s*False|rejectUnauthorized\s*:\s*false|NODE_TLS_REJECT_UNAUTHORIZED|CURLOPT_SSL_VERIFY(PEER|HOST)\s*,\s*(0|false)'; Hint='인증서 검증을 끄거나 바꿈' }

    # --- 오류·디버그·정보 노출 (주통기반 4·5)
    @{ Id='DEBUG';         Cat='제거되지 않은 디버그 코드'; Ju='4'; Sev='중간'; Ext="$code,$cfg"; Pat='UseDeveloperExceptionPage|(?i)customErrors\s+mode\s*=\s*"Off"|(?i)<compilation[^>]*debug\s*=\s*"true"|(?i)display_errors\s*=\s*(On|1)|\bphpinfo\s*\(|\.printStackTrace\s*\(|\bDEBUG\s*=\s*True|debug\s*=\s*True\s*\)|(?i)<trace\s+enabled\s*=\s*"true"'; Hint='운영에서 오류 상세·디버그가 보이는지' }
    @{ Id='ERROR-LEAK';    Cat='오류 메시지 정보노출'; Ju='5'; Sev='중간'; Ext='cs,vb,aspx,java,jsp,kt,php,py,js,ts'; Pat='(?i)(Content|Json|Ok|BadRequest|Problem|Write|send|json|print\w*|echo)\s*\([^;]*\b(ex|e|err|exception)\.(Message|StackTrace|ToString\(\)|getMessage\(\)|stack)'; Hint='예외 내용을 응답에' }
    @{ Id='LOG-SENSITIVE'; Cat='오류 메시지 정보노출'; Ju='5'; Sev='중간'; Ext=$code; Pat='(?i)(\b_?(logger|log)\.\w+|\bLog(Information|Warning|Error|Debug|Critical|Trace)|console\.(log|error|warn|info)|System\.out\.print\w*|Debug\.Write\w*|error_log|logging\.\w+)\s*\([^;]*(password|passwd|pwd|jumin|ssn|rrn|card_?no|access_?token|refresh_?token)'; Hint='로그에 비밀번호·개인정보·토큰' }

    # --- 쿠키·CORS·세션 (주통기반 16·18)
    @{ Id='COOKIE';        Cat='쿠키를 통한 정보노출'; Ju='18'; Sev='중간'; Ext="$code,$cfg"; Pat='(?i)HttpOnly\s*=\s*false|\bSecure\s*=\s*false|httpOnlyCookies\s*=\s*"false"|requireSSL\s*=\s*"false"|cookieless\s*=\s*"(UseUri|true)"|SameSite\s*=\s*SameSiteMode\.None|setHttpOnly\(\s*false|setSecure\(\s*false|CookieSecurePolicy\.(None|SameAsRequest)'; Hint='쿠키 보안 속성 끔' }
    @{ Id='CORS';          Cat='중요한 자원에 대한 잘못된 권한 설정'; Ju='-'; Sev='중간'; Ext="$code,$cfg"; Pat='AllowAnyOrigin\s*\(\)|SetIsOriginAllowed\s*\(\s*_?\w*\s*=>\s*true|Access-Control-Allow-Origin["'']?\s*[,:=]\s*["'']\*|@CrossOrigin\s*(\(\s*\))?\s*$|origins\s*=\s*["'']\*'; Hint='모든 출처 허용 CORS' }

    # --- 정규식 서비스 거부 (KISA 종료되지 않는 반복문)
    @{ Id='REDOS';         Cat='종료되지 않는 반복문(ReDoS)'; Ju='-'; Sev='확인'; Ext='cs,vb,java,kt,js,ts,py,php'; Pat='\((?:[^()\r\n]|\\.)*[+*]\??\)[+*{]'; Need='(?i)regex|regexp|pattern|preg_|re\.(compile|match|search|sub|findall)|GeneratedRegex|/[gimsuy]*\s*[,;)]'; Hint='중첩 반복 정규식. 긴 입력으로 시간 측정, 시간 제한(MatchTimeout) 확인' }

    # --- 개인정보 항목 (암호화 확인)
    @{ Id='PII-COLUMN';    Cat='암호화되지 않은 중요정보'; Ju='-'; Sev='확인'; Ext='sql,cs,vb,java,kt,xml,php,py'; Pat='(?i)\b(jumin\w*|rrn|resident_?(no|num)|ssn|passport_?no|driver_?licen[cs]e\w*|card_?no|account_?no|bank_?account)\b|주민(등록)?번호|여권번호|운전면허|계좌번호|카드번호'; Hint='고유식별정보·금융정보. 저장한다면 암호화·마스킹·접근 기록' }
)

foreach ($r in $rules) {
    $r.ExtSet = @($r.Ext -split ',')
    $r.Rx = New-Object System.Text.RegularExpressions.Regex($r.Pat, [System.Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))
    if ($r.ContainsKey('Need')) { $r.NeedRx = New-Object System.Text.RegularExpressions.Regex($r.Need, [System.Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2)) } else { $r.NeedRx = $null }
}

# 비밀값으로 보이는 부분을 가린다
$maskRx = New-Object System.Text.RegularExpressions.Regex('(?i)(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|client[_-]?secret|private[_-]?key|auth[_-]?token|token)(["'']?\s*[:=]\s*)("[^"]*"|''[^'']*''|[^;\s,]+)')
function Hide-Secret([string]$text) {
    $t = $maskRx.Replace($text, '$1$2***')
    $t = $t -replace '-----BEGIN [A-Z ]*PRIVATE KEY-----.*', '-----BEGIN PRIVATE KEY----- ***'
    if ($t.Length -gt 220) { $t = $t.Substring(0, 220) + '…' }
    return $t.Trim()
}

# ---------------------------------------------------------------- 패턴 검사
$findings = New-Object System.Collections.Generic.List[object]
$testRx = '(?i)((^|[\\/])(tests?|spec|__tests__|testing)[\\/]|Tests?\.(cs|java|py|js|ts)$|\.test\.|\.spec\.)'
foreach ($f in $files) {
    $ext = $f.Extension.TrimStart('.').ToLowerInvariant()
    if ($f.Name -like '.env*') { $ext = 'env' }
    $applicable = @($rules | Where-Object { $_.ExtSet -contains $ext })
    if ($applicable.Count -eq 0) { continue }
    try { $lines = [System.IO.File]::ReadAllLines($f.FullName) } catch { continue }
    $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/')
    $isTest = $rel -match $testRx
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line.Length -gt 4000) { continue }
        foreach ($r in $applicable) {
            try {
                if ($r.Rx.IsMatch($line) -and ($null -eq $r.NeedRx -or $r.NeedRx.IsMatch($line))) {
                    $findings.Add([pscustomobject]@{
                        Rule = $r.Id; Severity = $r.Sev; Category = $r.Cat; Juteong = $r.Ju
                        File = $rel; Line = $i + 1; Test = $(if ($isTest) { 'Y' } else { '' })
                        Code = (Hide-Secret $line); Hint = $r.Hint
                    })
                }
            } catch [System.Text.RegularExpressions.RegexMatchTimeoutException] { }
        }
    }
}

# ---------------------------------------------------------------- 런타임·패키지
$runtime = New-Object System.Collections.Generic.List[string]
$deps = New-Object System.Collections.Generic.List[string]
function Add-Block($list, [string]$title, [string]$body) { $list.Add("### $title`n`n``````text`n$($body.TrimEnd())`n```````n") }
function Test-Cmd([string]$name) { return [bool](Get-Command $name -ErrorAction SilentlyContinue) }
function Find-Files([string]$pattern) {
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Substring($root.Length) -notmatch $excludeRegex }
}

foreach ($g in @(Find-Files 'global.json')) { $runtime.Add("- ``$($g.FullName.Substring($root.Length).TrimStart('\','/'))``: $(([System.IO.File]::ReadAllText($g.FullName) -replace '\s+', ' '))") }
$tfms = @((@(Find-Files '*.*proj') + @(Find-Files '*.props')) | ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern '<TargetFrameworks?(Version)?>([^<]+)<' -AllMatches | ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[2].Value } } } | Sort-Object -Unique)
if ($tfms.Count -gt 0) { $runtime.Add("- .NET 대상 프레임워크: $($tfms -join ', ')") }
foreach ($pj in @(Find-Files 'package.json' | Select-Object -First 5)) {
    $txt = [System.IO.File]::ReadAllText($pj.FullName)
    if ($txt -match '"engines"\s*:\s*\{[^}]*\}') { $runtime.Add("- ``$($pj.FullName.Substring($root.Length).TrimStart('\','/'))`` engines: $($Matches[0] -replace '\s+', ' ')") }
}
foreach ($pom in @(Find-Files 'pom.xml' | Select-Object -First 5)) {
    $v = Select-String -LiteralPath $pom.FullName -Pattern '<(java\.version|maven\.compiler\.(source|release)|spring-boot\.version)>([^<]+)<|<artifactId>spring-boot-starter-parent</artifactId>\s*<version>([^<]+)<' -AllMatches
    if ($v) { $runtime.Add("- ``$($pom.FullName.Substring($root.Length).TrimStart('\','/'))``: $(($v | ForEach-Object { $_.Line.Trim() }) -join ' ')") }
}
if (Test-Cmd 'dotnet') {
    $rt = (& dotnet --list-runtimes 2>&1 | Out-String).Trim()
    if ($rt) { $runtime.Add("- 이 PC의 .NET 런타임 (서버가 아님): $(($rt -split "`r?`n" | ForEach-Object { ($_ -split ' \[')[0] }) -join '; ')") }
}

if (-not $SkipDependencies) {
    # .NET
    if (Test-Cmd 'dotnet') {
        $targets = @(Find-Files '*.sln') + @(Find-Files '*.slnx')
        if ($targets.Count -eq 0) { $targets = @(Find-Files '*.csproj') + @(Find-Files '*.vbproj') }
        foreach ($t in ($targets | Select-Object -First 10)) {
            $rel = $t.FullName.Substring($root.Length).TrimStart('\', '/')
            if (-not $NoRestore) { & dotnet restore $t.FullName -v q 2>&1 | Out-Null }
            $out = (& dotnet list $t.FullName package --vulnerable --include-transitive 2>&1 | Out-String)
            Add-Block $deps "dotnet: $rel" $out
        }
    } elseif (@(Find-Files '*.csproj').Count -gt 0) { $deps.Add('- .NET 프로젝트가 있으나 dotnet SDK가 없어 패키지 검사를 못 함. `packages.config`(.NET Framework)는 NuGet 취약 패키지 검사가 안 되므로 버전을 공식 보안 공지와 대조할 것') }
    # npm
    $locks = @(Find-Files 'package-lock.json')
    if ($locks.Count -gt 0) {
        if (Test-Cmd 'npm') {
            foreach ($l in ($locks | Select-Object -First 10)) {
                Push-Location $l.DirectoryName
                try { $out = (& npm audit --omit=dev 2>&1 | Out-String) } finally { Pop-Location }
                Add-Block $deps "npm: $($l.FullName.Substring($root.Length).TrimStart('\','/'))" $out
            }
        } else { $deps.Add('- package-lock.json이 있으나 npm이 없음') }
    } elseif (@(Find-Files 'package.json').Count -gt 0) { $deps.Add('- package.json은 있으나 package-lock.json이 없음. 버전이 고정되지 않아 감사 불가(잠금 파일 권고)') }
    # Python
    foreach ($req in @(Find-Files 'requirements*.txt' | Select-Object -First 5)) {
        if (Test-Cmd 'pip-audit') { Add-Block $deps "pip-audit: $($req.Name)" (& pip-audit -r $req.FullName 2>&1 | Out-String) }
        else { $deps.Add("- $($req.Name): pip-audit 미설치 (pip install pip-audit 후 다시)") }
    }
    # PHP
    foreach ($cl in @(Find-Files 'composer.lock' | Select-Object -First 5)) {
        if (Test-Cmd 'composer') { Push-Location $cl.DirectoryName; try { Add-Block $deps "composer: $($cl.Name)" (& composer audit 2>&1 | Out-String) } finally { Pop-Location } }
        else { $deps.Add('- composer.lock이 있으나 composer가 없음') }
    }
    # Java
    if (@(Find-Files 'pom.xml').Count -gt 0 -or @(Find-Files 'build.gradle*').Count -gt 0) {
        $deps.Add('- Java 프로젝트: OWASP Dependency-Check로 검사 권고 (`mvn org.owasp:dependency-check-maven:check` 또는 Gradle 플러그인). WEB-INF/lib의 jar도 대상')
    }
    if (@(Find-Files '*.jar').Count -gt 0) { $deps.Add("- 저장소에 jar 파일 $(@(Find-Files '*.jar').Count)개: 버전을 보안 공지와 대조할 것") }
}

# ---------------------------------------------------------------- 출력
$csvPath = Join-Path $outRoot 'scan-findings.csv'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('Rule,Severity,Category,Juteong,File,Line,Test,Code,Hint')
foreach ($x in $findings) {
    $cells = @($x.Rule, $x.Severity, $x.Category, $x.Juteong, $x.File, $x.Line, $x.Test, $x.Code, $x.Hint) | ForEach-Object { '"' + ([string]$_).Replace('"', '""') + '"' }
    [void]$sb.AppendLine(($cells -join ','))
}
[System.IO.File]::WriteAllText($csvPath, $sb.ToString(), $utf8Bom)

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# 소스 점검 결과 (자동, 후보)")
[void]$md.AppendLine()
[void]$md.AppendLine("- 대상: ``$root``")
[void]$md.AppendLine("- 시각: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$md.AppendLine("- 검사한 파일: $(@($files).Count)개, 후보: $($findings.Count)건 (테스트 코드 $(@($findings | Where-Object { $_.Test -eq 'Y' }).Count)건 포함)")
[void]$md.AppendLine("- 후보는 판정이 아니다. 코드를 따라가 사용자 입력이 닿는지 확인해 양호/취약을 정한다. 상세는 ``scan-findings.csv``.")
[void]$md.AppendLine()
[void]$md.AppendLine('## 규칙별 건수')
[void]$md.AppendLine()
[void]$md.AppendLine('| 규칙 | 무게 | KISA 항목 | 주통기반 | 건수 | 확인할 것 |')
[void]$md.AppendLine('| --- | --- | --- | --- | --- | --- |')
foreach ($grp in ($findings | Group-Object Rule | Sort-Object Count -Descending)) {
    $r = $rules | Where-Object { $_.Id -eq $grp.Name } | Select-Object -First 1
    [void]$md.AppendLine("| $($grp.Name) | $($r.Sev) | $($r.Cat) | $($r.Ju) | $($grp.Count) | $($r.Hint) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine('## 무게 높음 후보 (테스트 코드 제외, 최대 50건)')
[void]$md.AppendLine()
foreach ($x in ($findings | Where-Object { $_.Severity -eq '높음' -and $_.Test -ne 'Y' } | Select-Object -First 50)) {
    [void]$md.AppendLine("- ``$($x.File):$($x.Line)`` [$($x.Rule)] $($x.Code.Replace('|', '\|'))")
}
[void]$md.AppendLine()
[void]$md.AppendLine('## 런타임·프레임워크 버전')
[void]$md.AppendLine()
if ($runtime.Count -eq 0) { [void]$md.AppendLine('- 찾지 못함') } else { foreach ($l in $runtime) { [void]$md.AppendLine($l) } }
[void]$md.AppendLine()
[void]$md.AppendLine('버전은 제품의 공식 보안 공지(예: .NET 릴리스 노트, Spring·Tomcat·PHP 보안 공지, 보호나라 보안 공지)와 대조한다. 서버에 설치된 런타임은 서버에서 따로 확인한다.')
[void]$md.AppendLine()
[void]$md.AppendLine('## 취약 패키지 검사')
[void]$md.AppendLine()
if ($SkipDependencies) { [void]$md.AppendLine('- 건너뜀 (-SkipDependencies)') }
elseif ($deps.Count -eq 0) { [void]$md.AppendLine('- 검사할 패키지 파일을 찾지 못함') }
else { foreach ($l in $deps) { [void]$md.AppendLine($l) } }
$mdPath = Join-Path $outRoot 'scan-summary.md'
[System.IO.File]::WriteAllText($mdPath, $md.ToString(), $utf8Bom)

Write-Host "검사 파일 $(@($files).Count)개, 후보 $($findings.Count)건"
$findings | Group-Object Severity | Sort-Object Name | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Name, $_.Count) }
Write-Host "요약: $mdPath"
Write-Host "상세: $csvPath"
