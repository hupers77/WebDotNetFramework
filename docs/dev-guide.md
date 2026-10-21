# 포털 프레임워크 개발 표준 가이드

Oct 21, 2026 · @사랑나무아빠

## 1. 이 문서 사용법

이 가이드는 포털 구축이 끝난 뒤 유지보수를 맡는 개발자가 혼자서 화면·SP·API를 추가하고 고칠 수 있도록 규칙과 절차를 모은 문서입니다. "왜 이렇게 설계했는가"는 [엔터프라이즈 웹 포털 프레임워크 설계서](framework-design.md)에, "무엇을 어떻게 만드는가"는 이 문서에 둡니다.

**처음 투입된 개발자의 읽는 순서**

1. 2장으로 개발 환경을 만들고 포털을 로컬에서 띄웁니다.
2. 3장을 따라 표준 화면 예제 하나를 복사해 연습용 화면을 만들어 봅니다.
3. 실제 작업 전에 4\~9장의 규칙을, MR 올리기 전에 10장 체크리스트를 봅니다.
4. 흔한 운영 요청(코드 추가, 설정 변경 등)은 11장에서 찾습니다.

**관련 문서**

| 문서 | 내용 |
| --- | --- |
| 프레임워크 설계서 | 아키텍처, DB 설계, 보안 표준, 백업·복구(16장), GitLab 배포(17장) |
| 초기 데이터 정의서 | 기본 역할, 공통코드, 설정 키, 시스템 메뉴의 초기값 |
| 업무별 ERD·데이터 사전 | 업무 화면의 테이블 정의 (업무 개발 단계에서 별도 관리) |
| 운영 매뉴얼 | 서버 구성, 일일 점검, 장애 대응 |

이 문서는 구축 중 확정되는 사항을 계속 반영하고, 구축 완료 시 GitLab `docs/dev-guide.md`로도 내보내 코드와 함께 버전 관리합니다. 규칙을 바꾸면 이 문서도 같은 MR에서 고칩니다.

## 2. 개발 환경 구성

개발 PC에는 .NET 10 SDK, SQL Server 2022 Developer Edition, Git만 있으면 되고, 처음 클론부터 로그인 화면까지 30분 안에 띄울 수 있어야 합니다. 그보다 오래 걸리면 이 장을 고칩니다.

| 도구 | 버전 | 라이선스 주의 |
| --- | --- | --- |
| .NET SDK | 10.0.x (레포지토리 `global.json`으로 고정) | 무료 |
| IDE | Visual Studio 2022 이상 Professional, 또는 VS Code + C# 확장 | Visual Studio Community와 C# Dev Kit는 일정 규모 이상 기업의 업무 사용이 제한되므로, 회사 라이선스가 없으면 VS Code + C# 확장만 씁니다 |
| DB | SQL Server 2022 Developer Edition | 개발·테스트 전용 무료 (운영 사용 금지) |
| DB 도구 | SSMS, SqlPackage (dotnet tool) | 무료 |
| 형상관리 | Git, GitLab 계정 | - |
| 브라우저 | Microsoft Edge (PDF 변환 테스트에도 사용) | 무료 |

**최초 실행 순서**

```powershell
git clone https://<gitlab>/portal/portal.git
cd portal
dotnet tool restore                      # SqlPackage 등 도구 복원

# 1) 로컬 DB 생성 + 스키마 배포 + 초기 데이터
./db/ops/local-setup.ps1 -Server "localhost" -Database "PORTAL_LOCAL"

# 2) 로컬 연결 문자열은 소스가 아닌 사용자 비밀 저장소에
dotnet user-secrets --project src/Portal.Web set "ConnectionStrings:Portal" "Server=localhost;Database=PORTAL_LOCAL;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"

# 3) 실행 → https://localhost:5001 접속, 초기 관리자 계정으로 로그인(최초 로그인 시 비밀번호 변경)
dotnet run --project src/Portal.Web

# 4) 테스트
dotnet test
```

**참고**

- 로컬에서는 `AUTH.MODE = FORM`으로 동작합니다. AD SSO는 도메인에 가입된 개발계 서버에서 확인합니다.
- 개발계·운영계 DB에 개발 PC에서 직접 연결해 데이터를 고치지 않습니다. 모든 DB 변경은 `db/` 프로젝트에 커밋하고 파이프라인으로 배포합니다.
- 운영 데이터가 필요하면 분기별 복구 훈련으로 갱신된 개발계(마스킹 완료)를 씁니다.

