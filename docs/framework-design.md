# 엔터프라이즈 웹 포털 프레임워크 설계서

Oct 21, 2026 · @사랑나무아빠

## 1. 개요

이 프레임워크는 Windows Server 2022(IIS) + SQL Server 2022 위에서 .NET 10 LTS 기반 **모듈형 모놀리스**로 구축하며, 인증·권한·메뉴·코드·다국어·설정을 모두 DB에서 관리해 화면 추가 시 코드 수정 없이 운영자가 구성할 수 있게 합니다. 이후 개별 업무 시스템(인사, 구매, 설비 등)을 이 위에 올려 재사용하는 것이 목표입니다.

**설계 원칙**

1. **설정 우선(No Hard-coding)**: 메뉴, 버튼 권한, 코드, 다국어 문구, 정책값(비밀번호 규칙, 세션 시간, 파일 크기 등)은 모두 DB 테이블과 관리 화면으로 변경합니다. 코드에는 "키"만 남깁니다.
2. **DB 접근은 SP 전용**: 애플리케이션 DB 계정은 테이블 권한 없이 `EXECUTE` 권한만 가집니다. 인라인 SQL과 ORM 쿼리 생성을 금지합니다.
3. **보안 기본값(Secure by Default)**: KISA 전자정부 SW 개발보안 가이드, 주요정보통신기반시설 웹 취약점 점검 항목, OWASP Top 10을 프레임워크 공통 계층에서 처리해, 업무 개발자가 놓쳐도 기본 방어가 되도록 합니다.
4. **무료 라이선스, OSS 최소화**: .NET/Microsoft 제공 구성요소를 우선 사용하고, 외부 OSS는 MIT/Apache 2.0 등 상용 무료 라이선스 중 꼭 필요한 것만 허용 목록으로 관리합니다.
5. **빠른 화면**: 서버 렌더링(Razor) + 부분 갱신(fetch), 권한·메뉴·코드의 메모리 캐시, 서버 페이징을 기본으로 합니다.
6. **표준화**: 화면 유형별 표준 템플릿, SP 템플릿, 명명 규칙을 제공해 개발자 간 편차를 줄입니다.

**시스템 환경**

| 구분 | 사양 |
| --- | --- |
| 웹/WAS | 운영계·개발계 각 1대, Windows Server 2022, IIS 10 + ASP.NET Core Module(In-Process) |
| 런타임 | .NET 10 LTS (2028년 11월까지 지원) |
| DB | 운영계·개발계 각 1대, SQL Server 2022 (Standard 이상 권장, READ\_COMMITTED\_SNAPSHOT ON) |
| 인증 연계 | Active Directory (Kerberos/NTLM, LDAPS) |
| 클라이언트 | Edge/Chrome 최신 2개 버전, 해상도 1366px 이상, 모바일 반응형(조회 위주) |

**사용자와 언어**: 본사 임직원(AD SSO)과 다수 협력사 사용자(ID/PW)가 함께 쓰며, 한국어·영어·중국어(간체)를 지원합니다. 포털은 개인정보를 저장하지 않는 것을 원칙으로 하고, 접속기록은 10년 보관합니다.

**범위**: 공통 프레임워크(인증, 권한, 메뉴, 기준정보, 공통코드, 다국어, 설정, 로그), 공통 업무 모듈(게시판, 파일, PDF, 레포트, 대시보드, 외부 API, 알림, 스케줄러), 표준 화면 예제. 개별 업무 기능은 범위 밖입니다.

## 2. 기술 스택과 라이선스 정책

핵심 기능은 모두 .NET 기본 라이브러리와 Microsoft 무료 구성요소로 구현하고, 외부 OSS는 아래 허용 목록 4개 이내로 제한합니다. ORM(EF Core)은 SP 전용 원칙과 맞지 않아 쓰지 않고, ADO.NET 위에 자체 SP 실행기를 둡니다.

| 영역 | 선택 | 라이선스 | 비고 |
| --- | --- | --- | --- |
| 웹 프레임워크 | ASP.NET Core MVC + Razor Pages, Web API | MIT (Microsoft) | 서버 렌더링 중심, SPA 프레임워크 미사용 |
| DB 접근 | Microsoft.Data.SqlClient + 자체 SP 실행기 | MIT (Microsoft) | Dapper 수준의 경량 매퍼를 직접 구현 |
| AD SSO | Microsoft.AspNetCore.Authentication.Negotiate, System.DirectoryServices.Protocols | MIT (Microsoft) | Kerberos SSO + LDAPS 사용자 동기화 |
| 암호화 | System.Security.Cryptography, ASP.NET Core Data Protection | MIT (Microsoft) | PBKDF2, AES-256-GCM |
| 캐시 | IMemoryCache, HybridCache, SQL Server 분산 캐시 | MIT (Microsoft) | 웹팜 확장 시 Redis 없이 동작 |
| 로깅 | Microsoft.Extensions.Logging + 자체 DB/파일 Provider | MIT (Microsoft) | Serilog/NLog 미사용 |
| 엑셀 | DocumentFormat.OpenXml (Open XML SDK) | MIT (Microsoft) | 업/다운로드, 대용량 스트리밍 |
| PDF 생성 | PDFsharp / MigraDoc 6.x | MIT (OSS 허용) | 코드 기반 문서, 병합, 워터마크 |
| HTML→PDF | Microsoft Edge Headless (`--print-to-pdf`) | 무료 (런타임) | 레포트 HTML을 그대로 PDF화, 서버에 Edge 설치 필요 |
| UI 기반 | Bootstrap 5.3 | MIT (OSS 허용) | 레이아웃/폼 스타일만, jQuery 미사용 |
| 차트 | Chart.js 4.x | MIT (OSS 허용) | 대시보드 위젯 |
| QR 코드 | QRCoder 1.6 | MIT (OSS 허용) | 2단계 인증(TOTP) 앱 등록, 의존성 없음 |
| 그리드/트리/탭 | 자체 Vanilla JS 컴포넌트 | 자체 | 가상 스크롤 그리드, 메뉴 트리, MDI 탭 |
| DB 프로젝트 | SQL Server Data Tools(.sqlproj) | 무료 (Microsoft) | 테이블/SP 형상 관리, DACPAC 배포 |

**OSS 관리 규칙**: 허용 목록 밖의 패키지는 아키텍트 승인 후 추가하고, 라이선스(GPL/AGPL 및 매출 기준 유료화 라이선스 금지), 최신 보안 패치 여부를 확인합니다. QuestPDF, iText, AG Grid Enterprise 등은 상용 조건이 있어 제외합니다. 빌드 시 `dotnet list package --vulnerable`로 취약 패키지를 검사합니다.

## 3. 전체 아키텍처

하나의 웹 애플리케이션(Portal.Web) 안에 모든 모듈을 두되, 모듈 간에는 인터페이스로만 통신하는 4계층 구조입니다. 배치성 작업은 별도 Windows 서비스(Portal.Worker)로 분리해 웹 응답 속도에 영향을 주지 않게 합니다.

&#91;embedded content: 시스템 구성도 · 웹 호스트 4계층과 외부 자원\]

모든 요청은 공통 파이프라인(강조)을 거치고, DB에는 SpExecutor를 통한 SP 호출로만 닿습니다. Portal.Worker는 같은 DB·AD·메일 서버를 사용하는 별도 프로세스입니다.

**계층별 책임**

| 계층 | 구성 | 책임 |
| --- | --- | --- |
| Presentation | MVC Controller, Razor View, Tag Helper, JS 컴포넌트 | 화면 렌더링, 입력 검증, 버튼 권한 표시 |
| Cross-cutting | 미들웨어/필터 파이프라인 | 인증, 권한 검사, CSRF, 보안 헤더, 다국어, 접근 로그, 예외 처리 |
| Application | 모듈별 Service | 업무 규칙, 트랜잭션 경계, 캐시 무효화 |
| Data | SpExecutor, Repository | SP 호출, 파라미터/TVP 매핑, 결과 매핑 |
| Database | SQL Server SP, 테이블 | 데이터 처리, 데이터 범위(부서/본인) 필터 |

**요청 처리 순서** (모든 요청에 공통 적용)

1. IIS가 HTTPS 종료, 요청 크기/허용 메서드 제한 (Request Filtering)
2. 보안 헤더 미들웨어: CSP, HSTS, X-Content-Type-Options, Referrer-Policy, frame-ancestors
3. 인증: 쿠키 인증(ID/PW) 또는 Negotiate(AD SSO) → 사용자 세션 확인, 중복 로그인 검사
4. 다국어: 사용자 언어 설정 → 쿠키 → 브라우저 순으로 Culture 결정
5. 권한: 요청 URL → 프로그램(메뉴) + 기능코드 매핑 조회(캐시) → 사용자 권한 비교, 없으면 403
6. CSRF 토큰 검증(POST/PUT/DELETE)
7. Controller → Service → SpExecutor → SP 실행 (사용자 ID, 언어, IP는 공통 파라미터로 자동 주입)
8. 응답 후 접근 로그를 비동기 큐로 적재(응답 지연 없음)

**배포 구성**: 운영계와 개발계를 각각 웹서버 1대 + DB 1대로 둡니다. 개발계에서 단위·통합 테스트를 통과한 빌드만 운영계에 배포하고, DB 변경은 SSDT DACPAC 비교 스크립트를 개발계 → 운영계 순서로 적용합니다. 환경별 차이(연결 문자열, 개발계 메일 실발송 차단, 로그 레벨)는 `appsettings.Development.json` / `appsettings.Production.json`에만 둡니다.

서버가 1대이므로 캐시는 서버 메모리만 쓰고, 세션은 쿠키 + DB 세션 테이블(`TB_USER_SESSION`)로 관리해 앱 풀 재시작에도 로그인이 유지됩니다. Data Protection 키는 서버 로컬 폴더에 DPAPI로 보호해 저장하고 백업 대상에 포함합니다. 나중에 웹서버를 늘리더라도 `TB_CACHE_VERSION` 폴링과 DB 세션 구조를 그대로 쓸 수 있게 설계합니다. 개발계에 운영 데이터를 복사할 때는 마스킹 스크립트를 함께 실행합니다. 외부 API는 같은 호스트의 `/api/v1` 경로로 제공합니다.

## 4. 솔루션·모듈 구조

솔루션은 프레임워크 프로젝트 5개, 업무 모듈 프로젝트, 실행 호스트 2개(Web, Worker)로 나뉘며, 업무 모듈은 프레임워크를 참조만 하고 반대 방향 참조는 금지합니다. 새 업무 시스템은 `Portal.Modules.<업무명>` 프로젝트를 추가하고 MVC Area로 등록하면 됩니다.

