# WebDotNetFramework

.NET 10 / Windows Server 2022 / MSSQL 2022 기반 엔터프라이즈 웹 포털 프레임워크입니다. 현재 버전 **`fw-v1.5.0`** ([기술 스펙](docs/tech-spec.md)).

## 문서

| 문서 | 내용 |
|---|---|
| [설계서](docs/framework-design.md) | 아키텍처, 보안·인증, 권한, DB 표준, 로그 보관, 백업·복구, 배포, 테스트 전략 |
| [개발 표준 가이드](docs/dev-guide.md) | 유지보수 개발자를 위한 코딩·화면·SP 작성 규칙과 절차 |
| [초기 데이터 정의서](docs/initial-data.md) | 초기 역할, 메뉴, 공통코드, 설정 키, 배치 작업 |
| [업무 개발 이관 문서](docs/handover/README.md) | 별도 업무 저장소에서 개발할 때 읽을 요약: 프레임워크 사용법·구현 현황, 업무(경비대 반출입) 분석 내용, 새 업무 저장소 시작·업데이트 방식, CLAUDE.md 초안 |
| [기술 스펙 (버전 표기)](docs/tech-spec.md) | 현재 프레임워크 버전, 서버·개발 환경·패키지·화면 라이브러리의 실제 버전 |
| [보안 점검 보고서 (2026-10)](docs/security/2026-10-security-audit.md) | KISA 보안약점 49개·주통기반 웹 21개 항목·OWASP Top 10 자가진단, 발견 취약점과 조치, 운영 권고 |
| [보안 점검 반영 방법 (업무 저장소용)](docs/security/apply-to-business-repos.md) | 프레임워크에서 시작한 업무 저장소가 이번 보안 패치를 받는 순서와 직접 고칠 곳 |
| [보안 점검 skill `web-security-audit`](.claude/skills/web-security-audit/README.md) | 같은 점검을 다른 PC·다른 사내 시스템에서 Claude Code로 하는 방법, 점검 스크립트 |

## 빌드와 테스트

.NET 10 SDK 10.0.114 이상만 있으면 됩니다(`global.json`으로 버전 고정). 버전 전체는 [기술 스펙](docs/tech-spec.md).

```powershell
dotnet build Portal.sln                                   # 분석기 경고도 오류로 처리
dotnet test Portal.sln --filter "Category!=Integration"   # 단위 + 아키텍처 테스트 (DB 불필요)
./db/ops/local-setup.ps1 -Server localhost -Database PORTAL_LOCAL   # 로컬 DB 생성·배포 (SQL Server 2022 Developer)
dotnet test tests/Portal.IntegrationTests                 # SP·데이터 범위·DB 권한 통합 테스트 (PORTAL_TEST_DB 또는 PORTAL_LOCAL)
dotnet run --project src/Portal.Web                       # https://localhost:5001/health
```

## 솔루션 구조

| 프로젝트 | 역할 |
|---|---|
| `src/Portal.Core` | 공통 계약: `ApiResult`, `PageResult`, `BizException`, 설정·메시지·기능 키, `ICurrentUser` |
| `src/Portal.Data` | `ISpExecutor`(SP 전용 실행기), TVP, 결과 매퍼, SP 오류 변환, 트랜잭션 범위 |
| `src/Portal.Security` | 로그인·세션·비밀번호·2단계 인증·권한 평가, LDAP 조회 모듈 |
| `src/Portal.Infrastructure` | 캐시, 설정, 다국어, 로그, 파일, 엑셀, HTML 필터, 메일 큐·발송, PDF, 스케줄러 |
| `src/Portal.Framework.Web` | 컨트롤러 기반, 권한 필터, 세션 인증, 보안 헤더, 접근 로그, 외부 API 관문, 셸 레이아웃 |
| `src/Portal.Web` | 웹 호스트와 공통 화면(시스템 관리, 협력사, 로그, 레포트, 게시판, 대시보드), `web.config` |
| `src/Portal.Worker` | 배치 Windows 서비스 (등록된 스케줄러 작업 실행) |
| `tests/Portal.Testing` | 테스트 공용 Fake (`FakeSpExecutor`, `FakeCurrentUser`, `FakeTimeProvider`) |
| `tests/Portal.UnitTests` | 단위 테스트 + 아키텍처 테스트(참조 방향, OSS 허용 목록, 금지 패턴) |
| `tests/Portal.IntegrationTests` | 웹 화면 테스트와 SQL Server 통합 테스트(`Category=Integration`, 테스트마다 롤백) |
| `db/Portal.Database` | SQL 프로젝트(DACPAC): FW·LOG 스키마 테이블, 함수, SP, 권한 역할 `portal_app` |
| `db/seed/FW` | 초기 데이터 `MERGE` 스크립트 (배포 후 자동 실행, 반복 실행 가능) |
| `db/ops` | DB 운영 스크립트 (로컬 DB 설치) |
| `.claude/skills/web-security-audit` | 보안 점검 skill과 점검 스크립트 |