## 3. 새 화면 추가 순서

새 화면은 표준 화면 예제(`Portal.Modules.Sample`) 중 가장 비슷한 유형을 복사해 아래 8단계로 만듭니다. 예시는 "거래처 관리"(목록-폼 유형) 화면입니다.

1. **프로그램 ID를 정합니다.** 형식은 `<모듈>_<대상>_<유형>`, 예: `PUR_VENDOR_MGT`. 유형은 `MGT`(관리), `INQ`(조회), `RPT`(레포트), `POP`(팝업)입니다.
2. **DB 객체를 만듭니다.** `db/Portal.Database/<스키마>/Tables`, `/StoredProcedures`, `/Types`에 테이블, SP 4종(`USP_VENDOR_GET_LIST`, `_GET`, `_SAVE`, `_DEL`), 필요하면 TVP를 추가합니다. 파일 하나에 객체 하나입니다.
3. **등록 데이터를 추가합니다.** `db/seed/<모듈>/PUR_VENDOR_MGT.sql`에 `TB_PROGRAM`, `TB_PROGRAM_FUNC`, `TB_PROGRAM_API`, 메뉴, 다국어 키를 `MERGE`로 작성합니다. 여러 번 실행해도 결과가 같아야 합니다(배포 시 자동 실행).
4. **DTO와 Service를 만듭니다.** `IVendorService` 인터페이스와 구현에 업무 규칙을 둡니다. SP 호출은 `ISpExecutor`로만 합니다.
5. **Controller를 만듭니다.** `PortalController`를 상속하고 클래스에 `[Program]`, 변경 Action에 `[Func]`를 붙입니다.
6. **View와 JS를 만듭니다.** 예제 폴더를 복사해 이름만 바꾸고, 화면 문구는 모두 다국어 키로 씁니다(한국어·영어·중국어 3개 언어 모두 입력).
7. **테스트를 작성합니다.** SP당 통합 테스트 1개 이상, Service 업무 규칙 단위 테스트. 권한 매트릭스 테스트는 3단계의 `TB_PROGRAM_API`에서 자동 생성됩니다.
8. **로컬에서 확인하고 MR을 올립니다.** 역할에 권한을 주고 버튼 표시·차단을 확인한 뒤, 10장 체크리스트를 MR 본문에 붙입니다.

**파일 배치**

```
src/Portal.Modules.Purchase/
├─ Areas/Pur/Controllers/VendorController.cs
├─ Areas/Pur/Views/Vendor/Index.cshtml
├─ Services/IVendorService.cs, VendorService.cs
├─ Models/VendorDto.cs, VendorSearch.cs
└─ wwwroot/js/pur/vendor.js
db/Portal.Database/PUR/Tables/TB_VENDOR.sql
db/Portal.Database/PUR/StoredProcedures/USP_VENDOR_GET_LIST.sql ...
db/seed/PUR/PUR_VENDOR_MGT.sql
tests/Portal.IntegrationTests/Pur/VendorSpTests.cs
tests/Portal.UnitTests/Pur/VendorServiceTests.cs
```

**Controller 예시**

```csharp
[Area("Pur")]
[Program("PUR_VENDOR_MGT")]
public sealed class VendorController(IVendorService vendors) : PortalController
{
    public IActionResult Index() => View();                 // READ (기본)

    [HttpGet]
    public async Task<ApiResult<PageResult<VendorDto>>> List([FromQuery] VendorSearch search)
        => Ok(await vendors.GetListAsync(search));

    [HttpPost, Func("UPDATE")]
    public async Task<ApiResult> Save([FromBody] VendorDto dto)
        => Ok(await vendors.SaveAsync(dto));

    [HttpPost, Func("DELETE")]
    public async Task<ApiResult> Delete([FromBody] VendorKey key)
        => Ok(await vendors.DeleteAsync(key));
}
```

Controller에는 업무 로직을 두지 않습니다. 사용자 ID·언어·IP는 SpExecutor가 넣으므로 DTO에 두지 않습니다.

## 4. C# 코딩 규칙