```
Portal.sln
├─ src/
│  ├─ Portal.Core            공통 계약: Result/Paging DTO, 예외, 상수 키, 인터페이스
│  ├─ Portal.Data            SpExecutor, TVP 빌더, 결과 매퍼, 트랜잭션 스코프
│  ├─ Portal.Security        인증(ID/PW, AD), 권한 평가기, 암호화, XSS 필터, 마스킹
│  ├─ Portal.Infrastructure  캐시, 로깅, 파일 저장소, 메일, PDF, 엑셀, HTTP 클라이언트, 스케줄러
│  ├─ Portal.Framework.Web   기본 Controller, 필터, Tag Helper, 미들웨어, 공통 View/JS
│  ├─ Portal.Modules.System  사용자/조직/권한/메뉴/코드/다국어/설정/로그 관리 (Area: Sys)
│  ├─ Portal.Modules.Board   게시판 (Area: Board)
│  ├─ Portal.Modules.Report  레포트, PDF 출력 (Area: Rpt)
│  ├─ Portal.Modules.Dashboard 대시보드, 위젯 (Area: Dash)
│  ├─ Portal.Modules.Sample  표준 화면 예제 (Area: Sample)
│  ├─ Portal.Web             호스트: Program.cs, 레이아웃, 로그인, 외부 API(/api/v1)
│  └─ Portal.Worker          Windows 서비스: 스케줄러, 메일 발송, AD 동기화, 로그 정리
├─ db/
│  └─ Portal.Database        SSDT: 스키마별 테이블, SP, TVP 타입, 초기 데이터 스크립트
└─ tests/
   ├─ Portal.Testing         테스트 공용 Fake, 테스트 인증 핸들러, 테스트 DB 초기화
   ├─ Portal.UnitTests       권한·암호화·Service 단위 테스트 + 아키텍처 테스트
   └─ Portal.IntegrationTests (테스트 DB 대상 SP 검증)
```

**모듈 등록 방식**: 각 모듈은 `IPortalModule`(서비스 등록, 라우트, 메뉴 시드, 다국어 키 목록)을 구현하고, 호스트는 `appsettings.json`의 모듈 목록을 읽어 활성화합니다. 모듈을 끄면 해당 메뉴와 라우트가 함께 빠집니다.

**모듈 간 규칙**

- 다른 모듈의 SP를 직접 호출하지 않고, 해당 모듈이 공개한 인터페이스(예: `IUserLookup`, `ICodeProvider`)를 사용합니다.
- 모든 Controller는 `PortalController`를 상속해 현재 사용자, 언어, 권한 정보를 공통으로 얻습니다.
- 응답은 `ApiResult<T>`(성공 여부, 데이터, 메시지 키, 추적 ID) 하나로 통일합니다.

## 5. 인증 설계 (ID/PW, AD SSO)

로그인은 두 가지 방식을 동시에 지원하고, 어느 방식으로 들어와도 같은 `TB_USER` 계정과 같은 권한 체계를 씁니다. 사용할 방식은 설정(`AUTH.MODE` = `FORM` | `AD` | `BOTH`)으로 고릅니다.

**ID/PW 로그인**

1. 로그인 화면 → POST (HTTPS, CSRF 토큰, 로그인 요청 Rate Limit: IP당 분당 10회)
2. `USP_AUTH_GET_USER`로 계정, 잠금 여부, 해시 조회 → PBKDF2-SHA512(210,000회, 16바이트 salt) 고정 시간 비교
3. 실패 시 실패 횟수 증가, 설정값(기본 5회) 초과 시 계정 잠금. 오류 메시지는 "ID 또는 비밀번호가 올바르지 않습니다"로 동일하게 표시
4. 성공 시 세션 ID 재발급(세션 고정 방지), 중복 로그인 정책 적용, 로그인 이력 기록
5. 최초 로그인 또는 비밀번호 만료(기본 90일) 시 변경 화면으로 강제 이동
6. 2단계 인증: TOTP(RFC 6238, 자체 구현, 인증 앱 사용). 협력사(`AUTH.PARTNER_MFA`)와 관리자 역할(SYS_ADMIN·PORTAL_ADMIN·SECURITY_AUDITOR, `AUTH.ADMIN_MFA`)에 기본 적용합니다. 비밀번호가 맞으면 세션 대신 5분짜리 암호화 대기표 쿠키를 주고, 코드를 확인하면 세션을 만듭니다. 첫 로그인에서 QR로 인증 앱을 등록하며(비밀키는 Data Protection으로 암호화 저장), 한 번 쓴 코드는 다시 받지 않고, 틀린 코드도 로그인 실패 횟수에 넣어 잠급니다. 이메일 OTP는 메일 모듈 이후 검토합니다

**AD SSO 로그인**

1. 도메인 PC에서 접속 시 Negotiate(Kerberos, 실패 시 NTLM) 자동 인증. 사내 사이트를 브라우저 로컬 인트라넷 영역에 GPO로 등록
2. AD 계정(`objectSid`, `sAMAccountName`)을 `TB_USER_LOGIN_MAP`으로 포털 사용자와 연결. 매핑이 없으면 설정에 따라 자동 생성(`AUTH.AD_AUTO_PROVISION`) 또는 거부
3. 웹 세션은 ID/PW와 같은 쿠키 세션으로 전환 (매 요청 Kerberos 현상 방지)
4. 도메인 밖(모바일, 외부)에서는 AD 계정 ID/PW를 LDAPS(636) Bind로 검증하는 폼 로그인 제공
5. Portal.Worker가 야간에 LDAPS로 조직·사용자를 동기화 (퇴사자 = AD 비활성 → 포털 계정 잠금). AD 그룹 → 포털 역할 매핑(`TB_AD_GROUP_ROLE`) 지원
6. 협력사 사용자는 AD 계정이 없으므로 **협력사 전용 로그인 화면**(ID/PW + 2단계 인증, 가입 신청 링크)을 본사 SSO 진입과 따로 둡니다. 본사 사용자는 기본 주소에서 SSO로 바로 들어오고, 협력사 사용자는 협력사 로그인 주소로 들어옵니다(3단계 AD SSO 적용 때 만듦, 2026-10-22 요구사항)

향후 ADFS 또는 Microsoft Entra ID를 도입하면 OpenID Connect 핸들러(Microsoft 제공)를 추가하는 것만으로 확장되도록 인증 제공자를 인터페이스(`IAuthProvider`)로 분리합니다.

**본사·협력사 사용자**

- 사용자 유형 `USER_TYPE_CD`: `EMP`(본사 임직원, AD SSO), `PARTNER`(협력사, ID/PW), `SYSTEM`(관리·연동 계정).
- 협력사 사용자는 AD 계정이 없으므로 ID/PW에 2단계 인증(이메일 OTP 또는 TOTP)을 기본 적용합니다(`AUTH.PARTNER_MFA`).
- 계정 발급: 협력사 담당자 가입 신청 → 본사 담당 부서 승인 → 계정 활성화. 협력사 관리자에게 자기 회사 사용자 관리를 위임할 수 있습니다. 위임 화면(`PTN_USER_MGT`)은 본사 사용자 관리와 같은 SP를 쓰고, SP가 프로그램별 데이터 범위로 자기 회사 사용자만 다루게 하며 협력사 역할만 줄 수 있게 합니다.
- 계정 유효기간(계약 종료일)이 지나면 자동 잠금하고, 협력사별 접속 허용 IP 대역을 설정할 수 있습니다. 둘 다 시스템 관리 > 회사 관리(`SYS_COMPANY_MGT`)에서 정하며, 계약 종료일을 바꾸면 그 날짜를 사용 기한으로 쓰던 협력사 계정의 기한도 함께 바뀝니다.

**세션·토큰 정책** (모두 `TB_CONFIG`에서 변경)

| 항목 | 기본값 | 설정 키 |
| --- | --- | --- |
| 세션 비활동 만료 | 30분 (만료 5분 전 연장 안내) | `SESSION.IDLE_MINUTES` |
| 중복 로그인 | 후입자 우선(기존 세션 종료) | `SESSION.CONCURRENT_POLICY` |
| 인증 쿠키 | `__Host-` 접두어, Secure, HttpOnly, SameSite=Lax | (코드 고정 보안값) |
| 로그인 실패 잠금 | 5회, 관리자 해제 또는 30분 후 자동 해제 | `PWD.LOCK_COUNT`, `PWD.LOCK_MINUTES` |
| 비밀번호 규칙 | 3종류 조합 8자 또는 2종류 10자 이상, 최근 3개 재사용 금지 | `PWD.*` |
| 휴면 계정 | 1년 미접속 시 휴면 전환 | `USER.DORMANT_DAYS` |
| 외부 API 토큰 | Client Credentials, Access Token 30분 (서명 JWT) | `API.TOKEN_MINUTES` |

## 6. 권한·메뉴 모델

권한은 **역할(Role) × 프로그램(메뉴) × 기능(버튼)** 3축으로 부여하고, 사용자는 직접 부여받은 역할과 부서·AD 그룹을 통해 받은 역할의 합집합을 갖습니다. 메뉴에 보이는 것과 서버가 허용하는 것은 같은 권한 데이터로 판단하므로, 버튼을 숨기는 것과 서버 차단이 항상 일치합니다.

**권한 평가 규칙**

1. 유효 역할 = 사용자 역할 ∪ 부서 역할(하위 부서 상속 옵션) ∪ AD 그룹 역할 (유효기간 내만)
2. 기능 권한 = 유효 역할들의 `TB_ROLE_PROGRAM_FUNC` 허용 합집합
3. 사용자 예외 권한(`TB_USER_PROGRAM_FUNC`)을 마지막에 적용: `GRANT`는 추가, `DENY`는 역할 허용보다 우선
4. 메뉴 노출 = 해당 프로그램에 `READ` 권한이 있거나, 폴더인 경우 하위에 노출 메뉴가 하나 이상 있을 때
5. 데이터 범위 = 역할별 `DATA_SCOPE`(전체 / 소속 회사 / 소속 부서+하위 / 소속 부서 / 본인) 중 가장 넓은 값. SP가 `@P_USER_ID`로 판단
6. `SYS_ADMIN` 역할은 모든 기능 허용(단, 감사 로그는 삭제 불가)

**역할 부여 규칙**: 역할마다 줄 수 있는 회사 유형(`TB_ROLE.COMPANY_TYPE_CD`)을 두어 협력사 사용자에게 본사 역할을, 본사 사용자에게 협력사 역할을 줄 수 없게 합니다(저장 SP가 검사). 관리자는 자기 계정의 상태·역할을 바꿀 수 없고, `SYS_ADMIN` 역할을 주고 빼는 일과 `SYS_ADMIN` 계정을 고치는 일은 `SYS_ADMIN`만 합니다. 역할이나 상태가 바뀌면 권한 캐시(`AUTH`)를 갱신합니다.

**회사 간 공개와 격리**: 메뉴는 하나의 트리를 모두가 공유하고, 메뉴별 공개 대상(`OPEN_SCOPE_CD` = `ALL` 모든 회사 / `EMP` 본사만 / `PARTNER` 협력사만)을 정합니다. 게시판·각종 조회 화면처럼 어느 회사 사용자든 보는 화면은 `ALL`로 두고, 역할로 기능 권한만 나눕니다. 공개 대상은 메뉴 표시만 정하고, 화면·API 접근은 공개 대상과 관계없이 세션과 권한(역할 × 프로그램 × 기능)으로만 검사합니다. 따라서 협력사 역할에는 본사 전용(`EMP`) 프로그램 권한을 주지 않고, 함께 보는(`ALL`) 화면에는 조회 권한을 줍니다. 초기 데이터는 권한 매트릭스 테스트가 이 규칙을 확인합니다. 데이터는 두 종류로 구분합니다.

