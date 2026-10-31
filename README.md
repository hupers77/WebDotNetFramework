# WebDotNetFramework

.NET 10 / Windows Server 2022 / MSSQL 2022 기반 엔터프라이즈 웹 포털 프레임워크입니다. 현재 버전 **`fw-v1.2.1`** ([기술 스펙](docs/tech-spec.md)).

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
| 2026-10-31 | `fw-v1.2.1` | 업무 저장소 DB 통합 테스트에서 드러난 수정(PR #68): 쪽 나눔 없는 목록 SP를 `QueryPageAsync`로 부르면 `P_PAGE_NO`·`P_PAGE_SIZE`가 없어 오류가 나던 문제(휴일·게시판 관리·위젯 관리·API 클라이언트·송신 대상 관리 화면 목록), 레포트 요청 삭제 직후 최대 1초 다시 보이던 문제, `TB_PROGRAM_API` 시드의 화면 경로 24행 누락, 업무 저장소가 역할·프로그램·메뉴를 더해도 깨지지 않도록 통합 테스트 정리. 어셈블리 버전 1.2.1 | 병합 후 `db/ops/local-setup.ps1`로 새 DB를 만들어 통합 테스트 확인. 쪽 나눔 없는 목록 SP를 부르는 서비스가 있으면 `P_PAGE_NO`·`P_PAGE_SIZE` 없이 불러도 됨(둘 다 주는 기존 호출은 그대로). 기존 테스트 DB에 이전 실행이 남긴 테스트 데이터(`t-new`, `T_ERP`)가 있으면 지움 |
| 2026-10-31 | `fw-v1.2.0` | 보안 점검(KISA·주통기반·OWASP)과 패치(PR #66): 게시판 댓글·첨부의 기록 단위 권한, 첨부 묶음 가로채기 차단, HTML 필터 서비스 거부 수정, .NET 10.0.14 보안 업데이트, IIS `web.config`, 오류·버전 노출 정리. 업무 저장소 반영 안내, 기술 스펙 문서, 보안 점검 skill 추가, 어셈블리 버전을 태그와 맞춤(1.2.0)(PR #67) | [보안 점검 반영 방법](docs/security/apply-to-business-repos.md) 1~5장. 댓글 SP·`IBoardService` 직접 호출, 첨부를 저장하는 업무 SP 확인. 서버에 Hosting Bundle 10.0.14 설치 |
| 2026-10-30 | `fw-v1.1.0` | 화면 디자인 개편, 게시판 서식 편집기·본문 사진, 대시보드 차트 위젯(PR #65) | 병합 후 화면 확인 |
| 2026-10-23 | `fw-v1.0.0` | 1단계(프레임워크) 완료(PR #64까지): 인증·권한·메뉴·공통코드·다국어·설정·로그, 게시판·파일·PDF·레포트·대시보드·외부 API·알림·스케줄러, LDAP 조회 모듈, 업무 개발 이관 문서 | 업무 저장소 시작 기준 버전 |