Microsoft .NET 기본 명명 규칙을 따르고, 여기서 정한 규칙은 `.editorconfig`와 분석기가 빌드에서 강제합니다. 빌드 경고는 오류로 취급하므로 경고를 남긴 채 병합할 수 없습니다.

| 대상 | 규칙 | 예 |
| --- | --- | --- |
| 클래스·메서드·속성 | PascalCase | `VendorService.GetListAsync` |
| 인터페이스 | `I` + PascalCase | `IVendorService` |
| 지역 변수·매개변수 | camelCase | `vendorCode` |
| private 필드 | `_` + camelCase (가능하면 primary constructor) | `_spExecutor` |
| 비동기 메서드 | 접미어 `Async`, `async void` 금지 | `SaveAsync` |
| DTO | 업무명 + `Dto` / 검색 조건은 `Search` | `VendorDto`, `VendorSearch` |
| 상수 키 | `ConfigKeys`, `MessageKeys`, `FuncCodes` 클래스에 모음 | `MessageKeys.Saved` |

**필수 규칙**

- Nullable 참조 형식을 켭니다(`<Nullable>enable</Nullable>`). `!` 연산자로 경고를 끄지 않습니다.
- 하드코딩 금지: 화면 문구, URL, 파일 경로, 정책 숫자(시간, 횟수, 크기), 코드값은 다국어 키·설정 키·공통코드로만 참조합니다.
- 의존성은 생성자 주입으로만 받고, `new`로 Service를 만들거나 정적 클래스에 상태를 두지 않습니다(테스트에서 교체할 수 없기 때문).
- 현재 시각은 `TimeProvider`로 얻습니다(`DateTime.Now` 금지). 테스트에서 시각을 고정할 수 있습니다.
- 예외는 삼키지 않습니다. 업무 오류는 `BizException(MessageKeys.X)`로 던지고, 나머지는 공통 예외 미들웨어가 처리합니다(7장).
- DB 호출과 외부 호출은 모두 비동기이며 `CancellationToken`을 전달합니다.
- 새 NuGet 패키지는 설계서 2장의 허용 목록에 없으면 추가하지 않습니다. 필요하면 아키텍트 승인 후 목록과 함께 추가합니다.
- 주석은 "왜"를 씁니다. 공개 인터페이스에는 XML 문서 주석(`///`)을 달아 IDE 도움말과 API 문서에 나오게 합니다.

## 5. 화면(Razor/JS) 규칙과 웹 접근성

화면은 Razor가 구조와 권한을, JS는 데이터 조회·저장만 맡습니다. 버튼 표시 여부를 JS에서 판단하지 않고 `<fw-button>` Tag Helper에 맡깁니다.

**Razor**

- 레이아웃은 `_PortalLayout`만 씁니다. 탭 안에서 열리는 화면은 `_TabLayout`입니다.
- 문구는 `@L["키"]`, 버튼은 `<fw-button func="UPDATE" text-key="BTN.SAVE" />`, 코드 선택은 `<fw-select code-group="..." />`를 씁니다.
- `Html.Raw`는 금지입니다. 게시판 본문처럼 HTML이 필요하면 서버에서 필터를 거친 `SafeHtml` 형식만 출력합니다.

**JavaScript**

- 화면당 JS 파일 하나(`wwwroot/js/<모듈>/<화면>.js`), ES 모듈 형식. 외부 라이브러리를 화면에서 직접 불러오지 않습니다.
- 인라인 `<script>`와 `onclick=` 속성은 CSP에서 차단되므로 쓰지 않습니다. 이벤트는 `addEventListener`로 겁니다.
- 서버 호출은 `fw.ajax`만 씁니다(CSRF, 로딩, 오류 처리 자동). `fetch` 직접 호출 금지.
- 사용자 데이터는 `textContent` 또는 그리드 컴포넌트로만 넣고 `innerHTML`에 직접 넣지 않습니다.
- 메시지는 `fw.t('MSG.SAVED')`, 확인창은 `fw.dialog.confirm`, 날짜·숫자 표시는 `fw.format`을 씁니다(사용자 언어·시간대 자동 반영).
- 여러 행 저장은 그리드의 `getChanges()`(I/U/D 행)를 보내 TVP 일괄 저장 SP로 처리합니다. 행마다 요청을 보내지 않습니다.