- **공유 데이터**(공지, 자료실, 기준정보 조회 등): 게시판·프로그램 단위로 공개 대상을 설정(전체 / 본사 / 지정 협력사)하고, 글 단위로 지정 협력사에만 공개할 수도 있습니다.
- **회사 소유 데이터**(협력사가 등록한 업무 데이터, 협력사 사용자 목록 등): 협력사 사용자는 역할 설정과 관계없이 최대 "소속 회사" 범위까지만 봅니다. 모든 SP가 `@P_USER_ID`에서 회사 코드를 구해 강제하며, 통합 테스트에서 타사 데이터 조회 차단을 검증합니다.

**기능(버튼) 코드**: 기본 코드는 공통코드 `FUNC_CD`로 관리하고, 프로그램별로 사용할 기능만 `TB_PROGRAM_FUNC`에 등록합니다. 업무별 사용자 정의 기능(예: `CONFIRM`, `CLOSE_MONTH`)도 코드만 추가하면 됩니다.

| 코드 | 의미 | 표시 버튼 예 |
| --- | --- | --- |
| `READ` | 조회(화면 접근) | 조회, 초기화 |
| `CREATE` | 등록 | 신규, 행추가 |
| `UPDATE` | 수정 | 저장 |
| `DELETE` | 삭제 | 삭제, 행삭제 |
| `EXPORT` | 내보내기 | 엑셀, PDF |
| `PRINT` | 출력 | 인쇄, 레포트 |
| `UPLOAD` | 일괄 업로드 | 엑셀 업로드 |
| `APPROVE` | 승인/확정 | 승인, 반려 |
| `PII` | 개인정보 원문 조회 | 마스킹 해제 |

**적용 방법 (개발자 관점)**

- 서버: Action에 `[Func("UPDATE")]` 특성만 붙이면, 필터가 현재 요청의 프로그램 ID(라우트 → `TB_PROGRAM` 매핑)와 기능코드로 권한을 검사합니다. 특성이 없는 Action은 기본 `READ`로 간주합니다(기본 거부).
- 화면: `<fw-button func="DELETE" text-key="BTN.DELETE" />` Tag Helper가 권한이 없으면 버튼을 렌더링하지 않습니다. JS에는 `fw.auth.can('DELETE')`를 제공합니다.
- 권한은 로그인 시 계산해 사용자별로 캐시하고, 권한 변경 시 `TB_CACHE_VERSION`을 올려 즉시 반영합니다.

**메뉴 트리**: 메뉴는 무제한 단계 트리(`PARENT_MENU_ID`)로 저장하고, 화면에는 권장 4단계까지 표시합니다. 메뉴 유형은 `FOLDER`(묶음), `PAGE`(프로그램 연결), `LINK`(외부 URL, 새 창 옵션), `POPUP` 4가지입니다. 하나의 프로그램을 여러 메뉴에 연결할 수 있고(예: 공지사항 게시판을 두 메뉴에 노출), 권한은 메뉴가 아닌 프로그램에 걸리므로 메뉴 재배치가 권한에 영향을 주지 않습니다. 메뉴 세트(`MENU_SET`)를 두어 포털/관리자 콘솔/모바일 메뉴를 따로 구성할 수 있습니다.

## 7. DB 설계

프레임워크 테이블은 `FW` 스키마, 로그는 `LOG` 스키마, 업무 모듈은 모듈별 스키마(`BRD`, `RPT`, `DSH` 등)에 두고, 모든 테이블은 공통 감사 컬럼 5개를 갖습니다. 다국어 명칭은 본 테이블에 두지 않고 `_LANG` 테이블로 분리합니다.

**명명 규칙**

| 대상 | 규칙 | 예 |
| --- | --- | --- |
| 테이블 | `스키마.TB_대상[_세부]` | `FW.TB_USER`, `FW.TB_CODE_LANG` |
| 컬럼 | 대문자 스네이크, 접미어로 성격 표시 (`_ID`, `_CD`, `_NM`, `_YN`, `_DT`, `_CNT`) | `USE_YN`, `LAST_LOGIN_DT` |
| SP | `스키마.USP_대상_동작` | `FW.USP_MENU_GET_TREE` |
| TVP 타입 | `스키마.TVP_대상` | `FW.TVP_CODE` |
| 인덱스 | `IX_테이블_컨럼`, 고유는 `UX_` | `IX_TB_USER_DEPT_ID` |
| 공통 컬럼 | `REG_ID`, `REG_DT`, `UPD_ID`, `UPD_DT`, `ROW_VER rowversion` | 동시성 제어(낙관적 잠금)에 `ROW_VER` 사용 |

**테이블 목록**

| 영역 | 테이블 | 용도 |
| --- | --- | --- |
| 조직·사용자 | `TB_COMPANY`, `TB_DEPT`, `TB_USER`, `TB_USER_PWD_HIST`, `TB_USER_LOGIN_MAP`, `TB_USER_AD_GROUP`, `TB_USER_PREF`, `TB_USER_SESSION` | 회사(본사/협력사 구분, 협력사별 허용 IP), 부서 트리, 계정, 비밀번호 이력, AD 매핑, AD 그룹 소속(동기화), 개인 설정, 활성 세션 |
| 권한 | `TB_ROLE`, `TB_USER_ROLE`, `TB_DEPT_ROLE`, `TB_AD_GROUP_ROLE`, `TB_ROLE_PROGRAM_FUNC`, `TB_USER_PROGRAM_FUNC`, `TB_ROLE_DELEGATE` | 역할, 부여, 기능 권한, 개인 예외, 권한 위임 |
| 프로그램·메뉴 | `TB_PROGRAM`, `TB_PROGRAM_FUNC`, `TB_PROGRAM_API`, `TB_MENU`, `TB_MENU_LANG`, `TB_USER_FAVORITE` | 화면/URL, 사용 기능, API 경로 매핑, 메뉴 트리, 즐겨찾기 |
| 공통코드 | `TB_CODE_GROUP`, `TB_CODE_GROUP_LANG`, `TB_CODE`, `TB_CODE_LANG` | 계층형 코드, 추가 속성 5개, 유효기간 |
| 다국어 | `TB_LANG`, `TB_LANG_RESOURCE` | 언어 목록, 화면/메시지 문구 |
| 설정 | `TB_CONFIG`, `TB_CONFIG_HIST`, `TB_CACHE_VERSION` | 시스템 설정값, 변경 이력, 캐시 버전 |
| 기준정보 | `TB_HOLIDAY`, `TB_POSITION`, `TB_DUTY`, `TB_EXCHANGE_RATE` | 휴일/영업일, 직급, 직책, 환율 (업무별 확장) |
| 파일 | `TB_FILE_GROUP`, `TB_FILE` | 첨부 묶음, 파일 메타(저장명은 GUID) |
| 알림·작업 | `TB_MSG_TEMPLATE`, `TB_MSG_QUEUE`, `TB_NOTIFICATION`, `TB_JOB`, `TB_JOB_HIST` | 메일/알림 템플릿·큐, 스케줄 작업 |
| 외부 연계 | `TB_API_CLIENT`, `TB_API_OUTBOUND` | 수신 API 클라이언트(비밀값 해시), 송신 대상 설정 |
| 로그 (`LOG`) | `TB_LOGIN_LOG`, `TB_ACCESS_LOG`, `TB_AUDIT_LOG`, `TB_PII_ACCESS_LOG`, `TB_ERROR_LOG`, `TB_API_LOG`, `TB_LOG_CHAIN` | 로그인, 화면 접근, 데이터 변경, 개인정보 접속기록, 오류, API, 테이블별 해시 체인 마지막 값 |

**핵심 테이블 DDL** (공통 컬럼 `REG_ID, REG_DT, UPD_ID, UPD_DT, ROW_VER`는 생략)