## 규칙을 강제하는 장치

- `Directory.Build.props`: Nullable, 분석기(Recommended), 경고 = 오류
- `Directory.Packages.props` + `eng/allowed-packages.txt`: 중앙 버전 관리, 허용 목록 밖 패키지 금지
- `tests/Portal.UnitTests/Architecture`: 모듈 참조 방향, EF Core 금지, 인라인 SQL·`DateTime.Now`·`Html.Raw`·`innerHTML` 금지
- CI: `.github/workflows/ci.yml`(현재), `.gitlab-ci.yml`(운영 GitLab 이전용)

## 작업 기록

프레임워크에서 시작한 업무 저장소는 프레임워크를 병합할 때 이 기록과 해당 문서를 보고 할 일을 확인합니다(이관 문서 [04](docs/handover/04-new-business-repo.md) 7장). 버전을 바꾸는 PR은 같은 PR에서 이 표와 [기술 스펙](docs/tech-spec.md)을 고칩니다. 최신이 위입니다.

| 날짜 | 버전 | 내용 | 업무 저장소가 할 일 |
| --- | --- | --- | --- |
| 2026-11-05 | `fw-v1.5.0` | 메일 보내는 사람 지정(업무 저장소 요청서 2): `FW.USP_MSG_QUEUE_SAVE`에 선택 파라미터 `@P_FROM_USER_ID`(발송 때 그 사용자의 업무 이메일 사용; `TB_MSG_QUEUE.FROM_USER_ID` 추가). 새 설정 `MAIL.FROM_NAME`(보내는 사람 표시 이름)과 `MAIL.ALLOW_USER_FROM`(기본 N: 보내는 주소는 `MAIL.FROM` 그대로, 그 사용자 주소는 Reply-To; Y: 그 사용자 주소를 보내는 주소로 사용하려면 메일 서버가 허용해야 함). 템플릿별 표시 이름 `TB_MSG_TEMPLATE.FROM_NAME`(메시지 템플릿 화면 입력, `USP_MSG_TEMPLATE_SAVE`에 선택 파라미터 `@P_FROM_NAME`). `MailMessageData`에 `FromAddress`/`FromName`/`ReplyTo`(선택). 파라미터를 안 주는 기존 호출은 그대로. 어셈블리 버전 1.5.0 | 병합 후 DB 재배포. 반출불가 처리 등에서 `FW.USP_MSG_QUEUE_SAVE`를 `@P_FROM_USER_ID = 처리한 경비원 ID`로 부름. 사이트에서 `MAIL.FROM_NAME`(예: 통합반출시스템)을 입력하고, 메일 서버가 사용자 주소로 보내기를 허용하는지 확인한 뒤에만 `MAIL.ALLOW_USER_FROM = Y` (허용 안 하면 N으로 두면 됨) |
| 2026-11-05 | `fw-v1.4.0` | AD 로그인 확장 지점(업무 저장소 요청서 1): `IExternalCredentialValidator`(`Portal.Security.Authentication`)를 DI에 등록하면 그 검증기가 맡는 사용자(`CanHandleAsync`)는 포털 비밀번호 해시 대신 검증기로 비밀번호를 확인(`PWD_HASH` NULL 허용). 검증기 서버 장애(`ExternalCredentialResult.Unavailable` 또는 예외)는 실패 횟수에 넣지 않고 `AUTH.EXTERNAL_UNAVAILABLE` 안내. AD 로그인 세션은 `AUTH_METHOD_CD` = `AD`/`AD_MFA`로 남고 비밀번호 변경을 강제하지 않음. 새 설정 `AUTH.AD_MFA`(기본 Y): AD 로그인 사용자의 2단계 인증(협력사 설정 `AUTH.PARTNER_MFA`는 AD 로그인에 적용 안 함, 관리자 역할 `AUTH.ADMIN_MFA`는 따로 유지). `AUTH.MODE = FORM`이면 검증기를 쓰지 않음. `USP_AUTH_SESSION_CHECK_EXEC`가 `AUTH_METHOD_CD`를 추가로 돌려줌. 로그인 화면이 ID·비밀번호를 브라우저에서 RSA-OAEP로 암호화해 보냄(`Enc`, 개발자도구에 평문 안 보임; WebCrypto가 없는 환경(HTTP)은 기존 방식). 어셈블리 버전 1.4.0 | 병합 후 DB 재배포(시드: `AUTH.AD_MFA`, 문구 `AUTH.EXTERNAL_UNAVAILABLE`). 업무 저장소에서 `IExternalCredentialValidator` 구현(LDAP bind, `TB_USER_LOGIN_MAP`의 `PROVIDER_CD='AD'`로 대상 판단)을 `IServiceCollection`에 등록하고 `AUTH.AD_MFA`를 사이트 정책에 맞게 설정. 로그인 화면에 `Enc` 필드가 추가됐으므로 업무 저장소가 로그인 뷰를 복사해 쓰고 있었다면 프레임워크 것으로 교체 |
| 2026-11-03 | `fw-v1.3.0` | 메뉴에 있었지만 주소가 404이던 표준 화면 6종 구현: 내 정보(`/My/Profile`, 화면 언어 변경), 메뉴 관리(`/Sys/Menu`, 트리·언어별 메뉴명), 다국어 문구 관리(`/Sys/Lang`, 키 단위 편집·엑셀 내보내기/업로드), 메시지 템플릿(`/Sys/MsgTemplate`), 레포트 관리(`/Rpt/Report`), 운영 모니터링(`/Sys/Monitor`). 새 SP 10개(`USP_MY_PROFILE_GET`, `USP_MENU_ADMIN_GET_LIST`·`USP_MENU_SAVE`, `USP_LANG_RESOURCE_ADMIN_GET_LIST`·`USP_LANG_RESOURCE_SAVE`, `USP_MSG_TEMPLATE_ADMIN_GET_LIST`·`USP_MSG_TEMPLATE_SAVE`, `USP_REPORT_ADMIN_GET_LIST`·`USP_REPORT_SAVE`, `USP_MONITOR_GET`), `TB_PROGRAM_API` 18행, 다국어 문구 약 120키(ko/en/zh-CN). 그리드 입력 칸에 `list`(입력 후보) 옵션 추가. 어셈블리 버전 1.3.0 | 병합 후 DB를 다시 배포(시드가 경로·문구를 채움)하고 `PORTAL_ADMIN`으로 6개 화면을 열어 확인. 업무 저장소가 같은 이름의 컨트롤러/뷰를 이미 만들었다면 프레임워크 것으로 교체. 메뉴·다국어 문구를 업무 저장소 시드에서 더하던 곳은 이제 화면으로도 관리 가능 |
| 2026-10-31 | `fw-v1.2.1` | 업무 저장소 DB 통합 테스트에서 드러난 수정(PR #68): 쪽 나눔 없는 목록 SP를 `QueryPageAsync`로 부르면 `P_PAGE_NO`·`P_PAGE_SIZE`가 없어 오류가 나던 문제(휴일·게시판 관리·위젯 관리·API 클라이언트·송신 대상 관리 화면 목록), 레포트 요청 삭제 직후 최대 1초 다시 보이던 문제, `TB_PROGRAM_API` 시드의 화면 경로 24행 누락, 업무 저장소가 역할·프로그램·메뉴를 더해도 깨지지 않도록 통합 테스트 정리. 어셈블리 버전 1.2.1 | 병합 후 `db/ops/local-setup.ps1`로 새 DB를 만들어 통합 테스트 확인. 쪽 나눔 없는 목록 SP를 부르는 서비스가 있으면 `P_PAGE_NO`·`P_PAGE_SIZE` 없이 불러도 됨(둘 다 주는 기존 호출은 그대로). 기존 테스트 DB에 이전 실행이 남긴 테스트 데이터(`t-new`, `T_ERP`)가 있으면 지움 |
| 2026-10-31 | `fw-v1.2.0` | 보안 점검(KISA·주통기반·OWASP)과 패치(PR #66): 게시판 댓글·첨부의 기록 단위 권한, 첨부 묶음 가로채기 차단, HTML 필터 서비스 거부 수정, .NET 10.0.14 보안 업데이트, IIS `web.config`, 오류·버전 노출 정리. 업무 저장소 반영 안내, 기술 스펙 문서, 보안 점검 skill 추가, 어셈블리 버전을 태그와 맞춤(1.2.0)(PR #67) | [보안 점검 반영 방법](docs/security/apply-to-business-repos.md) 1~5장. 댓글 SP·`IBoardService` 직접 호출, 첨부를 저장하는 업무 SP 확인. 서버에 Hosting Bundle 10.0.14 설치 |
| 2026-10-30 | `fw-v1.1.0` | 화면 디자인 개편, 게시판 서식 편집기·본문 사진, 대시보드 차트 위젯(PR #65) | 병합 후 화면 확인 |
| 2026-10-23 | `fw-v1.0.0` | 1단계(프레임워크) 완료(PR #64까지): 인증·권한·메뉴·공통코드·다국어·설정·로그, 게시판·파일·PDF·레포트·대시보드·외부 API·알림·스케줄러, LDAP 조회 모듈, 업무 개발 이관 문서 | 업무 저장소 시작 기준 버전 |