**웹 접근성 (KWCAG 2.2 기준, 공통 컴포넌트가 기본 지원)**

| 항목 | 개발자가 지킬 것 |
| --- | --- |
| 적절한 대체 텍스트 | 의미 있는 이미지·아이콘 버튼에 `alt` 또는 `aria-label`(다국어 키) |
| 레이블 제공 | 모든 입력에 `<label for>` 연결. `fw-form` 필드 사용 시 자동 |
| 키보드 사용 | 모든 기능을 Tab/Enter/Space/방향키로 사용 가능, 포커스 표시 제거 금지 |
| 명도 대비 | 텍스트와 배경 4.5:1 이상 (테마 색상 변수만 사용) |
| 색에 무관한 인식 | 오류·상태를 색만으로 표시하지 않고 문구·아이콘을 함께 표시 |
| 오류 정정 | 입력 오류는 해당 필드 옆에 표시하고 포커스를 이동 |
| 페이지 제목 | 탭 제목·`<title>`에 화면명(메뉴명) 표시. 화면당 `<h1>` 하나 |
| 표의 구성 | 그리드는 헤더 셀과 요약(`aria-label`)을 갖음 |

## 6. SQL/SP 작성 규칙

명명 규칙과 SP 템플릿은 설계서 7·8장이 기준이며, 여기에는 유지보수 중 자주 어기는 규칙만 모았습니다. 가장 중요한 규칙은 회사 범위 필터를 빠뜨리지 않는 것입니다.

- 모든 목록·단건 SP는 데이터 범위 함수를 거칩니다. 협력사 사용자가 다른 회사 데이터를 보면 안 되기 때문입니다(아래 예시). 공유 데이터(공지 등)는 공개 대상 조건을 거칩니다.
- `SELECT *`, `WITH (NOLOCK)`은 금지입니다. DB가 READ\_COMMITTED\_SNAPSHOT이라 NOLOCK이 필요 없습니다.
- 키워드는 대문자, 들여쓰기 2칸, 테이블 별칭은 의미 있는 약어(`V`, `VL`)를 씁니다.
- 저장 SP는 `@P_ROW_VER`로 동시성을 검사하고, 감사 대상 테이블이면 `LOG.USP_AUDIT_WRITE`를 호출합니다.
- 기준정보·코드·메뉴·권한·다국어·설정을 바꾸는 SP는 마지막에 `TB_CACHE_VERSION`을 올립니다. 빠뜨리면 화면에 변경이 반영되지 않습니다.
- 테이블 변경은 호환성을 유지합니다: 컬럼은 NULL 허용 또는 기본값과 함께 추가하고, 삭제·이름 변경은 다음 릴리스로 미룹니다(롤백 대비).
- 초기·등록 데이터 스크립트는 `MERGE`로 작성해 여러 번 실행해도 같은 결과가 나오게 합니다.
- 작성자·변경 이력은 SP 주석이 아니라 Git 이력으로 관리합니다. SP 첫 줄 주석에는 용도와 프로그램 ID만 씁니다.
- 한 요청에서 1초를 넘는 SP는 느린 SP 로그에 남습니다. 릴리스 전 실행 계획을 확인하고 필요한 인덱스를 같은 MR에 넣습니다.

**데이터 범위 적용 예시**

```sql
-- 협력사 등록 업무 데이터: 사용자의 데이터 범위(회사/부서/본인)에 맞는 행만
SELECT V.VENDOR_CD, V.VENDOR_NM, V.COMPANY_CD
  FROM PUR.TB_VENDOR V
  JOIN FW.UFN_DATA_SCOPE(@P_USER_ID, 'PUR_VENDOR_MGT') S
    ON V.COMPANY_CD = S.COMPANY_CD
   AND (S.DEPT_ID IS NULL OR V.DEPT_ID = S.DEPT_ID)
   AND (S.USER_ID IS NULL OR V.REG_ID  = S.USER_ID)
 WHERE V.USE_YN = 'Y';
```

`FW.UFN_DATA_SCOPE`는 사용자의 역할과 사용자 유형(본사/협력사)을 보고 허용 범위를 돌려주는 인라인 테이블 함수입니다. 협력사 사용자에게는 항상 자기 회사 한 행만 돌려줍니다.