```sql
-- 사용자
CREATE TABLE FW.TB_USER (
  USER_ID        VARCHAR(30)    NOT NULL PRIMARY KEY,
  COMPANY_CD     VARCHAR(10)    NOT NULL,
  DEPT_ID        VARCHAR(20)    NULL,
  USER_NM        NVARCHAR(100)  NOT NULL,
  EMAIL_ENC      VARBINARY(512) NULL,        -- 업무용 이메일만, AES-256 암호화
  USER_TYPE_CD   VARCHAR(10)    NOT NULL DEFAULT 'EMP', -- EMP/PARTNER/SYSTEM
  PWD_HASH       VARBINARY(64)  NULL,        -- AD 전용 계정은 NULL
  PWD_SALT       VARBINARY(16)  NULL,
  PWD_CHG_DT     DATETIME2(0)   NULL,
  LOGIN_FAIL_CNT TINYINT        NOT NULL DEFAULT 0,
  LOCK_YN        CHAR(1)        NOT NULL DEFAULT 'N',
  LOCK_DT        DATETIME2(0)   NULL,
  ACCOUNT_END_DT DATE           NULL,        -- 협력사 계약 종료일(지나면 자동 잠금)
  USER_STAT_CD   VARCHAR(10)    NOT NULL DEFAULT 'ACTIVE', -- ACTIVE/DORMANT/RETIRED
  LANG_CD        VARCHAR(5)     NOT NULL DEFAULT 'ko',
  TIMEZONE_ID    VARCHAR(50)    NOT NULL DEFAULT 'Korea Standard Time',
  MFA_SECRET_ENC VARBINARY(256) NULL,
  LAST_LOGIN_DT  DATETIME2(0)   NULL
);

-- 역할 / 사용자-역할
CREATE TABLE FW.TB_ROLE (
  ROLE_ID        VARCHAR(30)   NOT NULL PRIMARY KEY,
  ROLE_NM        NVARCHAR(100) NOT NULL,
  DATA_SCOPE_CD  VARCHAR(10)   NOT NULL DEFAULT 'SELF', -- ALL/COMPANY/DEPT_SUB/DEPT/SELF
  COMPANY_TYPE_CD VARCHAR(10)  NULL,                    -- 줄 수 있는 회사 유형 HQ/PARTNER (NULL: 모두)
  SORT_NO        INT           NOT NULL DEFAULT 0,
  USE_YN         CHAR(1)       NOT NULL DEFAULT 'Y'
);
CREATE TABLE FW.TB_USER_ROLE (
  USER_ID   VARCHAR(30) NOT NULL,
  ROLE_ID   VARCHAR(30) NOT NULL,
  START_DT  DATE        NOT NULL DEFAULT CAST(GETDATE() AS DATE),
  END_DT    DATE        NOT NULL DEFAULT '9999-12-31',
  PRIMARY KEY (USER_ID, ROLE_ID)
);

-- 프로그램(화면) / 프로그램 기능
CREATE TABLE FW.TB_PROGRAM (
  PROGRAM_ID   VARCHAR(30)   NOT NULL PRIMARY KEY,  -- 예: SYS_USER_MGT
  MODULE_CD    VARCHAR(20)   NOT NULL,
  PROGRAM_NM   NVARCHAR(100) NOT NULL,              -- 관리용 기본명
  URL          VARCHAR(300)  NOT NULL,              -- /Sys/User/Index
  PII_YN       CHAR(1)       NOT NULL DEFAULT 'N',  -- 개인정보 처리 화면 여부(접속기록 대상)
  USE_YN       CHAR(1)       NOT NULL DEFAULT 'Y'
);
CREATE TABLE FW.TB_PROGRAM_FUNC (
  PROGRAM_ID   VARCHAR(30) NOT NULL,
  FUNC_CD      VARCHAR(20) NOT NULL,                -- 공통코드 FUNC_CD
  PRIMARY KEY (PROGRAM_ID, FUNC_CD)
);
CREATE TABLE FW.TB_PROGRAM_API (                    -- API/Action → 프로그램+기능 매핑
  HTTP_METHOD  VARCHAR(10)  NOT NULL,
  ROUTE_PATTERN VARCHAR(300) NOT NULL,
  PROGRAM_ID   VARCHAR(30)  NOT NULL,
  FUNC_CD      VARCHAR(20)  NOT NULL,
  PRIMARY KEY (HTTP_METHOD, ROUTE_PATTERN)
);

-- 역할별 / 사용자별 기능 권한
CREATE TABLE FW.TB_ROLE_PROGRAM_FUNC (
  ROLE_ID     VARCHAR(30) NOT NULL,
  PROGRAM_ID  VARCHAR(30) NOT NULL,
  FUNC_CD     VARCHAR(20) NOT NULL,
  PRIMARY KEY (ROLE_ID, PROGRAM_ID, FUNC_CD)
);
CREATE TABLE FW.TB_USER_PROGRAM_FUNC (
  USER_ID     VARCHAR(30) NOT NULL,
  PROGRAM_ID  VARCHAR(30) NOT NULL,
  FUNC_CD     VARCHAR(20) NOT NULL,
  GRANT_TYPE  CHAR(5)     NOT NULL,                 -- GRANT / DENY
  END_DT      DATE        NOT NULL DEFAULT '9999-12-31',
  PRIMARY KEY (USER_ID, PROGRAM_ID, FUNC_CD)
);

-- 메뉴 트리 / 다국어 메뉴명
CREATE TABLE FW.TB_MENU (
  MENU_ID        INT IDENTITY  NOT NULL PRIMARY KEY,
  MENU_SET_CD    VARCHAR(10)   NOT NULL DEFAULT 'PORTAL',
  OPEN_SCOPE_CD  VARCHAR(10)   NOT NULL DEFAULT 'ALL', -- ALL/EMP/PARTNER (회사 유형별 공개)
  PARENT_MENU_ID INT           NULL,
  MENU_TYPE_CD   VARCHAR(10)   NOT NULL,           -- FOLDER/PAGE/LINK/POPUP
  PROGRAM_ID     VARCHAR(30)   NULL,
  LINK_URL       VARCHAR(500)  NULL,
  ICON_CD        VARCHAR(50)   NULL,
  SORT_NO        INT           NOT NULL DEFAULT 0,
  NEW_WIN_YN     CHAR(1)       NOT NULL DEFAULT 'N',
  USE_YN         CHAR(1)       NOT NULL DEFAULT 'Y'
);
CREATE INDEX IX_TB_MENU_PARENT ON FW.TB_MENU (MENU_SET_CD, PARENT_MENU_ID, SORT_NO);
CREATE TABLE FW.TB_MENU_LANG (
  MENU_ID  INT           NOT NULL,
  LANG_CD  VARCHAR(5)    NOT NULL,
  MENU_NM  NVARCHAR(100) NOT NULL,
  PRIMARY KEY (MENU_ID, LANG_CD)
);

-- 공통코드
CREATE TABLE FW.TB_CODE_GROUP (
  GROUP_CD     VARCHAR(30)   NOT NULL PRIMARY KEY,
  GROUP_NM     NVARCHAR(100) NOT NULL,
  SYSTEM_YN    CHAR(1)       NOT NULL DEFAULT 'N',  -- Y면 삭제 불가
  ATTR1_NM     NVARCHAR(50)  NULL,                  -- 추가 속성의 의미 정의
  ATTR2_NM     NVARCHAR(50)  NULL,  -- ... ATTR5_NM
  USE_YN       CHAR(1)       NOT NULL DEFAULT 'Y'
);
CREATE TABLE FW.TB_CODE (
  GROUP_CD     VARCHAR(30)   NOT NULL,
  CODE         VARCHAR(30)   NOT NULL,
  PARENT_CODE  VARCHAR(30)   NULL,                  -- 계층형 코드
  SORT_NO      INT           NOT NULL DEFAULT 0,
  ATTR1        NVARCHAR(200) NULL,  -- ... ATTR5
  START_DT     DATE          NOT NULL DEFAULT '1900-01-01',
  END_DT       DATE          NOT NULL DEFAULT '9999-12-31',
  USE_YN       CHAR(1)       NOT NULL DEFAULT 'Y',
  PRIMARY KEY (GROUP_CD, CODE)
);
CREATE TABLE FW.TB_CODE_LANG (
  GROUP_CD VARCHAR(30) NOT NULL, CODE VARCHAR(30) NOT NULL, LANG_CD VARCHAR(5) NOT NULL,
  CODE_NM  NVARCHAR(200) NOT NULL,
  PRIMARY KEY (GROUP_CD, CODE, LANG_CD)
);

-- 다국어 리소스 / 설정
CREATE TABLE FW.TB_LANG_RESOURCE (
  RES_KEY   VARCHAR(100)   NOT NULL,                -- BTN.SAVE, MSG.SAVED, SYS.USER.TITLE
  LANG_CD   VARCHAR(5)     NOT NULL,
  RES_TEXT  NVARCHAR(1000) NOT NULL,
  RES_TYPE  VARCHAR(10)    NOT NULL DEFAULT 'LABEL', -- LABEL/MSG/BTN/TOOLTIP
  PRIMARY KEY (RES_KEY, LANG_CD)
);
CREATE TABLE FW.TB_CONFIG (
  CONFIG_KEY   VARCHAR(100)   NOT NULL PRIMARY KEY,  -- SESSION.IDLE_MINUTES
  CONFIG_VAL   NVARCHAR(2000) NULL,
  VAL_TYPE_CD  VARCHAR(10)    NOT NULL,              -- STRING/INT/BOOL/JSON/SECRET
  ENC_YN       CHAR(1)        NOT NULL DEFAULT 'N',  -- SECRET은 DPAPI/AES 암호화 저장
  CATEGORY_CD  VARCHAR(20)    NOT NULL,
  DESCRIPTION  NVARCHAR(500)  NULL,
  RESTART_YN   CHAR(1)        NOT NULL DEFAULT 'N'   -- 재기동 필요 여부 표시
);
CREATE TABLE FW.TB_CACHE_VERSION (
  CACHE_KEY  VARCHAR(50) NOT NULL PRIMARY KEY,       -- MENU, AUTH, CODE, LANG, CONFIG
  VERSION_NO BIGINT      NOT NULL DEFAULT 1
);
```

**운영 설정**: DB는 `READ_COMMITTED_SNAPSHOT ON`(조회-저장 충돌 감소), Query Store ON, 로그 테이블은 월 단위 파티션으로 보관 기간이 지나면 파티션 단위로 제거합니다. 로그 보관은 아래 정책을 따릅니다.

**개인정보와 접속기록 정책**

- **원칙**: 포털은 개인정보를 저장하지 않습니다. 사용자 정보는 사번/ID, 이름, 알림용 업무 이메일로 한정하고, 휴대전화·주민번호·주소 등은 컬럼 자체를 두지 않습니다.
- **예외**: 업무상 반드시 저장해야 하면 사전 검토 후 다음을 모두 적용합니다. 컬럼 단위 AES-256-GCM 암호화(키는 DB 밖에서 DPAPI로 보호), 고유식별정보는 SQL Server Always Encrypted, 화면 마스킹, `PII` 권한자만 원문 조회, 조회·다운로드 접속기록, 보유 기간 경과 시 파기 배치.
- **접속기록 10년 보관**: `LOG` 스키마 테이블은 월 단위 파티션 + 페이지 압축을 쓰고, 1년이 지난 파티션은 읽기 전용 파일그룹으로 옮기며 10년이 지나면 파티션 단위로 삭제합니다. 보관 기간은 `LOG.RETENTION_YEARS`(기본 10)로 설정합니다. 오래된 파일그룹은 별도 백업 매체에도 보관합니다.
- **위·변조 방지**: 로그 테이블에는 애플리케이션 계정의 INSERT만 허용하고, 행마다 이전 행의 해시를 이어 저장(해시 체인)해 월별 무결성 점검 배치로 검증합니다.
- **용량**: 10년치 로그는 접속량에 따라 차이가 커서, 1단계에서 실제 일 로그량을 측정해 DB 디스크를 산정합니다.

## 8. SP 표준

모든 DB 작업은 아래 템플릿을 따르는 SP로 만들고, 애플리케이션은 `SpExecutor` 하나로만 호출합니다. SP 동작 접미어는 6개로 고정합니다.

| 접미어 | 용도 | 반환 |
| --- | --- | --- |
| `_GET_LIST` | 목록 조회(페이징) | 1) 목록 2) 전체 건수 |
| `_GET` | 단건 상세 | 상세 1행 (+하위 다중 결과셋) |
| `_GET_TREE` | 트리 조회 | 계층 레벨·경로 포함 목록 |
| `_SAVE` | 등록/수정 (단건 또는 TVP 일괄) | 처리 건수, 신규 키 |
| `_DEL` | 삭제 (기본 논리삭제 `USE_YN='N'`) | 처리 건수 |
| `_EXEC` | 업무 처리(확정, 마감 등) | 처리 결과 |

**작성 규칙**

- `SET NOCOUNT ON; SET XACT_ABORT ON;`으로 시작하고 `TRY/CATCH` + 트랜잭션을 쓴다. 저장 계열은 SP 내부에서 트랜잭션을 열고, 여러 SP를 묶을 때만 애플리케이션 트랜잭션 스코프를 쓴다.
- 공통 파라미터 `@P_USER_ID`, `@P_LANG_CD`, `@P_CLIENT_IP`는 항상 받는다(SpExecutor가 자동 주입, 화면에서 보낸 값은 무시).
- 동적 SQL은 금지하며, 정렬 컬럼 같은 불가피한 경우만 `sp_executesql` + 화이트리스트 `QUOTENAME` 검증을 쓴다.
- 업무 오류는 `THROW 50001~59999`로 던지고 메시지에는 다국어 키(`MSG.DUP_CODE`)를 넣는다. 애플리케이션이 키를 사용자 언어로 번역한다.
- 페이징은 `OFFSET ... FETCH`, 선택형 검색 조건 `(@P_X IS NULL OR COL = @P_X)`에는 `OPTION (RECOMPILE)`을 붙여 실행 계획 오염을 막는다.
- 변경 SP는 `LOG.USP_AUDIT_WRITE`를 호출해 변경 전/후 값을 JSON으로 남긴다(감사 대상 테이블은 설정으로 지정).
- 동시성: 수정 시 `@P_ROW_VER`를 받아 다르면 `MSG.CONCURRENCY` 오류.

