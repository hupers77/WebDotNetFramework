# web-security-audit: 웹 보안 점검 skill 사용 안내

2026-10 프레임워크 보안 점검([보고서](../../../docs/security/2026-10-security-audit.md))을 다른 PC와 다른 사내 시스템에서도 똑같이 할 수 있게 만든 Claude Code skill과 점검 스크립트입니다.

이 skill로 할 수 있는 일은 다음과 같습니다.

- KISA 소프트웨어 보안약점 49개 항목, 주통기반 웹 21개 항목, 보호나라 자가점검, OWASP Top 10으로 소스와 사이트를 점검합니다.
- 항목마다 양호/취약/권고 판정을 단 보고서를 만듭니다.
- 사용자가 요청하면 취약점 패치와 테스트까지 합니다.

대상은 이 프레임워크로 만든 시스템에 한정되지 않습니다. Java/JSP, PHP, ASP, .NET Framework, Node, Python으로 만든 사내 시스템도 점검할 수 있습니다.

| 파일 | 내용 |
| --- | --- |
| `SKILL.md` | Claude Code가 따르는 점검 절차(안전 규칙, 대상 파악, 자동 점검, 직접 점검, 패치, 보고서) |
| `references/checklist.md` | 점검 항목과 항목별 확인 방법 |
| `references/report-template.md` | 보고서 양식 |
| `scripts/Invoke-SourceScan.ps1` | 소스 점검: 취약 패키지(dotnet·npm·pip·composer)와 위험한 코드 패턴 후보 40여 종 |
| `scripts/Test-WebSecurity.ps1` | 접속 점검: 보안 헤더, 쿠키, HTTPS·HSTS·TLS, TRACE, 노출 경로(.git·.env·백업·swagger 등), 디렉터리 목록, 오류 화면 |

## 1. 이 저장소와 업무 저장소에서 쓰기

`.claude/skills/`는 저장소에 들어 있습니다. 그래서 이 저장소나 이 프레임워크를 병합한 업무 저장소를 받은 PC라면 어디서나 따로 설치하지 않고 씁니다.

```text
# 저장소 폴더에서 Claude Code를 열고
/web-security-audit 이 저장소 전체를 점검하고 취약점은 패치까지 해줘
/web-security-audit 업무 영역(Areas/Gat, db/Portal.Database/GAT)만 점검해서 보고서만 써줘
/web-security-audit 개발계 https://dev-gate.example.local 도 접속 점검에 포함해줘
```

`/web-security-audit`를 쓰지 않고 "KISA 기준으로 웹 보안 점검해줘"라고만 해도 Claude Code가 이 skill을 골라 씁니다.

## 2. 다른 PC, 다른 사내 시스템에서 쓰기

이 저장소가 없는 시스템(예: 기존 Java 시스템)을 점검하려면 skill 폴더를 그 PC의 **개인 skill 폴더**에 복사합니다. 복사해 두면 그 PC의 모든 프로젝트에서 쓸 수 있습니다.

```powershell
# Windows (PowerShell). 이 저장소를 받은 위치에서
$dest = Join-Path $env:USERPROFILE '.claude\skills\web-security-audit'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Recurse -Force .\.claude\skills\web-security-audit\* $dest
```

```bash
# macOS / Linux
mkdir -p ~/.claude/skills/web-security-audit
cp -R .claude/skills/web-security-audit/. ~/.claude/skills/web-security-audit/
```

그다음 점검할 시스템의 소스 폴더에서 Claude Code를 열고 같은 방법으로 요청합니다.

```text
/web-security-audit 이 시스템(Java 8, Tomcat 9, Oracle)을 점검해서 docs/security에 보고서를 써줘. 패치는 하지 말고 목록만
```

