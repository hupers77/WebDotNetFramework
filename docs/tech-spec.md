# 기술 스펙 (버전 표기)

기준 시점: 2026-10-31, 프레임워크 **`fw-v1.2.0`** (제안, PR #66 보안 패치와 PR #67 문서·도구 병합 후 태그). 이 문서는 프레임워크가 쓰는 플랫폼·도구·패키지의 **실제 버전**을 한곳에 적은 것입니다. 업무 저장소(프레임워크에서 시작한 저장소)는 프레임워크를 병합할 때 이 문서를 보고 개발 PC·빌드 서버·운영 서버의 버전을 맞춥니다.

설계 배경과 선택 이유는 [설계서](framework-design.md) 1·2장에 있습니다. 설계서와 이 문서의 버전이 다르면 **이 문서가 현재 상태**입니다.

## 1. 프레임워크 버전

| 항목 | 값 |
| --- | --- |
| 현재 버전 | `fw-v1.2.0` (제안. 태그는 사용자가 붙임) |
| 어셈블리 버전 | `Directory.Build.props`의 `<Version>` = `1.2.0`. 서버 자신(localhost)에서 `/health`를 부르면 이 값이 보입니다 |
| 버전 규칙 | `fw-v주.부.수`. 버그·보안 수정과 문서는 `수`, 기능 추가(업무 코드 수정 불필요)는 `부`, 깨지는 변경(SP 파라미터·결과, 공통 JS API, 설정 키 이름)은 `주` ([04-new-business-repo.md](handover/04-new-business-repo.md) 2장) |
| 변경 기록 | [README 작업 기록](../README.md#작업-기록) |

| 태그 | 커밋 | 날짜 | 내용 |
| --- | --- | --- | --- |
| `fw-v1.0.0` | `2d5af00` | 2026-10-23 | 1단계(프레임워크) 완료 |
| `fw-v1.1.0` | `cd5a01d` | 2026-10-30 | 화면 디자인 개편, 게시판 서식 편집기·본문 사진, 차트 위젯 |
| `fw-v1.2.0` (제안) | PR #67 병합 커밋 | 2026-10-31 | 보안 점검과 취약점 패치, .NET 10.0.14 (PR #66), 반영 안내·점검 skill·기술 스펙 (PR #67) |

## 2. 운영 환경 (서버)

| 구분 | 제품 | 버전 | 비고 |
| --- | --- | --- | --- |
| OS | Windows Server | 2022 | 최신 누적 업데이트 |
| 웹 서버 | IIS + ASP.NET Core Module V2 | IIS 10, In-Process 호스팅 | `src/Portal.Web/web.config` |
| 런타임 | **ASP.NET Core Hosting Bundle** | **10.0.14 이상** | .NET 10 LTS (지원 종료 2028-11). 10.0.14는 CVE-2026-111497, CVE-2026-70197 보안 수정 포함 |
| 배치 서비스 | Portal.Worker (Windows 서비스) | 같은 런타임 | `Microsoft.Extensions.Hosting.WindowsServices` |
| DB | SQL Server | 2022 (호환성 수준 160) | Standard 이상 권장. 데이터 정렬 `Korean_Wansung_CI_AS`, `READ_COMMITTED_SNAPSHOT ON` |
| PDF 변환 | Microsoft Edge | 서버 설치본 최신 | 헤드리스로 HTML→PDF. 경로 기본값 `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` |
| 구성 | 운영계·개발계 각 웹 1대 + DB 1대 | | |

확인 명령 (서버에서):

```powershell
dotnet --list-runtimes          # Microsoft.AspNetCore.App 10.0.14 이상
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.*\Setup' | Select-Object PatchLevel
```

## 3. 개발·빌드 환경

| 구분 | 제품 | 버전 | 어디에 고정 |
| --- | --- | --- | --- |
| SDK | .NET SDK | **10.0.114 이상** (같은 10.0.1xx 대역의 최신 기능 밴드 허용) | `global.json` (`rollForward: latestFeature`) |
| 대상 프레임워크 | `net10.0` | | `Directory.Build.props` |
| 언어 | C# | `latest` (.NET 10 기준 C# 14) | `Directory.Build.props` |
| 코드 규칙 | Nullable, 경고 = 오류, 분석기 `Recommended`, 코드 스타일 빌드 검사 | | `Directory.Build.props`, `.editorconfig` |
| DB 프로젝트 | Microsoft.Build.Sql SDK | 2.3.0 | `db/Portal.Database/Portal.Database.sqlproj` |
| DB 배포 | SqlPackage (dotnet tool) | 170.5.96 | `dotnet-tools.json` (`dotnet tool restore`) |
| 운영 스크립트 | PowerShell | 5.1 이상 (7 권장) | `db/ops/*.ps1` |
| CI (현재) | GitHub Actions | `ubuntu-latest`, `actions/setup-dotnet@v4` (global.json 따름) | `.github/workflows/ci.yml` |
| CI (운영 이전용) | GitLab CI | Windows Runner (PowerShell shell executor) | `.gitlab-ci.yml` |
| 개발 DB | SQL Server 2022 Developer | | `db/ops/local-setup.ps1` |
| 브라우저 (사용자) | Microsoft Edge, Chrome | 최신 2개 버전 | 해상도 1366px 이상 |

## 4. NuGet 패키지

모든 버전은 `Directory.Packages.props`에서 중앙 관리하고(전이 의존성도 고정), 허용 목록 `eng/allowed-packages.txt` 밖의 패키지는 아키텍처 테스트가 막습니다.

| 패키지 | 버전 | 라이선스 | 용도 | 배포물 포함 |
| --- | --- | --- | --- | --- |
| Microsoft.Data.SqlClient | 7.1.1 | MIT | DB 접근 (SP 실행기) | 예 |
| DocumentFormat.OpenXml | 3.5.1 | MIT | 엑셀 올리기·내려받기 | 예 |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | MIT | Portal.Worker Windows 서비스 | 예 |
| System.DirectoryServices.Protocols | 10.0.14 | MIT | LDAP 조회 모듈(선택 사용) | 예 |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT | SqlClient 전이 의존성 보안 패치 고정 | 예 |
| QRCoder | 1.6.0 | MIT | 2단계 인증 QR (의존성 없는 마지막 판) | 예 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT | 웹 통합 테스트 | 아니요 |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | 테스트 | 아니요 |
| xunit.v3 | 3.2.2 | Apache 2.0 | 테스트 | 아니요 |
| xunit.runner.visualstudio | 3.1.5 | Apache 2.0 | 테스트 | 아니요 |
| Microsoft.SqlServer.Dacpacs.Master | 160.2.2 | MIT | DB 프로젝트 master 참조 | 아니요 |

ASP.NET Core MVC, Data Protection, 메모리 캐시, 로깅, 암호화(`System.Security.Cryptography`)는 런타임(공유 프레임워크)에 들어 있어 따로 패키지가 없습니다. 허용 목록의 PDFsharp·MigraDoc은 아직 쓰지 않습니다(필요할 때 추가).

## 5. 화면(프런트엔드)

| 구분 | 내용 |
| --- | --- |
| 렌더링 | 서버 렌더링(Razor) + `fetch` 부분 갱신. SPA 프레임워크 없음 |
| 공통 스크립트 | 자체 Vanilla JS: `fw.js`(호출·메시지), `fw-grid.js`(그리드), `fw-tree.js`(트리), `fw-shell.js`(셸·탭), `fw-file.js`(첨부), `fw-editor.js`(서식 편집기) |
| 스타일 | 자체 CSS: `portal.css`, `report.css` |
| 차트 | 대시보드 차트 위젯은 자체 구현(`js/dash/home.js`) |
| 외부 라이브러리·CDN | **없음**. CSP `script-src 'self'`로 외부 스크립트를 막습니다. 설계서 2장의 Bootstrap·Chart.js는 쓰지 않고 자체 구현으로 대신했습니다 |
| 다국어 | ko / en / zh (간체), `/i18n/{언어}.json` |

## 6. DB 구성

| 구분 | 내용 |
| --- | --- |
| 스키마 | `FW`(프레임워크 표·SP), `LOG`(접속·감사·작업 로그, 월 단위 분할), 업무 저장소는 업무 스키마를 더함(예: `GAT`) |
| 접근 방식 | SP 전용, 앱 계정은 `portal_app` 역할(EXECUTE만) |
| 분할 | `PF_LOG_MONTH` / `PS_LOG_MONTH` (로그 월 분할, 10년 보관) |
| 형상 관리 | SQL 프로젝트 → DACPAC → SqlPackage 배포, 초기 데이터 `db/seed/FW` (`MERGE`, 반복 실행 가능) |

## 7. 보안 기준

- 점검 결과: [2026-10 보안 점검 보고서](security/2026-10-security-audit.md) (KISA 49개 항목, 주통기반 웹 21개 항목, OWASP Top 10)
- 업무 저장소 반영: [보안 점검 반영 방법](security/apply-to-business-repos.md)
- 점검 도구: Claude Code skill [`web-security-audit`](../.claude/skills/web-security-audit/README.md)
- .NET 보안 업데이트는 매월 둘째 화요일(미국 시간, 한국 시간 수요일)에 나옵니다. 보안 릴리스가 나오면 `Directory.Packages.props`의 Microsoft 패키지, `global.json` SDK, 서버 Hosting Bundle을 함께 올리고 이 문서를 고칩니다.

## 8. 이 문서 고치는 방법

1. 버전을 바꾸는 PR(패키지, SDK, 런타임, 서버 요구 사항, 프레임워크 태그)은 **같은 PR에서 이 문서와 [README 작업 기록](../README.md#작업-기록)을 고칩니다**.
2. 프레임워크 태그를 붙이기 전에 `Directory.Build.props`의 `<Version>`을 태그와 같게 맞춥니다(`fw-v1.3.0` → `1.3.0`).
3. 맨 위 "기준 시점"과 1장 태그 표를 고칩니다.