**SP 템플릿 (목록 조회)**

```sql
CREATE OR ALTER PROCEDURE FW.USP_CODE_GET_LIST
  @P_GROUP_CD   VARCHAR(30),
  @P_KEYWORD    NVARCHAR(100) = NULL,
  @P_USE_YN     CHAR(1)       = NULL,
  @P_PAGE_NO    INT           = 1,
  @P_PAGE_SIZE  INT           = 50,
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45)   = NULL
AS
BEGIN
  SET NOCOUNT ON;
  SELECT C.CODE, ISNULL(L.CODE_NM, LK.CODE_NM) AS CODE_NM, C.PARENT_CODE,
         C.SORT_NO, C.ATTR1, C.ATTR2, C.USE_YN, C.ROW_VER
    FROM FW.TB_CODE C
    LEFT JOIN FW.TB_CODE_LANG L  ON L.GROUP_CD = C.GROUP_CD AND L.CODE = C.CODE AND L.LANG_CD = @P_LANG_CD
    LEFT JOIN FW.TB_CODE_LANG LK ON LK.GROUP_CD = C.GROUP_CD AND LK.CODE = C.CODE AND LK.LANG_CD = 'ko'
   WHERE C.GROUP_CD = @P_GROUP_CD
     AND (@P_USE_YN IS NULL OR C.USE_YN = @P_USE_YN)
     AND (@P_KEYWORD IS NULL OR C.CODE LIKE @P_KEYWORD + '%' OR L.CODE_NM LIKE '%' + @P_KEYWORD + '%')
   ORDER BY C.SORT_NO, C.CODE
  OFFSET (@P_PAGE_NO - 1) * @P_PAGE_SIZE ROWS FETCH NEXT @P_PAGE_SIZE ROWS ONLY
  OPTION (RECOMPILE);

  SELECT COUNT(*) AS TOTAL_CNT
    FROM FW.TB_CODE C
    LEFT JOIN FW.TB_CODE_LANG L ON L.GROUP_CD = C.GROUP_CD AND L.CODE = C.CODE AND L.LANG_CD = @P_LANG_CD
   WHERE C.GROUP_CD = @P_GROUP_CD
     AND (@P_USE_YN IS NULL OR C.USE_YN = @P_USE_YN)
     AND (@P_KEYWORD IS NULL OR C.CODE LIKE @P_KEYWORD + '%' OR L.CODE_NM LIKE '%' + @P_KEYWORD + '%')
  OPTION (RECOMPILE);
END
```

**SP 템플릿 (그리드 일괄 저장, TVP)**

```sql
CREATE TYPE FW.TVP_CODE AS TABLE (
  ROW_STAT CHAR(1) NOT NULL,      -- I/U/D (그리드 행 상태)
  CODE VARCHAR(30) NOT NULL, CODE_NM NVARCHAR(200) NULL, SORT_NO INT NULL,
  ATTR1 NVARCHAR(200) NULL, USE_YN CHAR(1) NULL, ROW_VER BINARY(8) NULL
);
GO
CREATE OR ALTER PROCEDURE FW.USP_CODE_SAVE
  @P_GROUP_CD VARCHAR(30), @P_ROWS FW.TVP_CODE READONLY,
  @P_USER_ID VARCHAR(30), @P_LANG_CD VARCHAR(5), @P_CLIENT_IP VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRY
    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM @P_ROWS R JOIN FW.TB_CODE C ON C.GROUP_CD = @P_GROUP_CD AND C.CODE = R.CODE
                WHERE R.ROW_STAT IN ('U','D') AND C.ROW_VER <> R.ROW_VER)
      THROW 50002, 'MSG.CONCURRENCY', 1;
    -- I: INSERT, U: UPDATE, D: USE_YN='N' (MERGE 또는 개별 DML)
    -- TB_CODE_LANG 저장, LOG.USP_AUDIT_WRITE 호출
    UPDATE FW.TB_CACHE_VERSION SET VERSION_NO += 1 WHERE CACHE_KEY = 'CODE';
    COMMIT;
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
  END CATCH
END
```

**애플리케이션 호출 방식**

```csharp
// 목록 + 건수 (다중 결과셋)
var page = await _sp.QueryPageAsync<CodeDto>("FW.USP_CODE_GET_LIST",
    new { P_GROUP_CD = groupCd, P_KEYWORD = keyword, P_PAGE_NO = pageNo, P_PAGE_SIZE = 50 });

// 그리드 일괄 저장 (TVP)
await _sp.ExecuteAsync("FW.USP_CODE_SAVE",
    new { P_GROUP_CD = groupCd, P_ROWS = Tvp.From("FW.TVP_CODE", rows) });
```

`SpExecutor`는 명령 타임아웃(설정값, 기본 30초), 느린 SP 로깅(기본 1초 초과), `SqlException` → 업무 오류 변환, 결과 매핑 캐시(리플렉션 1회)를 담당합니다. 애플리케이션 DB 계정에는 `GRANT EXECUTE ON SCHEMA::FW` 등 스키마 단위 실행 권한만 줍니다.

## 9. 보안 표준

보안은 프레임워크 공통 계층에서 기본 적용하고, 릴리스마다 아래 항목을 점검표로 확인합니다. 기준은 OWASP Top 10, 행안부·KISA 소프트웨어 개발보안 가이드(보안약점 49개 항목), 주요정보통신기반시설 웹 취약점 점검 항목, 개인정보의 안전성 확보조치 기준입니다.

| 위협 | 프레임워크 대응 | 담당 구성요소 |
| --- | --- | --- |
| SQL 삽입 | SP + 파라미터 바인딩만 허용, DB 계정 EXECUTE 전용, 동적 SQL 금지 | SpExecutor, DB 권한 |
| XSS | Razor 기본 인코딩, `Html.Raw` 금지(분석기 규칙), 게시판 HTML은 화이트리스트 필터, CSP nonce | HtmlSanitizer(자체), 보안 헤더 |
| CSRF | 모든 변경 요청 Antiforgery 토큰 자동 검증(`fw.ajax`가 헤더 자동 첨부), SameSite 쿠키 | 글로벌 필터 |
| 접근통제 우회(IDOR) | 모든 Action 권한 검사(기본 거부), 데이터 범위는 SP에서 사용자 기준 필터, 파일 다운로드도 권한 검사 | 권한 필터, SP |
| 인증 취약 | PBKDF2 해시, 실패 잠금, 비밀번호 규칙·이력, 세션 재발급, 중복 로그인 통제, MFA | Portal.Security |
| 자동화 공격 | 로그인·API Rate Limit(.NET 내장 RateLimiter), 일정 실패 후 지연 | 미들웨어 |
| 파일 업로드 | 확장자 화이트리스트 + 매직 넘버 검사, 크기 제한(설정), 웹 루트 밖 저장, GUID 파일명, 실행 권한 없는 폴더, (선택) Defender 검사, 묶음의 프로그램 권한 확인, 협력사는 자기 회사 묶음만(공유 묶음은 조회만) | FileService, 파일 SP |
| 경로 조작 | 파일은 ID로만 요청, 서버가 경로 조합 후 기준 폴더 밖이면 거부 | FileService |
| 오픈 리다이렉트 | `returnUrl`은 로컬 URL만 허용 (`Url.IsLocalUrl`) | 로그인 |
| SSRF | 외부 호출은 `TB_API_OUTBOUND`에 등록된 호스트만 허용, 내부 IP 대역 차단 | HttpClient 핸들러 |
| 정보 노출 | 사용자에게는 추적 ID만 표시, 상세 예외는 서버 로그. IIS `Server`/`X-Powered-By` 헤더 제거, 디렉터리 리스팅 비활성 | 예외 미들웨어, IIS |
| 민감정보 저장 | 개인정보 컬럼 AES-256-GCM 암호화(키는 Data Protection/DPAPI로 보호), 화면 마스킹(`홍*동`, `010-****-1234`), `PII` 권한자만 원문 | CryptoService, MaskingService |
| 전송 구간 | TLS 1.2/1.3만 허용, HSTS, DB 연결 `Encrypt=True` | IIS, 연결 문자열 |
| 설정·비밀값 | DB 연결은 Windows 통합 인증(gMSA 앱 풀 계정) 우선, 비밀값은 암호화 저장, 소스에 비밀값 금지 | 배포 설정 |
| 로그·추적 | 로그인, 권한 변경, 개인정보 조회·다운로드 접속기록 10년 보관(설정), 비밀번호·토큰 기록 금지, 로그 테이블은 INSERT 전용 | LOG 스키마 |
| 취약 구성요소 | OSS 허용 목록, 빌드 시 취약 패키지 검사, 월 1회 Windows/.NET/SQL 패치 | CI |

**보안 헤더 기본값**

```
Strict-Transport-Security: max-age=31536000; includeSubDomains
Content-Security-Policy: default-src 'self'; script-src 'self' 'nonce-{random}'; style-src 'self';
                         img-src 'self' data:; frame-ancestors 'self'; form-action 'self'; object-src 'none'
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=()
Cache-Control: no-store   (인증이 필요한 동적 페이지)
```

**개발 보안 절차**: Roslyn 보안 분석기(Microsoft.CodeAnalysis.NetAnalyzers)를 빌드에 포함해 위반 시 빌드를 실패시키고, 오픈 전 웹 취약점 진단(모의해킹)을 받습니다. 보안약점 49개 항목별 대응 매핑표는 별도 점검표로 관리합니다.

## 10. 설정 관리·다국어·캐시

설정은 "서버마다 다른 값"만 `appsettings.json`에, "업무 정책값"은 모두 `TB_CONFIG`에 둡니다. 운영자는 설정 관리 화면에서 변경하고, 변경은 이력(`TB_CONFIG_HIST`)에 남으며 캐시 버전 증가로 5초 안에 모든 서버에 반영됩니다.

| 위치 | 담는 것 | 예 |
| --- | --- | --- |
| `appsettings.{환경}.json` | 서버 의존값 (최소한) | DB 연결 문자열, 활성 모듈 목록, 파일 저장 루트, 로그 레벨, Data Protection 키 폴더(`Portal:DataProtection:KeyPath`) |
| `TB_CONFIG` | 정책·기능 스위치 | 인증 모드, 비밀번호 규칙, 세션 시간, 업로드 확장자·크기, 메일 서버, 로그 보관 기간, 사이트명·로고 |
| `TB_CODE` | 선택 목록 | 사용자 상태, 기능코드, 게시판 유형, 파일 구분 |
| `TB_LANG_RESOURCE` | 화면에 보이는 모든 문구 | 라벨, 버튼, 메시지, 유효성 오류 |
| `TB_MSG_TEMPLATE` | 메일/알림 본문 | 비밀번호 초기화 안내, 게시글 등록 알림 |

코드에서는 `IPortalConfig.Get<int>(ConfigKeys.SessionIdleMinutes)`처럼 상수 키로만 읽고, 기본값은 초기 데이터 스크립트에 둡니다. 보안상 낮추면 안 되는 값(쿠키 Secure, TLS 등)은 설정으로 열지 않습니다.

