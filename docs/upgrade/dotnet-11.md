# .NET 11 이관 안내 (`fw-v1.7.0` → `fw-v1.8.0`)

작성: 2026-11-25. 대상: 프레임워크에서 시작한 업무 저장소 개발자, 개발 PC·GitLab 러너·웹 서버 담당자.

`fw-v1.8.0`은 기능 변경 없이 플랫폼만 .NET 10에서 .NET 11로 올린 버전입니다. **DB 변경(SP·표·시드)이 없고**, 화면·공통 JS·설정 키도 그대로입니다. 그래서 업무 저장소는 병합 후 빌드 경고만 정리하면 되고, 서버에는 .NET 11 Hosting Bundle만 더 설치하면 됩니다.

## 1. 먼저 알아 둘 점: 지원 기간

| 버전 | 종류 | 출시 | 지원 종료 | 2026-11-25 기준 최신 패치 |
| --- | --- | --- | --- | --- |
| .NET 10 | LTS | 2025-11-11 | **2028-11-14** | 10.0.15 (2026-11-10) |
| .NET 11 | STS (24개월) | 2026-11-10 | **2028-11-14** | 11.0.0 (2026-11-10) |

출처: [.NET 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [STS 24개월 지원 발표](https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/), [.NET 11 출시 발표](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/).

- .NET 11로 올려도 **지원 종료일은 .NET 10과 같습니다**(둘 다 2028-11-14). 올리는 이점은 최신 런타임 성능, C# 15, 새 API이고, 지원 기간이 늘지는 않습니다.
- 다음 LTS는 .NET 12(2027년 11월 예정)입니다. 2028-11-14 전에 .NET 12로 한 번 더 옮겨야 합니다.
- 11.0.0은 첫 출시판입니다. 첫 보안 패치(11.0.1)는 매월 둘째 화요일 패치일(2026-12-08 예상)에 나올 가능성이 높습니다. 나오면 [기술 스펙](../tech-spec.md) 7장 절차대로 패키지·SDK·Hosting Bundle을 함께 올립니다.

## 2. 프레임워크에서 바뀐 파일

| 파일 | 변경 |
| --- | --- |
| `global.json` | SDK `10.0.114` → `11.0.100` (`rollForward: latestFeature` 그대로) |
| `Directory.Build.props` | `<TargetFramework>` `net10.0` → `net11.0`, `<Version>` `1.7.0` → `1.8.0` |
| `Directory.Packages.props` | Microsoft 패키지 4개 `10.0.14` → `11.0.0` (`Microsoft.Extensions.Hosting.WindowsServices`, `System.DirectoryServices.Protocols`, `System.Security.Cryptography.Pkcs`, `Microsoft.AspNetCore.Mvc.Testing`). 그 밖의 패키지(SqlClient 7.1.1, OpenXml, QRCoder, xunit 등)는 그대로 |
| `tests/Portal.UnitTests/**` (10개 파일) | 새 분석기 규칙 CA2026: `JsonDocument.Parse(x).RootElement` → `JsonElement.Parse(x)` |
| `.gitlab-ci.yml`, `.github/workflows/ci.yml`, `db/ops/local-docker.sh`, `db/ops/local-setup.ps1` | SqlPackage 실행 때만 `DOTNET_ROLL_FORWARD=Major` (아래 3장 "SqlPackage") |

검증: .NET SDK 11.0.100으로 `dotnet build Portal.sln -c Release`(경고 = 오류) 성공, DB 프로젝트 빌드 성공, 단위·웹 테스트 1,134건 통과(`Portal.UnitTests` 882, `Portal.IntegrationTests`의 DB 없는 테스트 252), 취약 패키지 0건. DB 통합 테스트와 Windows Server·IIS 실제 기동은 이 작업에서 하지 않았습니다(5장).

## 3. .NET 10 → 11 변경점과 영향

공식 목록: [.NET 11 호환성이 깨지는 변경](https://learn.microsoft.com/dotnet/core/compatibility/11), [ASP.NET Core 11 호환성이 깨지는 변경](https://learn.microsoft.com/aspnet/core/breaking-changes/11/overview). 공식 목록은 "작성 중"으로 표시돼 있어 이후 항목이 더 늘 수 있습니다. 아래는 이 프레임워크와 업무 저장소에 해당할 수 있는 것만 골랐습니다(MAUI·Blazor·macOS 등 해당 없는 항목 제외).

### 3.1 빌드가 깨질 수 있는 것 (업무 저장소에서 먼저 확인)

| 변경 | 프레임워크 | 업무 저장소가 할 일 |
| --- | --- | --- |
| **새 분석기 규칙 CA2026**: `JsonDocument.Parse(...).RootElement`는 자원 누수라 `JsonElement.Parse(...)`를 쓰라는 경고. 경고 = 오류라 빌드 실패 | 테스트 11곳 수정 | 같은 패턴을 `JsonElement.Parse`로 바꿈. `using var doc = JsonDocument.Parse(...)`처럼 해제하는 코드는 그대로 둬도 됨 |
| **API 사용 중지(obsolete) 경고** (일반 라이브러리, 암호화) | 해당 없음(빌드 경고 0) | 빌드에서 `SYSLIB*` 경고가 나오면 경고 설명대로 새 API로 바꿈 |
| **Microsoft.Extensions 일부가 공유 프레임워크에 포함**: `Caching.Abstractions`, `Configuration.Abstractions`, `DependencyInjection.Abstractions`, `Diagnostics.Abstractions`, `FileProviders.Abstractions`, `Hosting.Abstractions`, `Logging.Abstractions`, `Options`, `Primitives`를 패키지로 참조하면 NU1510 경고 | 참조하지 않음 | 업무 프로젝트가 위 패키지를 참조하면 `PackageReference`와 `Directory.Packages.props` 줄을 지움 |
| **NuGet 복원이 전역 패키지 폴더에서 더 높은 버전을 찾지 않음** | 모든 버전이 nuget.org에 정확히 있음 | 사내 NuGet 미러를 쓰면 `Directory.Packages.props`의 정확한 버전(특히 Microsoft 패키지 `11.0.0`)이 미러에 있는지 확인 |
| **C# 15**: `LangVersion latest`라 자동으로 C# 15 | 깨지는 변경 없음 | 없음 |
| **Visual Studio**: `net11.0`은 Visual Studio 2026 최신 업데이트(또는 VS Code + C# 확장)가 필요. Visual Studio 2022로는 열리지 않음 | - | 개발 PC 도구 업데이트 |
| **SqlPackage**(DB 배포 도구 170.5.96)는 net8/net10용만 있어 .NET 11만 설치된 PC에서 "You must install or update .NET" 오류 | 실행 줄에만 `DOTNET_ROLL_FORWARD=Major`를 붙여 .NET 11에서 실행(확인함). 다른 방법은 .NET 10 런타임을 함께 설치 | 업무 저장소의 배포 스크립트·CI에 같은 설정을 붙임 |

### 3.2 동작이 바뀌는 것 (빌드는 되지만 결과가 다를 수 있음)

| 변경 | 프레임워크 영향 | 업무 저장소 확인 |
| --- | --- | --- |
| **BackgroundService가 예외로 멈추면 `IHost.RunAsync`도 예외를 던짐** (전에는 정상 종료, 종료 코드 0 → 이제 0이 아님) | 프레임워크 백그라운드 작업(`SchedulerService`, `CacheVersionMonitor`, 로그 기록기, `ReportRunWorker`)은 오류를 잡아 기록하므로 평소 영향 없음. 예상 못 한 오류로 멈추면 Portal.Worker 서비스가 "비정상 종료"로 기록됨 | 운영 서버에서 Windows 서비스 복구 옵션을 켜 두면 자동 재시작됨: `sc.exe failure PortalWorker reset= 86400 actions= restart/60000/restart/60000/restart/60000` |
| **double·float → decimal 변환이 정확히 반올림됨**. 예: `(decimal)1.23`(double) = `1.229999999999999982236431606` (전에는 `1.23`). 컴파일 시 상수로 계산되는 변환은 SDK 11로 빌드하면 대상 프레임워크와 상관없이 바뀜 | 레포트 숫자 표시(`ReportFormat`)는 소수 2자리 서식이라 화면 결과는 같음. DB 금액은 SQL `decimal`로 읽어 영향 없음 | 금액·수량 계산에 `double`을 거쳐 `decimal`로 바꾸는 코드가 있으면 `decimal` 리터럴(`1.23m`)과 `decimal` 연산으로 바꿈. 기대값을 박아 둔 테스트가 깨질 수 있음 |
| **`Math.Round(값, 자릿수)` 결과가 정확히 반올림됨** | 사용하지 않음 | 금액 반올림을 `double`로 하고 있으면 결과 끝자리가 달라질 수 있음. `decimal`로 반올림 |
| **`DateOnly`/`TimeOnly` `TryParse`가 잘못된 서식 문자나 `DateTimeStyles` 값이면 예외** (잘못된 입력 문자열은 전처럼 false) | 고정 서식 `yyyy-MM-dd` + `DateTimeStyles.None`만 씀. 영향 없음 | 서식 문자열을 설정·입력에서 받아 쓰는 곳만 확인 |
| **체인 구성(`AddConfiguration`)에서 빈 문자열이 앞 값을 덮어씀** | 사용하지 않음(`appsettings.json`의 `""` 기본값은 뒤의 `appsettings.Production.json`·환경 변수가 덮는 일반 구조라 해당 없음) | `AddConfiguration(...)`을 쓰면 빈 값 키를 지움 |
| **ZIP 읽을 때 CRC32 검사** | 첨부 묶음은 ZIP을 만들기만 함. 엑셀 읽기는 OpenXml 읽기 전용이라 깨진 파일이 이제 오류로 걸러질 뿐 | 업로드된 ZIP을 직접 읽는 업무 코드는 깨진 파일 오류 처리 확인 |
| **SqlClient의 Entra ID(Azure AD) 인증 분리** (`Authentication=Active Directory ...`) | 프레임워크는 SQL 인증·Windows 통합 인증만 씀. 해당 없음 | 사내 SQL Server(온프레미스)는 해당 없음 |
| **Kestrel HTTP 규칙 강화**, HTTP/1.1 요청에 Content-Length와 Transfer-Encoding이 함께 오면 응답 후 연결 종료 | IIS In-Process 호스팅. HTTP/1.1 부분은 .NET 10·IIS에도 보안 업데이트로 들어오는 변경 | 외부 시스템이 비표준 헤더로 호출하면 연결이 끊길 수 있음(외부 API 연동 시험 때 확인) |
| **MVC 호환성 옵션 제거, `ConcurrencyLimiter` 미들웨어 제거** | 둘 다 사용하지 않음(처리율 제한은 `RateLimiter` 사용) | `SetCompatibilityVersion`, `UseConcurrencyLimiter`를 쓰면 지움 |
| **최소 CPU가 x86-64-v2로 상향** (SSE4.2, POPCNT 등). 미지원 CPU면 "The current CPU is missing one or more of the baseline instruction sets"로 기동 실패 | - | 2009년 이후 서버 CPU는 모두 해당. 아주 오래된 가상화 환경에서 CPU 기능을 가리는 설정(호환성 모드)만 확인 |

## 4. 적용 순서

### 4.1 개발 PC·빌드 서버

1. .NET SDK 11.0.100 이상 설치([다운로드](https://dotnet.microsoft.com/download/dotnet/11.0)). 기존 .NET 10 SDK는 지우지 않습니다(옛 태그 빌드·롤백용).
2. Visual Studio 2026 최신 업데이트 또는 VS Code + C# 확장.
3. GitLab Windows 러너에도 SDK 11 설치. `dotnet --list-sdks`에 `11.0.1xx`가 보여야 합니다.

### 4.2 업무 저장소

```bash
git fetch framework --tags
git checkout develop && git checkout -b chore/framework-fw-v1.8.0
git merge fw-v1.8.0
dotnet build Portal.sln -c Release          # 3.1장 경고(CA2026, NU1510, SYSLIB*)가 나오면 고침
dotnet test Portal.sln -c Release --filter "Category!=Integration"
```

- 업무 저장소가 `global.json`, `Directory.Build.props`, `Directory.Packages.props`를 고쳐 쓰고 있었다면 충돌이 납니다. SDK·대상 프레임워크·Microsoft 패키지 줄은 프레임워크 값(`11.0.100`, `net11.0`, `11.0.0`)을 택하고, `<Version>`과 업무 전용 패키지 줄은 업무 저장소 값을 둡니다.
- 업무 전용 패키지는 `dotnet list package --vulnerable --include-transitive`와 .NET 11 지원 여부(패키지 페이지의 대상 프레임워크)를 확인합니다. `net8.0`·`netstandard2.0`용 패키지는 .NET 11에서 그대로 씁니다.
- 3.2장 동작 변경 중 업무 코드에 해당하는 것(금액 계산, 체인 구성, ZIP)을 확인합니다.
- 이번 버전은 DB 변경이 없지만, 업무 저장소 규칙대로 프레임워크 병합 MR에서 CI `db-integration`을 한 번 돌립니다(SqlPackage가 .NET 11에서 도는지도 함께 확인됨).

### 4.3 웹 서버 (개발계 먼저, 확인 후 운영계)

1. **ASP.NET Core 11.0 Hosting Bundle**(11.0.0 이상, 패치가 나오면 최신) 설치: [다운로드](https://dotnet.microsoft.com/download/dotnet/11.0) → "Hosting Bundle". Windows Server 2022 x64는 .NET 11 지원 OS입니다([지원 OS 목록](https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md)).
2. **.NET 10 Hosting Bundle은 지우지 않습니다.** 두 런타임이 나란히 설치되고, 앱은 빌드된 대상(`net11.0`)의 런타임을 씁니다. 롤백할 때 바로 옛 빌드를 올릴 수 있습니다.
3. 설치 뒤 `iisreset` (또는 서버 재시작). Hosting Bundle이 IIS 모듈(ASP.NET Core Module V2)을 최신으로 바꾸며, 새 모듈은 .NET 10 앱도 그대로 실행합니다.
4. 확인: `dotnet --list-runtimes`에 `Microsoft.AspNetCore.App 11.0.x`와 `Microsoft.NETCore.App 11.0.x`.
5. `fw-v1.8.0` 기반 빌드를 배포(`dotnet publish -c Release`, 프레임워크 종속 배포). `web.config`와 IIS 앱 풀 설정(`.NET CLR 버전: 관리 코드 없음`)은 바꿀 것이 없습니다.
6. Portal.Worker 서비스도 같은 서버에서 새 빌드로 교체하고 서비스를 다시 시작합니다. 3.2장의 서비스 복구 옵션을 켭니다.
7. 서버 자신에서 `https://localhost/health`를 열어 버전 `1.8.0`, 로그인·메뉴·조회·엑셀·PDF·메일 발송을 확인합니다(통합 테스트 확인 목록의 ".NET 11" 항목).

### 4.4 되돌리기 (롤백)

이번 버전은 DB 변경이 없으므로 **앱만 되돌리면 됩니다**.

1. 직전 배포본(`fw-v1.7.0` 기반 .NET 10 빌드)을 다시 배포합니다. .NET 10 Hosting Bundle을 지우지 않았다면 서버 설치 작업은 없습니다.
2. `iisreset`, Portal.Worker 서비스 재시작, `/health`에서 버전 `1.7.0` 확인.
3. 소스는 업무 저장소의 병합 MR을 되돌리거나(revert) 병합 전 커밋에서 다시 빌드합니다. 이때 빌드 PC에는 .NET 10 SDK(10.0.114 이상)가 있어야 합니다.
4. .NET 11 Hosting Bundle을 꼭 지워야 하면 지운 뒤 .NET 10 Hosting Bundle을 "복구(Repair)"로 다시 실행해 IIS 모듈을 되살리고 `iisreset` 합니다(최신 번들을 지우면 IIS 모듈이 함께 지워질 수 있음).

## 5. 확인하지 못한 점

| 항목 | 내용 |
| --- | --- |
| Windows Server 2022 + IIS 실제 기동 | 이 작업은 Linux 클라우드 환경에서 빌드·단위 테스트만 했습니다. 개발 서버에서 4.3장 절차로 확인이 필요합니다 |
| DB 통합 테스트 | 단계 종료 통합 테스트 규칙에 따라 돌리지 않았습니다. DB 변경은 없지만 SqlPackage를 .NET 11에서 실행하는 경로는 실제 배포로 확인이 필요합니다 |
| Edge 헤드리스 PDF 변환 | 프로세스 실행 API에 깨지는 변경은 없지만 개발 서버에서 한 번 출력해 봐야 합니다 |
| AD·LDAP | `System.DirectoryServices.Protocols` 11.0.0으로 올림. 회사 PC에서만 시험 가능 |
| Visual Studio 최소 버전 | 정확한 최소 업데이트 번호는 확인하지 못했습니다. Visual Studio 2026을 최신으로 업데이트하면 됩니다 |
| 11.0.0 보안 수정 범위 | 11.0.0은 10.0.14 긴급 보안 업데이트 뒤에 나와 같은 수정이 들어 있을 것으로 보지만 공지로 확인하지는 못했습니다. 11.0.1이 나오면 바로 올리기를 권합니다 |