## 7. 오류·메시지 코드 체계와 로깅

사용자에게 보이는 모든 메시지는 다국어 키이고, 오류 화면에는 항상 추적 ID가 함께 표시됩니다. 사용자가 추적 ID를 알려주면 운영 모니터링 화면에서 서버 오류와 SP 호출을 바로 찾을 수 있습니다.

**다국어 키 체계**

| 접두어 | 용도 | 예 |
| --- | --- | --- |
| `LBL.` | 공통 라벨 | `LBL.USER_NM`, `LBL.USE_YN` |
| `BTN.` | 버튼 | `BTN.SAVE`, `BTN.DELETE` |
| `MSG.` | 결과·업무 오류 메시지 | `MSG.SAVED`, `MSG.DUP_CODE`, `MSG.CONCURRENCY` |
| `VAL.` | 입력 검증 | `VAL.REQUIRED`, `VAL.MAX_LENGTH` (인자 `{0}` 사용) |
| `CNF.` | 확인 질문 | `CNF.DELETE` |
| `<모듈>.<화면>.` | 화면 전용 문구 | `PUR.VENDOR.TITLE` |

공통 키로 표현되면 화면 전용 키를 만들지 않습니다. 새 키는 한국어·영어·중국어를 모두 채워 등록합니다.

**오류 처리 흐름**

| 발생 위치 | 방법 | 화면 표시 | HTTP |
| --- | --- | --- | --- |
| SP 업무 오류 | `THROW <번호>, 'MSG.키', 1` | 키 번역 메시지 | 200 (`success:false`) |
| Service 업무 오류 | `throw new BizException(MessageKeys.X)` | 키 번역 메시지 | 200 (`success:false`) |
| 입력 검증 실패 | DTO 특성 검증(`[Required]` 등, 메시지는 `VAL.` 키) | 필드별 메시지 | 400 |
| 권한 없음 | 권한 필터 | 권한 안내 | 403 |
| 세션 만료 | 인증 미들웨어 | 로그인 화면 | 401 |
| 그 외 예상 못한 오류 | 공통 예외 미들웨어가 기록 | "일시적 오류" + 추적 ID | 500 |

SP 오류 번호 대역: `FW` 50001\~50999, `BRD` 51000\~51999, `RPT` 52000\~52999, 이후 모듈마다 1,000단위로 추가 배정합니다.

**로깅 규칙**

- `ILogger<T>`만 씁니다. 메시지 템플릿을 쓰고 문자열을 이어 붙이지 않습니다: `_logger.LogWarning("Vendor {VendorCd} not found", vendorCd);`
- 레벨: `Error` 조치가 필요한 실패, `Warning` 장애는 아니지만 주의(재시도 성공, 느린 SP), `Information` 주요 업무 이벤트, `Debug` 개발계에서만.
- 비밀번호, 토큰, 연결 문자열, 개인정보 원문은 로그에 남기지 않습니다.
- 접속기록·감사 로그는 프레임워크가 자동으로 남기므로 화면에서 따로 기록하지 않습니다. 개인정보 처리 화면이면 `TB_PROGRAM.PII_YN = 'Y'`로 등록만 합니다.

## 8. 외부 API 개발 규칙

외부 시스템에 제공하는 API는 `/api/v1/<리소스>` 형식의 REST로 만들고, 한 번 공개한 응답 형식은 바꾸지 않습니다. 형식을 깨야 하면 `v2`를 새로 만들고 `v1`은 공지 후 일정 기간 유지합니다.

- URL은 소문자 복수형 명사(`/api/v1/vendors/{vendorCd}`), 동작은 HTTP 메서드(GET 조회, POST 등록, PUT 수정, DELETE 삭제)로 표현합니다.
- JSON 필드는 camelCase, 날짜는 ISO 8601 UTC(`2026-10-21T05:30:00Z`)입니다.
- 목록은 `page`, `size`(최대 500) 파라미터로 페이징합니다.
- 모든 API는 클라이언트별 허용 목록(`TB_API_CLIENT`)에 등록된 것만 호출됩니다. 새 API는 등록 데이터 스크립트를 함께 만듭니다.
- 같은 요청이 두 번 와도 결과가 같아야 하는 등록 API는 `Idempotency-Key` 헤더를 받습니다.
- OpenAPI 문서는 자동 생성되므로, 모든 요청·응답 DTO에 XML 주석을 달아 문서에 설명이 나오게 합니다.