**다국어**

- 기본 언어 `ko`, 추가 언어는 `TB_LANG`에 행만 추가하면 됩니다(초기 제공: 한국어 ko, 영어 en, 중국어 간체 zh-CN). 번역이 없으면 기본 언어 → 키 순으로 대체 표시합니다.
- 서버: DB 기반 `IStringLocalizer` 구현. Razor에서는 `@L["SYS.USER.TITLE"]`, Tag Helper에서는 `text-key` 속성을 씁니다.
- 클라이언트: 언어별 JSON 번들(`/i18n/ko.json?v={버전}`)을 브라우저 캐시하고 `fw.t('MSG.SAVED')`로 사용합니다.
- 메뉴명, 코드명, 게시판명은 각 `_LANG` 테이블에서, 날짜·숫자·통화 형식은 .NET Culture로, 시간대는 DB에 UTC로 저장 후 사용자 시간대로 표시합니다.
- 번역 관리 화면에서 미번역 키 목록 조회, 엑셀 내보내기/가져오기를 제공합니다.

중국어는 웹 폰트를 내려받지 않고 OS 기본 글꼴(Microsoft YaHei) 스택을 쓰며, PDF 출력용으로 서버에 한국어·중국어 글꼴을 설치합니다. 번체가 필요하면 `zh-TW` 언어 행만 추가하면 됩니다.

**캐시 전략**

| 대상 | 위치 | 무효화 |
| --- | --- | --- |
| 메뉴 트리(언어별), 프로그램·API 매핑 | 서버 메모리 | `TB_CACHE_VERSION` MENU |
| 사용자별 권한 집합 | 서버 메모리 (사용자당 수 KB, 슬라이딩 30분) | AUTH 버전 또는 해당 사용자 키 삭제 |
| 공통코드, 다국어, 설정 | 서버 메모리 + 브라우저(JSON) | CODE / LANG / CONFIG 버전 |
| 정적 파일(JS/CSS/이미지) | 브라우저 1년 | 빌드 시 파일 해시(fingerprint) 변경 |
| 대시보드 위젯 데이터 | 서버 메모리 (위젯별 TTL 설정) | TTL 만료 |

## 11. 업무 공통 모듈

요구된 모듈(기준정보, 공통코드, 게시판, PDF, 레포트, 대시보드, 외부 API)에 더해, 업무 시스템을 올릴 때 반복해서 필요한 10개 기능을 프레임워크에 포함합니다. 모두 동일한 권한·다국어·로그 체계를 따릅니다.

**요구 모듈**

| 모듈 | 핵심 설계 |
| --- | --- |
| 기준정보 | 회사·부서(트리, 유효기간으로 조직 개편 이력 보존), 직급·직책, 휴일/영업일 달력, 환율. 공통 선택 팝업(사용자/부서/코드 검색) 제공 |
| 공통코드 | 그룹-코드 2단 + 코드 내 계층, 추가 속성 5개(그룹별 의미 정의), 유효기간. 화면에서는 `<fw-select code-group="USER_STAT">` 한 줄로 사용 |
| 게시판 | 게시판 마스터 설정으로 무한 생성: 유형(일반/공지/FAQ/자료실/Q&A), 답글·댓글·첨부·익명·비밀글·게시기간·상단 고정·조회자 목록 사용 여부, 게시판별 역할 권한. 편집기는 자체 경량 에디터(contenteditable) + 서버 HTML 화이트리스트 필터 |
| PDF 변환 | ① 화면/레포트 HTML → Edge Headless로 PDF(인쇄용 CSS 적용) ② 코드 기반 문서 → MigraDoc ③ PDF 병합·워터마크(출력자·일시)·암호 설정 → PDFsharp. 변환은 큐로 처리해 동시 실행 수를 제한(기본 4) |
| 레포트 | `TB_REPORT`에 레포트 ID, 데이터 SP, 파라미터 정의, 템플릿(Razor 뷰)을 등록하면 파라미터 입력 화면·미리보기·PDF/엑셀 출력이 자동 제공. 머리글/바닥글/페이지 번호/결재란 공통 파셜. 대용량은 백그라운드 생성 후 알림 |
| 대시보드 | 위젯 = 데이터 SP + 표시 유형(숫자 카드/막대/선/도넛/목록/공지) + 캐시 TTL + 권한. 사용자는 허용된 위젯을 12칸 그리드에 배치하고 레이아웃을 저장. 위젯은 비동기로 각각 로드해 한 위젯이 느려도 화면은 즉시 표시 |
| 외부 API (수신) | `/api/v1/*`, OAuth2 Client Credentials 토큰, 클라이언트별 허용 API·IP·호출 한도, OpenAPI 문서 자동 생성(.NET 내장), 요청/응답 로그(민감 필드 마스킹) |
| 외부 API (송신) | `TB_API_OUTBOUND`에 대상 URL·인증 방식·타임아웃·재시도 횟수 등록, `IHttpClientFactory` + 내장 Resilience(재시도, 회로 차단). 코드에는 대상 키만 사용 |

**추가 핵심 기능 (프레임워크 포함 권장)**

