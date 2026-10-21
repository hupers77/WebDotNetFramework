# WebDotNetFramework

.NET 10 / Windows Server 2022 / MSSQL 2022 기반 엔터프라이즈 웹 포털 프레임워크입니다.

## 문서

| 문서 | 내용 |
|---|---|
| [설계서](docs/framework-design.md) | 아키텍처, 보안·인증, 권한, DB 표준, 로그 보관, 백업·복구, 배포, 테스트 전략 |
| [개발 표준 가이드](docs/dev-guide.md) | 유지보수 개발자를 위한 코딩·화면·SP 작성 규칙과 절차 |
| [초기 데이터 정의서](docs/initial-data.md) | 초기 역할, 메뉴, 공통코드, 설정 키, 배치 작업 |

## 빌드와 테스트

.NET 10 SDK만 있으면 됩니다(`global.json`으로 버전 고정).

```powershell
dotnet build Portal.sln      # 분석기 경고도 오류로 처리
dotnet test Portal.sln       # 단위 + 아키텍처 테스트
dotnet run --project src/Portal.Web   # https://localhost:5001/health
```

## 솔루션 구조 (구축 진행 중)

| 프로젝트 | 역할 | 상태 |
|---|---|---|
| `src/Portal.Core` | 공통 계약: `ApiResult`, `PageResult`, `BizException`, 설정·메시지·기능 키, `ICurrentUser` | 1단계 |
| `src/Portal.Data` | `ISpExecutor`(SP 전용 실행기), TVP, 결과 매퍼, SP 오류 변환, 트랜잭션 범위 | 1단계 |
| `src/Portal.Web` | 웹 호스트 (`/health`) | 1단계 |
| `tests/Portal.Testing` | 테스트 공용 Fake (`FakeSpExecutor`, `FakeCurrentUser`, `FakeTimeProvider`) | 1단계 |
| `tests/Portal.UnitTests` | 단위 테스트 + 아키텍처 테스트(참조 방향, OSS 허용 목록, 금지 패턴) | 1단계 |

나머지 프로젝트(Security, Infrastructure, Framework.Web, Modules.*, Worker, db/)는 설계서 15장 로드맵 순서로 추가됩니다.

## 규칙을 강제하는 장치

- `Directory.Build.props`: Nullable, 분석기(Recommended), 경고 = 오류
- `Directory.Packages.props` + `eng/allowed-packages.txt`: 중앙 버전 관리, 허용 목록 밖 패키지 금지
- `tests/Portal.UnitTests/Architecture`: 모듈 참조 방향, EF Core 금지, 인라인 SQL·`DateTime.Now`·`Html.Raw`·`innerHTML` 금지
- CI: `.github/workflows/ci.yml`(현재), `.gitlab-ci.yml`(운영 GitLab 이전용)