**응답 형식**

```json
// 성공
{ "success": true, "data": { "vendorCd": "V0001", "vendorNm": "한빛산업" }, "traceId": "00-4bf9...-01" }

// 실패 (HTTP 상태 코드도 함께: 400/401/403/404/409/429/500)
{ "success": false, "error": { "code": "MSG.DUP_CODE", "message": "이미 사용 중인 코드입니다." }, "traceId": "00-4bf9...-01" }
```

외부 시스템을 호출할 때는 주소를 코드에 쓰지 않고 `TB_API_OUTBOUND`에 등록한 대상 키로 `IOutboundApiClient`를 씁니다. 타임아웃·재시도는 등록값을 따릅니다.

## 9. 테스트 작성 규칙

버그를 고칠 때는 먼저 그 버그를 재현하는 실패 테스트를 만들고, 고친 뒤 통과하는 것을 확인합니다. 테스트를 건너뛰거나 `Skip` 처리해서 파이프라인을 통과시키지 않습니다.

| 종류 | 위치 | 작성 시점 | 규칙 |
| --- | --- | --- | --- |
| 단위 | `tests/Portal.UnitTests/<모듈>/` | Service에 업무 규칙을 추가·변경할 때 | DB 없이, `Portal.Testing`의 Fake(`FakeSpExecutor`, `FakeCurrentUser`, `FakeTimeProvider`)로 의존성 교체 |
| SP 통합 | `tests/Portal.IntegrationTests/<모듈>/` | SP를 추가·변경할 때 (SP당 1개 이상) | 테스트마다 트랜잭션을 열고 끝나면 롤백. 데이터는 테스트 안에서 만듦 |
| 웹 통합 | `tests/Portal.IntegrationTests/Web/` | 권한·인증·API 응답 형식에 영향을 주는 변경 | `PortalWebFactory` + 테스트 인증 핸들러로 사용자·역할·회사 지정 |
| 회사 격리 | SP 통합 테스트 안 | 회사 소유 데이터를 다루는 모든 목록·단건 SP | 협력사 A 사용자로 협력사 B 데이터가 0건인지 확인 |

**이름과 형식**: 테스트 이름은 `대상_상황_기대결과`(예: `SaveAsync_중복코드_DUP_CODE오류`), 본문은 준비(Arrange) → 실행(Act) → 확인(Assert) 순서로 씁니다. 시간·난수·외부 서비스에 의존하는 테스트는 만들지 않습니다.

```csharp
public sealed class VendorSpTests(DbFixture db) : IClassFixture<DbFixture>
{
    [Fact]
    public async Task GetList_협력사사용자_타사데이터_조회안됨()
    {
        await using var tx = await db.BeginAsync();               // 끝나면 롤백
        await tx.SeedVendorAsync(company: "P001", code: "V1");
        await tx.SeedVendorAsync(company: "P002", code: "V2");

        var rows = await tx.Sp.QueryPageAsync<VendorDto>("PUR.USP_VENDOR_GET_LIST",
            new { P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, asUser: TestUsers.Partner("P001"));

        Assert.All(rows.Items, r => Assert.Equal("P001", r.CompanyCd));
    }
}
```

로컬에서는 `dotnet test`, 통합 테스트만 돌리려면 `dotnet test --filter Category=Integration`을 씁니다. 테스트 DB는 `db/ops/local-setup.ps1`이 만든 로컬 DB를 씁니다.

## 10. 보안 코딩 체크리스트와 MR 규칙

MR을 올릴 때 아래 체크리스트를 MR 본문에 붙이고 해당 항목을 확인합니다. 리뷰어는 체크되지 않은 항목이 있으면 승인하지 않습니다. 자세한 위협별 대응은 설계서 9장을 봅니다.

**보안 체크리스트**