| 기능 | 필요한 이유 | 설계 요지 |
| --- | --- | --- |
| 파일 관리 | 모든 모듈 첨부 공통 | 파일 그룹 ID로 연결, 드래그 업로드, 일괄 ZIP 다운로드, 다운로드 이력 |
| 엑셀 업/다운로드 | 기준정보 일괄 등록, 목록 출력 | 그리드 컬럼 정의 그대로 출력, 업로드는 검증 결과 미리보기 후 TVP 저장, 다운로드 사유 입력 옵션(개인정보) |
| 감사 로그·접속기록 | 보안 심사, 개인정보 법적 의무 | 로그인, 화면 접근, 데이터 변경 전/후, 개인정보 조회 사유. 조회 화면과 월별 점검 리포트 |
| 알림센터·메일 | 승인 요청, 댓글, 배치 결과 전달 | 포털 알림(상단 배지, 60초 폴링 또는 SSE), 메일 큐 + SMTP 발송, 템플릿 다국어. 업무 SP가 같은 트랜잭션으로 `TB_MSG_QUEUE`에 템플릿 ID·치환값·암호화한 주소를 쌓고, 발송을 마치면 주소·치환값을 지운다. Portal.Worker가 1분마다 최대 20건씩 잠가 가져가(`USP_MSG_QUEUE_CLAIM_EXEC`, 여러 대가 떠도 중복 발송 없음) SMTP로 보내고, 실패는 최대 3회 재시도한다. Worker는 웹과 같은 Data Protection 키 폴더를 공유한다. 포털 알림은 같은 SP가 템플릿의 포털 채널이 켜져 있으면 `TB_NOTIFICATION`에 쌓고(문구는 볼 때 화면 언어로), 헤더 배지가 `NOTIFY.POLL_SECONDS`마다 안 읽은 수를 센다. 배지 조회는 세션을 연장하지 않고(`X-Portal-Background`) 접속기록도 남기지 않는다(`[SkipAccessLog]`). 승인 담당자처럼 기능 권한으로 받는 사람을 고를 때는 `FW.UFN_FUNC_USERS`를 쓴다 |
| 스케줄러 | 야간 동기화, 집계, 정리 작업 | Portal.Worker의 Cron 스케줄러(자체 구현, 5필드, 한국 시간), `TB_JOB`에 주기·대상 SP/클래스 등록, 수동 실행(`RUN_REQ_YN`), 실패 알림(연속 실패는 한 번), 중복 실행 방지(DB 잠금, 제한 시간이 지나면 넘겨받음), 실행 이력 `TB_JOB_HIST`. SP 작업은 `USP_JOB_..._EXEC` 이름만 실행한다. 운영 > 스케줄 관리(`SYS_JOB_MGT`)에서 주기·제한 시간·사용 여부를 바꾸고 수동 실행한다(저장 동시성은 Worker가 바꾸는 `ROW_VER` 대신 `EDIT_VER`로 검사) |
| 승인(결재) 엔진 | 업무 문서 승인 흐름 공통화 | 사내 전자결재 연동이 기본. \`IApprovalGateway\`(상신, 상태 조회, 결과 콜백)로 분리하고 실제 연동은 업무 개발 시 구현. 업무 키와 결재 문서 번호 매핑(\`TB\_APPROVAL\_LINK\`), 결재 결과 수신 시 업무 SP 실행. 전자결재를 쓸 수 없는 협력사 업무용으로 간단한 내부 승인선 제공 |
| 권한 위임·대리 | 휴가·출장 시 업무 공백 방지 | 기간 한정 역할 위임, 위임 중 처리는 "대리자(원권한자)"로 기록 |
| 공지 팝업·시스템 점검 모드 | 운영 공지, 배포 중 접근 차단 | 기간·대상 지정 팝업, 점검 모드 시 관리자만 접속 |
| 통합 검색 | 메뉴·게시글·사용자 빠른 찾기 | 메뉴 검색은 클라이언트, 게시글은 SQL Server Full-Text Search |
| 운영 모니터링 | 장애 조기 발견 | `/health` 엔드포인트(DB, 파일 저장소, AD), 오류·느린 SP 현황 화면, 캐시 강제 갱신, 접속자 현황·강제 로그아웃 |

## 12. UI 프레임워크와 표준 화면

화면은 상단 헤더 + 좌측 메뉴 트리 + 멀티 탭(MDI) 본문의 고정 레이아웃에, 자체 JS 컴포넌트 라이브러리(`fw.*`)와 Tag Helper로 구성합니다. 새 화면은 아래 8가지 표준 유형 중 하나를 복사해 시작합니다.

**레이아웃**

- 헤더: 로고(설정), 메뉴 검색(초성 검색 지원), 언어 전환, 알림, 사용자 메뉴(내 정보, 비밀번호 변경, 로그아웃), 세션 남은 시간
- 좌측 메뉴 트리: 아이콘 + 다단 펼침, 접기(아이콘만 표시), 즐겨찾기 탭, 최근 사용 메뉴, 현재 위치 자동 펼침, 키보드 탐색
- 본문: 탭 최대 개수 설정(기본 10), 탭별 상태 유지, 새로고침·닫기·다른 탭 닫기. 탭 없이 단일 화면 모드도 설정으로 선택
- 테마: CSS 변수 기반 라이트/다크, 회사별 브랜드 색상은 설정값으로 주입. 화면 폭 768px 이하에서는 메뉴가 드로어로 바뀜

**공통 컴포넌트 (`fw.*`, 자체 구현)**

| 컴포넌트 | 기능 |
| --- | --- |
| `fw.grid` | 가상 스크롤(1만 행 이상), 서버 페이징, 정렬, 컬럼 고정·너비 저장, 인라인 편집 + 행 상태(I/U/D) 추적, 합계 행, 엑셀 출력, 코드 컬럼 자동 명칭 표시 |
| `fw.tree` | 메뉴·부서·코드 트리 (구현: 평평한 목록을 상위 ID로 트리로 그림, 키보드·스크린리더 지원. 지연 로딩·체크박스·드래그 정렬은 필요한 화면에서 추가) |
| `fw.form` | 자동 바인딩, 필수·형식 검증(서버 검증 규칙과 동일 메시지 키), 변경 감지 후 이탈 경고 |
| `fw.popup` / `fw.dialog` | 공통 선택 팝업(사용자/부서/코드), 확인·알림 대화상자(다국어) |
| `fw.ajax` | CSRF 헤더 자동, 로딩 표시, 오류 공통 처리(401 → 로그인, 403 → 권한 안내), 중복 제출 방지 |
| `fw.chart` | Chart.js 래퍼, 테마 색상 자동 적용 |
| 입력 컴포넌트 | 날짜/기간(네이티브 input + 보조), 숫자·금액 포맷, 코드 셀렉트, 파일 업로드 |

**표준 화면 예제 (Portal.Modules.Sample)**

| # | 유형 | 구성 | 적용 예 |
| --- | --- | --- | --- |
| 1 | 조회형 | 검색 조건 + 그리드 + 엑셀 | 사용자 목록, 로그 조회 |
| 2 | 그리드 편집형 | 그리드 인라인 행추가/수정/삭제 + 일괄 저장(TVP) | 공통코드, 환율 |
| 3 | 마스터-디테일 | 상단 목록 선택 → 하단 상세 그리드 | 코드 그룹-코드, 역할-사용자 |
| 4 | 목록-폼 | 좌측 목록 + 우측 상세 폼(신규/저장/삭제) | 사용자 관리 |
| 5 | 트리-폼 | 좌측 트리 + 우측 상세 | 메뉴 관리, 부서 관리 |
| 6 | 권한 매트릭스 | 역할 선택 → 프로그램 트리 × 기능 체크박스 | 역할별 권한 관리 |
| 7 | 등록 폼(단독) + 첨부 + 승인 | 입력 폼, 첨부파일, 승인 요청 | 게시글, 신청서 |
| 8 | 엑셀 업로드형 | 양식 다운로드 → 업로드 → 검증 결과 → 저장 | 사용자 일괄 등록 |

모든 예제는 Controller, View, JS, SP, 다국어 키, 메뉴·프로그램 등록 스크립트까지 한 세트로 제공해, 개발자가 복사 후 이름만 바꾸면 동작하도록 합니다.

## 13. 성능 기준과 전략

목표는 화면 최초 표시 1초 이내, 일반 조회 API 서버 응답 p95 300ms 이내이며, 기준을 넘는 요청은 자동으로 로그에 남겨 추적합니다. 수치는 사내망 기준 초안이며 운영 환경에 맞게 조정합니다.

| 항목 | 목표 (초안) | 수단 |
| --- | --- | --- |
| 화면 최초 표시 | 1초 이내 | 서버 렌더링, 정적 파일 번들·압축(Brotli)·장기 캐시, 외부 CDN 미사용 |
| 조회 API p95 | 300ms 이내 | 서버 페이징(기본 50행), 인덱스 설계, SP 실행계획 검토 |
| 권한·메뉴 판단 | 요청당 DB 호출 0회 | 메모리 캐시(10장) |
| 느린 SP 감지 | 1초 초과 시 기록 | SpExecutor 시간 측정, Query Store |
| 동시 사용자 | 웹서버 1대당 수백 명 수준 (부하 테스트로 확정) | 비동기 I/O, 연결 풀, 웹팜 확장 |

**규칙**: 목록 SP는 반드시 페이징하고 `SELECT *`를 금지합니다. 로그 적재·메일·PDF 생성 같은 부수 작업은 비동기 큐로 넘기고, 대용량 엑셀은 스트리밍으로 출력합니다. 릴리스 전 표준 화면 대상 부하 테스트(무료 도구, 예: JMeter 또는 자체 스크립트)를 실시합니다.

## 14. 유지보수성과 테스트 전략

모든 프레임워크 코드는 인터페이스 + 의존성 주입으로 작성해 DB·AD·외부 API 없이 단위 테스트가 가능하고, SP·권한·화면 흐름은 실제 SQL Server 대상 통합 테스트로 검증합니다. 테스트가 통과하지 않으면 배포 패키지를 만들지 않습니다.

**유지보수 원칙**

- **설정으로 바꾸고, 코드는 덜 바꾼다**: 메뉴·권한·코드·문구·정책값 변경은 관리 화면에서 처리해 배포가 필요 없습니다.
- **모듈 경계**: 업무 모듈은 프레임워크만 참조하고 서로 직접 참조하지 않아, 한 모듈 수정이 다른 모듈에 번지지 않습니다. 참조 규칙은 아키텍처 테스트로 강제합니다.
- **표준 템플릿**: 화면 8유형, SP 6유형 템플릿과 `dotnet new` 프로젝트/항목 템플릿을 제공해 누가 만들어도 같은 구조가 됩니다.
- **코드 규칙 자동화**: `.editorconfig` + .NET 분석기를 빌드 오류 수준으로 적용하고, Nullable 참조 형식을 켭니다.
- **추적 가능성**: 모든 요청에 추적 ID를 부여해 화면 오류 메시지 → 서버 로그 → SP 로그를 한 번에 찾습니다.
- **DB 형상 관리**: 테이블·SP는 SSDT 프로젝트로 Git에서 관리하고, 배포는 DACPAC 비교 스크립트로 합니다(운영 DB 직접 수정 금지).
- **문서화**: 모듈별 README, 설정 키 목록은 `TB_CONFIG.DESCRIPTION`에서 자동 생성, 외부 API는 OpenAPI 문서 자동 생성.

**테스트 구성** (도구는 모두 무료: xUnit, ASP.NET Core `WebApplicationFactory`, SQL Server Developer/LocalDB)

| 구분 | 대상 | 방법 | 실행 시점 |
| --- | --- | --- | --- |
| 단위 테스트 | 권한 평가 규칙, 비밀번호 정책, 암호화·마스킹, 다국어 대체 규칙, Service 업무 규칙 | 인터페이스를 수작업 테스트 더블(Fake)로 대체, DB 없이 실행 | 매 빌드 |
| SP 통합 테스트 | 모든 SP의 조회·저장·동시성·오류 코드 | 테스트 DB에 DACPAC 배포 → 기본 데이터 입력 → 테스트별 트랜잭션 롤백으로 격리 | 단계 종료 시, 배포 전 (CI 수동 실행) |
| 웹 통합 테스트 | 로그인, 권한 차단(403), CSRF, 보안 헤더, API 응답 형식 | `WebApplicationFactory`로 앱을 메모리에 띄우고 테스트 인증 핸들러로 사용자·역할 주입 | 매 빌드(CI) |
| 아키텍처 테스트 | 모듈 참조 방향, Controller의 권한 특성 누락, 인라인 SQL 사용 여부 | 리플렉션 기반 자체 검사 | 매 빌드 |
| 권한 매트릭스 테스트 | 코드의 `[Program]`/`[Func]`와 `TB_PROGRAM_API`가 같은지, 역할별로 모든 경로의 허용/차단이 역할 권한 설정과 일치하는지, 경로마다 등록된 기능 없이는 403인지 | `TB_PROGRAM_API`를 테스트 DB에서 읽어 케이스 자동 생성, `WebApplicationFactory`로 실제 요청 | 단계 종료 시, 배포 전 (CI 수동 실행) |
| E2E (선택) | 표준 화면 8종 시나리오 | Playwright for .NET (Apache 2.0, OSS 허용 후보) | 릴리스 전 |

**기준**: 프레임워크 핵심 프로젝트(Core, Data, Security) 단위 테스트 라인 커버리지 80% 이상, 모든 SP는 최소 1개 통합 테스트를 갖습니다. 신규 화면은 표준 예제와 함께 제공되는 테스트 템플릿을 복사해 작성합니다. Mock 라이브러리는 OSS 최소화를 위해 쓰지 않고 수작업 Fake를 프레임워크가 제공합니다(xUnit은 테스트 전용이라 배포물에 포함되지 않음).

## 15. 개발 로드맵과 다음 단계

프레임워크는 기반 → 연계 → 업무 공통 → 안정화 순서로 만들며, 각 단계는 관문을 통과해야 다음 단계로 넘어갑니다. 기간은 투입 인력이 정해지면 확정합니다.

&#91;embedded content: 개발 로드맵 · 4단계, 단계별 관문\]

1단계가 끝나면 업무 화면 개발을 병행할 수 있습니다. 표준 화면·권한·SP 템플릿이 이때 갖춰지기 때문입니다.

**단계 조정 (2026-10-22)**: AD SSO(도메인 PC 자동 로그인)와 AD 조직 동기화(퇴사자 자동 잠금 포함)는 2단계에서 3단계로 옮깁니다. 2단계는 파일 업로드·다운로드, 엑셀 업/다운로드, 협력사 계정 가입·승인, 알림·메일, 배치 작업(Portal.Worker)을 만들고, 관문은 파일·엑셀 보안 검사, 협력사 승인·메일 발송, 배치 작업 실행 검증입니다. 3단계 전까지 본사 직원도 ID/PW로 로그인합니다(`AUTH.MODE = FORM`).

**확정된 운영 조건** (2026-10-21 확인)

| 항목 | 결정 | 반영 위치 |
| --- | --- | --- |
| 서버 | 운영계·개발계 각 웹 1대 + DB 1대 | 1장, 3장 |
| 사용자 | 본사 + 다수 협력사 | 5장, 6장, 7장 |
| 언어 | 한국어, 영어, 중국어(간체로 가정) | 10장 |
| 전자결재 | 사내 전자결재 연동, 실제 개발 시 적용 | 11장 |
| HTML→PDF | 서버에 MS Edge 설치 | 2장, 11장 |
| 개인정보 | 저장하지 않음이 원칙, 불가피하면 암호화 정책 적용 | 7장, 9장 |
| 접속기록 | 10년 보관 | 7장, 9장 |

**업무 ERD·데이터 사전 반영 시점**: 1단계 마무리 시점(표준 화면·SP 템플릿·명명 규칙 확정 직후)에 기존 업무 ERD를 받아 명명 규칙, 회사 코드·감사 컬럼, 회사 간 공개/격리 기준에 맞는지 검토하고, 3단계 착수 전에 확정합니다.

## 16. 백업·복구 절차

운영 DB가 1대이므로 백업이 유일한 복구 수단입니다. 목표는 데이터 손실 최대 15분(RPO), 서비스 복구 4시간 이내(RTO)이며, 이 값은 초안이므로 운영 부서와 확정합니다. 이 장은 운영·유지보수 담당자가 그대로 따라 할 수 있도록 작성했습니다.

**백업 대상과 주기**

| 대상 | 방식 | 주기 | 보관 |
| --- | --- | --- | --- |
| 운영 DB (복구 모델 FULL) | 전체 백업 | 매주 일요일 02:00 | 5주 + 월말 전체 1년 |
| 운영 DB | 차등 백업 | 매일 02:00 (일요일 제외) | 14일 |
| 운영 DB | 트랜잭션 로그 백업 | 15분마다 | 14일 |
| 접속기록(LOG 읽기 전용 파일그룹) | 파일그룹 백업 | 읽기 전용 전환 시 1회 | 10년 (별도 매체) |
| 시스템 DB (master, msdb) | 전체 백업 | 매일 | 30일 |
| 백업 암호화 인증서·개인 키 | 파일 내보내기 | 생성·갱신 시 | 영구 (금고/별도 보관) |
| 첨부파일 저장소 | 파일 백업 (주간 전체 + 일간 증분) | 매일 03:00 | 30일 + 월말 1년 |
| 웹서버 설정 (`appsettings.Production.json`, Data Protection 키, IIS 구성) | 파일 백업 | 변경 시 + 매주 | 1년 |
| 프로그램 | GitLab 릴리스 패키지로 재생성 | - | GitLab 보관 |

**백업 규칙**

- 모든 DB 백업은 `WITH COMPRESSION, CHECKSUM, ENCRYPTION`으로 만듭니다. 암호화 인증서가 없으면 복구가 불가능하므로 인증서 백업 파일과 암호를 서로 다른 곳에 보관합니다.
- 백업 파일은 ① DB 서버의 별도 볼륨 ② 별도 백업 서버 또는 NAS(1시간마다 복사) ③ 월 1회 외부 매체 세 곳에 둡니다. DB 서버가 통째로 죽어도 ②에서 복구할 수 있어야 합니다.
- 작업은 SQL Server Agent 작업(자체 T-SQL 스크립트, GitLab `db/ops` 폴더에서 관리)으로 실행하고, 실패 시 Database Mail로 담당자에게 즉시 메일을 보냅니다.
- 운영 배포로 DB를 변경하기 직전에는 `COPY_ONLY` 전체 백업을 별도로 만듭니다(백업 체인에 영향 없음).

**일일 점검 (운영 담당자, 매일 오전)**

- [ ] 전날 전체/차등/로그 백업 작업 성공 (관리자 화면 "운영 모니터링 > 백업 현황" 또는 Agent 작업 기록)
- [ ] 백업 파일이 백업 서버/NAS에 복사됨
- [ ] DB·백업·로그 디스크 여유 공간 20% 이상
- [ ] 파일 저장소 백업 성공

**장애 유형별 복구 절차**

1. **DB 장애 (DB 손상, 디스크 고장)**
   1. 웹서버 사이트 루트에 `app_offline.htm`을 두어 점검 화면을 표시하고, 장애 사실을 공지합니다.
   2. DB에 접근이 가능하면 최종 로그(tail-log) 백업을 먼저 받습니다. 손실 구간이 0에 가까워집니다.
   3. 최신 전체 → 최신 차등 → 그 이후 로그 백업을 순서대로 `NORECOVERY`로 복원하고, 마지막에 `RECOVERY`로 엽니다(아래 스크립트).
   4. `DBCC CHECKDB`로 무결성을 확인합니다.
   5. 애플리케이션 계정의 DB 사용자 연결을 확인합니다(다른 서버에 복원했다면 `ALTER USER ... WITH LOGIN`).
   6. `app_offline.htm`을 없애고 확인 목록(로그인, 메뉴 표시, 조회 1건, 저장 1건, 첨부 다운로드)을 점검합니다.
   7. 복구 시점·손실 구간을 장애 보고서에 기록합니다.
2. **실수로 데이터를 삭제·변경한 경우**: 운영 DB를 덮어쓰지 않고, 다른 이름(`PORTAL_RESTORE`)으로 사고 직전 시점까지 복원(`STOPAT`)한 뒤 필요한 행만 비교해 되돌립니다. 감사 로그의 변경 전 값으로 범위를 먼저 확인합니다.
3. **웹서버 장애**: ① 서버 구성서대로 Windows Server 2022, IIS, .NET 10 Hosting Bundle, Microsoft Edge, 한국어·중국어 글꼴을 설치 ② GitLab의 마지막 운영 릴리스 패키지 배포 ③ 설정 파일, Data Protection 키, 첨부파일 복원 ④ 확인 목록 점검. Data Protection 키를 잃으면 모든 사용자가 다시 로그인해야 하고 암호화된 설정값을 다시 입력해야 합니다.
4. **배포 실패**: 17장의 롤백 절차를 따릅니다.

**복원 스크립트 예시**

```sql
-- 0) (가능하면) 최종 로그 백업
BACKUP LOG PORTAL TO DISK = N'E:\Backup\PORTAL_tail.trn' WITH NORECOVERY, NO_TRUNCATE, CHECKSUM;