- 소스를 고칠 수 없는 시스템(외주·패키지 제품)은 "보고서만"이라고 말합니다. 접속 점검과 소스 검토 결과로 보고서를 쓰고, 고칠 일은 권고로 남깁니다.
- 소스가 없고 주소만 있는 시스템은 접속 점검(`Test-WebSecurity.ps1`)과 서버 자가점검 항목만 할 수 있습니다. 나머지는 "확인 못 함"으로 적습니다.
- claude.ai 계정 skill로도 저장해 두면, 같은 계정의 Claude 앱에서도 같은 절차를 쓸 수 있습니다. 이때는 스크립트 없이 `SKILL.md`의 절차와 `references/checklist.md` 5장의 명령으로 점검합니다.

## 3. Claude 없이 스크립트만 쓰기

두 스크립트는 혼자서도 돕니다. 필요한 것은 Windows 기본 PowerShell 5.1 또는 PowerShell 7입니다. 소스 점검에서 패키지까지 보려면 그 언어의 도구(dotnet, npm 등)가 PC에 있어야 합니다.

```powershell
# 실행 정책 때문에 막히면 -ExecutionPolicy Bypass 를 붙여 그 실행에만 허용
powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SourceScan.ps1 -Path C:\src\legacy-system -OutDir C:\temp\scan
powershell -ExecutionPolicy Bypass -File .\scripts\Test-WebSecurity.ps1 -Url https://dev-legacy.example.local -Paths /login.do -OutFile C:\temp\scan\web.md
```

| 스크립트 | 결과 | 읽는 법 |
| --- | --- | --- |
| `Invoke-SourceScan.ps1` | `scan-summary.md`, `scan-findings.csv`(엑셀에서 열림) | **후보 목록**입니다. 줄마다 코드를 열어 사용자 입력이 닿는지 확인해 판정합니다. 비밀값은 `***`로 가려져 있습니다 |
| `Test-WebSecurity.ps1` | Markdown 표 (주통기반 번호, 결과, 내용) | 취약 → 권고 → 확인 → 양호 순입니다. localhost로 점검하면 HSTS·버전 노출 결과가 실제와 다를 수 있어, 다른 PC에서 실제 호스트 이름으로 다시 봅니다 |

주요 옵션:

- `Invoke-SourceScan.ps1 -SkipDependencies`: 인터넷이 막힌 PC에서 패키지 검사를 건너뜁니다.
- `Invoke-SourceScan.ps1 -Exclude legacy,samples`: 그 경로를 빼고 봅니다.
- `Test-WebSecurity.ps1 -SkipCertificateCheck`: 사내 인증서를 쓰는 개발 서버에서 씁니다.
- `Test-WebSecurity.ps1 -Paths /login`: 로그인 화면까지 보면 세션·CSRF 쿠키 속성도 확인합니다.

## 4. 지켜야 할 것

1. **허락받은 시스템만** 점검합니다. 운영 서버는 운영 담당 동의를 받고 업무 시간을 피합니다.
2. 접속 점검은 GET·HEAD·OPTIONS·TRACE만 60건 안팎 보냅니다. 로그인 시도, 공격 문자열, 부하는 보내지 않습니다. SQL 삽입·권한 우회 같은 동적 진단은 개발계에서 테스트 코드로 하거나 전문기관 모의해킹으로 합니다.
3. 결과 파일에는 서버 구성 정보가 들어 있으므로 저장소에 올리기 전에 내용을 확인합니다. 접속 점검 원본(`web.md`)과 후보 목록(`scan-findings.csv`)은 저장소에 올리지 않고, 정리한 보고서만 올립니다.

## 5. 고치는 방법

- 점검 항목, 패턴, 노출 경로를 바꾸면 이 폴더의 파일을 고치고 프레임워크 저장소에 PR을 올립니다. 업무 저장소는 다음 프레임워크 병합 때 받습니다.
- 스크립트를 바꾸면 PowerShell 7과 5.1에서 각각 한 번씩 돌려 봅니다. 소스 점검은 일부러 약하게 만든 예제 폴더로, 접속 점검은 로컬 포털과 디렉터리 목록을 켠 시험 사이트로 확인합니다.