- [ ] 모든 새 Action에 `[Program]`/`[Func]`가 있고, `TB_PROGRAM_API` 등록 데이터를 추가했다
- [ ] DB 접근은 SP로만 했고, 동적 SQL을 만들지 않았다
- [ ] 회사 소유 데이터 SP에 `FW.UFN_DATA_SCOPE`를 적용했고, 회사 격리 테스트가 있다
- [ ] 화면에 `Html.Raw`, `innerHTML`, 인라인 스크립트를 쓰지 않았다
- [ ] 파일 업로드·다운로드는 `FileService`만 썼다 (경로 직접 조합 없음)
- [ ] 리다이렉트 URL은 로컬 URL만 허용했다
- [ ] 외부 호출 주소는 `TB_API_OUTBOUND` 등록값을 썼다
- [ ] 비밀번호·토큰·연결 문자열·개인정보를 코드, 설정 파일, 로그에 넣지 않았다
- [ ] 새로 개인정보를 저장하지 않았다 (불가피하면 사전 검토 완료 + 암호화·마스킹·`PII_YN` 적용)
- [ ] 허용 목록 밖의 패키지를 추가하지 않았다

**MR 규칙**

- 제목: `[이슈번호] 변경 요약` (예: `[PORTAL-123] 거래처 관리 화면 추가`). 본문에는 변경 이유, 화면 캡처(화면 변경 시), DB 변경 여부, 체크리스트를 씁니다.
- MR 하나에는 한 가지 변경만 담습니다. 리팩터링과 기능 추가는 나눠 올립니다.
- 리뷰어 1명 이상 승인 + 파이프라인 성공이어야 병합됩니다. 프레임워크 프로젝트(Core, Data, Security, Infrastructure, Framework.Web)를 바꾸는 MR은 프레임워크 담당자가 리뷰합니다.
- DB 변경이 있으면 파이프라인이 만든 변경 SQL 스크립트를 리뷰어가 함께 봅니다.
- 브랜치·배포 절차는 설계서 17장을 따릅니다.

## 11. 자주 하는 작업

운영 중 들어오는 요청 대부분은 코드 수정 없이 관리 화면에서 끝납니다. 코드를 고치기 전에 아래 표에서 먼저 찾습니다.

| 요청 | 처리 방법 | 코드 수정 |
| --- | --- | --- |
| 메뉴 위치·이름 변경, 메뉴 숨기기 | 시스템 관리 > 메뉴 관리 | 없음 |
| 특정 역할에 버튼 권한 부여 | 시스템 관리 > 역할별 권한 | 없음 |
| 협력사 추가, 협력사 관리자 지정 | 시스템 관리 > 회사 관리 / 사용자 관리 | 없음 |
| 공통코드 값 추가·숬기기 | 시스템 관리 > 공통코드 (삭제 대신 사용 안 함) | 없음 |
| 화면 문구·번역 수정 | 시스템 관리 > 다국어 관리 | 없음 |
| 정책값 변경 (세션 시간, 비밀번호 규칙, 업로드 크기 등) | 시스템 관리 > 설정 관리 (`RESTART_YN = 'Y'` 항목은 앱 풀 재시작 필요) | 없음 |
| 게시판 추가 | 게시판 관리에서 생성 후 메뉴에 연결 | 없음 |
| 대시보드 위젯 추가 | 데이터 SP 작성 → 위젯 관리에 등록 | SP만 |
| 레포트 추가 | 데이터 SP + Razor 템플릿 작성 → 레포트 관리에 등록 | SP + 템플릿 |
| 배치 작업 추가 | SP 또는 `IScheduledJob` 구현 작성 → 스케줄 관리에 주기(Cron) 등록 | SP 또는 클래스 |
| 외부 API 클라이언트 추가 | 연계 관리 > API 클라이언트 (비밀값은 발급 시 1회만 표시) | 없음 |
| 새 언어 추가 | `TB_LANG`에 행 추가 → 다국어 관리에서 엑셀로 일괄 번역 입력 | 없음 |
| 새 설정 키 필요 | `ConfigKeys`에 상수 추가 + `db/seed`에 기본값 + 초기 데이터 정의서 갱신 | 있음 |
| 화면 변경이 반영 안 됨 | 운영 모니터링 > 캐시 강제 갱신. 자주 발생하면 해당 SP의 `TB_CACHE_VERSION` 누락 확인 | 경우에 따라 |

관리 화면에서 바꾼 값은 설정 이력·감사 로그에 남으므로 누가 언제 바꿈는지 나중에 확인할 수 있습니다.