-- 1) 전체 → 2) 차등 → 3) 로그 (순서대로)
RESTORE DATABASE PORTAL FROM DISK = N'E:\Backup\PORTAL_FULL_20261018.bak' WITH NORECOVERY, REPLACE, CHECKSUM;
RESTORE DATABASE PORTAL FROM DISK = N'E:\Backup\PORTAL_DIFF_20261021.bak' WITH NORECOVERY, CHECKSUM;
RESTORE LOG PORTAL FROM DISK = N'E:\Backup\PORTAL_LOG_20261021_0215.trn' WITH NORECOVERY;
-- ... 이후 로그 파일들 ...
RESTORE LOG PORTAL FROM DISK = N'E:\Backup\PORTAL_tail.trn'
  WITH RECOVERY;            -- 특정 시점까지만: WITH STOPAT = '2026-10-21 10:29:00', RECOVERY

DBCC CHECKDB (PORTAL) WITH NO_INFOMSGS;
```

어느 파일을 어떤 순서로 복원할지는 `msdb.dbo.backupset` 조회 스크립트(`db/ops/restore-plan.sql`)가 자동으로 목록을 만들어 줍니다. 백업 서버에만 파일이 남은 경우는 `RESTORE HEADERONLY`로 파일별 시점을 확인합니다.

**복구 훈련**: 분기 1회 운영 백업을 개발계에 복원하고 마스킹 스크립트를 실행합니다. 걸린 시간과 문제점을 기록해 RTO를 검증하고, 개발계 데이터 갱신도 함께 해결합니다. 훈련 기록은 운영 매뉴얼에 남깁니다.

## 17. 배포·형상관리 (GitLab)

소스, DB 스키마, 운영 스크립트, 문서를 모두 GitLab에서 관리하고, 개발계 배포는 자동, 운영계 배포는 릴리스 태그 + 수동 승인으로만 합니다. 사람이 서버에 직접 파일을 올리거나 운영 DB를 직접 고치는 일은 없습니다.

**레포지토리 구성**: 처음에는 레포지토리 하나(`portal`)에 4장의 솔루션 구조를 그대로 두고, `docs/`(개발 표준 가이드, 운영 매뉴얼)와 `db/ops/`(백업·복원·점검 스크립트)를 추가합니다. 다른 업무 시스템이 프레임워크를 재사용하는 시점에 Core/Data/Security/Infrastructure/Framework.Web을 별도 레포지토리로 분리해 GitLab Package Registry의 내부 NuGet 패키지로 배포합니다. 프로젝트 경계를 처음부터 나눠두는 이유가 이것입니다.

**브랜치 전략**

| 브랜치 | 용도 | 병합 규칙 | 배포 |
| --- | --- | --- | --- |
| `main` | 운영 반영 완료 코드 | 보호 브랜치, `release/*`·`hotfix/*` MR만 | 태그 `vX.Y.Z` → 운영계(수동 승인) |
| `develop` | 다음 릴리스 통합 | 보호 브랜치, MR + 리뷰 1명 + 파이프라인 성공 | 개발계 자동 |
| `feature/<이슈번호>-<요약>` | 기능 개발 | `develop`으로 MR | - |
| `release/X.Y` | 운영 배포 준비(개발계 검수) | `main`과 `develop`에 병합 | - |
| `hotfix/<이슈번호>` | 운영 긴급 수정 | `main`과 `develop`에 병합 | 태그 → 운영계 |

버전은 유의적 버전(`주.부.수`)을 따르고, 실행 중인 버전은 화면 하단과 `/health`에 표시합니다. 커밋 메시지와 MR 제목에는 이슈 번호를 넣어 릴리스 노트를 자동으로 만듭니다.

**CI/CD 파이프라인** (GitLab Runner를 Windows 서버에 shell executor로 설치)

| 단계 | 내용 | 실패 시 |
| --- | --- | --- |
| build | `dotnet build`(분석기 경고 = 오류), DB 프로젝트 → DACPAC | MR 병합 불가 |
| test | 단위 테스트, 아키텍처 테스트, 웹 통합 테스트(DB 없이), 커버리지 보고서. 테스트 DB에 DACPAC 배포 후 SP 통합 테스트는 단계 종료 시·배포 전에 수동 실행 | MR 병합 불가 |
| security | `dotnet list package --vulnerable`, 허용 목록 외 패키지 검사, 비밀값 커밋 검사 | MR 병합 불가 |
| package | `dotnet publish` 결과 + DACPAC + 변경 SQL 스크립트를 릴리스 아티팩트로 보관 | - |
| deploy-dev | `develop` 병합 시 개발계 자동 배포 + 확인 목록 자동 점검 | 알림 |
| deploy-prod | `vX.Y.Z` 태그 시 대기, 승인자가 수동 실행 | 롤백 |

운영 연결 문자열·비밀값은 레포지토리에 넣지 않고 서버의 `appsettings.Production.json`에만 둡니다. 파이프라인에 필요한 값은 GitLab CI/CD 변수(Protected + Masked)로 둡니다.

**운영 배포 절차** (서버가 1대이므로 정기 점검 시간에 실행, 예: 매주 수요일 19:00. 공지 팝업으로 미리 안내)

1. 릴리스 담당자가 변경 SQL 스크립트(`SqlPackage /Action:Script`로 생성)를 검토합니다. 데이터 손실 경고가 있으면 중단합니다.
2. 운영 DB `COPY_ONLY` 전체 백업을 받습니다.
3. `app_offline.htm`을 두고 앱 풀을 멈춥니다.
4. 현재 배포 폴더를 `releases\<이전버전>`으로 보관하고, 새 패키지를 `releases\<새버전>`에 풀어 IIS 사이트 경로를 전환합니다.
5. 검토한 DB 변경 스크립트를 적용합니다.
6. 앱 풀을 시작하고 확인 목록(16장)을 점검한 뒤 `app_offline.htm`을 없앱니다.
7. GitLab 릴리스에 배포 일시·담당자·결과를 기록합니다.

**롤백**: 프로그램만 문제면 IIS 사이트 경로를 이전 버전 폴더로 되돌립니다(1분 이내). DB 변경까지 되돌려야 하면 2단계의 백업으로 복원합니다. 그래서 DB 변경은 가능한 한 컬럼 추가·SP 추가처럼 이전 버전 프로그램과도 호환되도록 만들고, 컬럼 삭제는 다음 릴리스로 미룹니다.

**관련 문서**: 코딩·화면·SP 작성 규칙은 별도 문서인 [포털 프레임워크 개발 표준 가이드](dev-guide.md)에, 초기 역할·코드·설정값은 [포털 초기 데이터 정의서](initial-data.md)에 둡니다. 두 문서는 구축이 끝난 뒤 `docs/` 폴더에 Markdown으로도 내보내 레포지토리와 함께 관리합니다.
