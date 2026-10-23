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

# 3) 초기 관리자(sysadmin) 생성: 임시 비밀번호가 한 번만 출력됨. 다시 실행하면 비밀번호 재설정
dotnet run --project src/Portal.Web -- init-admin

# 4) 실행 → https://localhost:5001 접속, sysadmin으로 로그인(최초 로그인 시 비밀번호 변경)
dotnet dev-certs https --trust           # 로컬 HTTPS 인증서 (세션·CSRF 쿠키는 HTTPS에서만 동작)
dotnet run --project src/Portal.Web

# 5) 테스트
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
3. **등록 데이터를 추가합니다.** `db/seed/<모듈>/PUR_VENDOR_MGT.sql`에 `TB_PROGRAM`, `TB_PROGRAM_FUNC`, `TB_PROGRAM_API`, 메뉴, 다국어 키를 `MERGE`로 작성합니다. `TB_PROGRAM_FUNC`에 넣은 기능(조회·등록·수정·삭제·내보내기·출력 등)이 역할별 권한 화면의 체크 칸이 되므로, 화면에 있는 버튼의 기능은 빠짐없이 넣습니다. 여러 번 실행해도 결과가 같아야 합니다(배포 시 자동 실행). 새 파일은 `db/Portal.Database/Scripts/PostDeployment.sql`에 `:r` 한 줄로 등록합니다.
4. **DTO와 Service를 만듭니다.** `IVendorService` 인터페이스와 구현에 업무 규칙을 둡니다. SP 호출은 `ISpExecutor`로만 합니다.
5. **Controller를 만듭니다.** `PortalController`를 상속하고 클래스에 `[Program]`, 변경 Action에 `[Func]`를 붙입니다.
6. **View와 JS를 만듭니다.** 예제 폴더를 복사해 이름만 바꾸고, 화면 문구는 모두 다국어 키로 씁니다(한국어·영어·중국어 3개 언어 모두 입력).
7. **테스트를 작성합니다.** SP당 통합 테스트 1개 이상, Service 업무 규칙 단위 테스트. 권한 매트릭스 테스트(`tests/Portal.IntegrationTests/Web/PermissionMatrixTests.cs`)는 3단계의 `TB_PROGRAM_API`에서 경로별·역할별 케이스를 자동으로 만들므로 따로 작성하지 않습니다. 대신 Action을 추가하면 `TB_PROGRAM_API`에 `(메서드, 경로, 프로그램, 기능)` 한 줄을 꼭 함께 넣어야 합니다. 경로는 `/{Area}/{Controller}/{Action}`이고 `Index`는 생략하며, 메서드 특성이 없는 Action은 GET으로 적습니다. 코드의 `[Program]`/`[Func]`와 한 줄이라도 다르면 테스트가 실패합니다.
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

**그리드 일괄 저장 권한**: 저장 Action은 `[Func("UPDATE")]`로 열고, 본문에서 `await DemandRowFuncsAsync(request.Rows)`를 먼저 부릅니다. 추가 행(I)이 있으면 `CREATE`, 삭제 행(D)이 있으면 `DELETE` 권한이 추가로 필요하며, 없으면 403입니다. 행 DTO는 `IGridRow`(`RowStat`)를 구현합니다. 실제 예는 공통코드 화면(`src/Portal.Web/Areas/Sys`)입니다.

**엑셀 다운로드**: 목록 화면의 엑셀 버튼은 `<fw-button func="EXPORT" text-key="BTN.EXCEL">`으로 그리고, Action은 `[HttpGet, Func(FuncCodes.Export)]`로 엽니다. Service가 화면 그리드와 같은 컬럼으로 `ExcelSheet<T>`를 만들어 `ExcelWriter.Write`에 넘기고, Controller는 `Excel(stream, "파일명.xlsx")`로 돌려줍니다. 머리글은 `IPortalLocalizer`로 사용자 언어 문구를 씁니다. 문자열은 항상 글자 칸으로 써서 `=`로 시작해도 수식이 실행되지 않고(수식 주입 방지), 날짜는 엑셀 날짜 서식으로 들어갑니다. 행은 흘려 쓰므로 목록 SP 결과를 그대로 넘기면 됩니다. 예: 공통코드 화면의 `Export`.

**개인정보가 든 파일 다운로드**: 이름처럼 개인정보가 들어 있는 목록은 사유를 받고 개인정보 접속기록(`LOG.TB_PII_ACCESS_LOG`, 10년 보관, 해시 체인)을 남긴 뒤 내려줍니다. 사유가 주소·방문 기록에 남지 않게 Action은 `[HttpPost, Func(FuncCodes.Export)]`로 열고, 화면은 `fw.dialog.prompt`로 사유를 받아 `fw.download(url, { search, reason })`로 보냅니다. Service는 파일을 만들기 전에 `IPiiAccessLog.WriteAsync(programId, PiiActions.Download, 대상, 사유)`를 부릅니다. 대상에는 건수와 검색 조건만 넣고, 개인정보 원문(이름, 이름일 수 있는 검색어)은 넣지 않습니다. 기록에 실패하면 파일도 나가지 않습니다. 마스킹 해제(`PiiActions.Unmask`)도 같은 방법으로 사유를 받아 기록합니다. 예: 사용자 관리 화면의 `Export`.

**엑셀 업로드**: 내려받은 엑셀을 그대로 업로드 양식으로 씁니다(같은 컬럼 정의를 내보내기와 업로드가 함께 씁니다). 순서는 양식 받기 → 입력 → 업로드(미리보기) → 그리드 확인 → 저장입니다.

1. 버튼은 `<fw-button func="UPLOAD" text-key="BTN.UPLOAD">`과 숨긴 `<input type="file" accept=".xlsx">`로 그리고, `fw.upload(url, file)`로 올립니다.
2. Action은 `[HttpPost, Func(FuncCodes.Upload)]`로 열고 `IFormFile` 크기를 `FILE.MAX_SIZE_MB`로 확인합니다. Service는 `ExcelReader.Read(stream, 최대행수)`로 첫 시트를 읽습니다. 형식이 아닌 파일, 행 수 초과는 업무 오류가 됩니다.
3. Service는 머리글을 양식과 맞춰 보고(모르는 머리글이면 `MSG.EXCEL_HEADER_MISMATCH`), 행마다 저장 검증과 같은 규칙으로 검사해 **추가(I)·수정(U) 행과 행별 오류**를 돌려줍니다. 미리보기는 저장하지 않습니다.
4. 화면은 `grid.applyChanges(rows, 키필드)`로 행을 그리드에 반영하고 오류 목록을 보여 줍니다. 사용자가 확인한 뒤 저장 버튼으로 저장하며, 저장 Action이 행 상태별 권한과 값을 다시 검사합니다.

빈 칸은 값을 지우고(필수 값은 오류), 날짜는 엑셀 날짜 서식 또는 `yyyy-MM-dd` 글자로 받습니다. 예: 공통코드 화면의 `Upload`.

**PDF 만들기**: HTML을 PDF로 바꿀 때는 `IPdfConverter.ConvertAsync(html, new PdfPageOptions(바닥글 문구, 가로 여부))`를 씁니다. 서버의 Microsoft Edge를 헤드리스로 띄워 A4로 인쇄하고, 모든 쪽 아래에 바닥글 문구(출력자·출력 일시 등)와 쪽 번호(쪽 / 전체 쪽)를 넣습니다. HTML은 인쇄용 CSS까지 들어 있는 완성된 문서여야 합니다. 변환할 때는 외부 자원을 막으므로 CSS는 `<style>`로 문서 안에 넣고, 그림과 글꼴은 `data:` URL로 넣습니다. 스크립트도 실행되지 않습니다. 동시에 4건까지 변환하고, 그 이상은 앞 변환이 끝날 때까지 기다립니다. 한 건이 60초를 넘으면 실패합니다(`Portal:Pdf:MaxConcurrency`, `TimeoutSeconds`). 브라우저 경로는 `Portal:Pdf:BrowserPath`로 정하고, 비우면 Edge 기본 설치 경로를 씁니다. 실제 변환 테스트(`BrowserPdfConverterTests`)는 환경 변수 `PORTAL_PDF_BROWSER`에 Edge나 Chromium 경로를 넣을 때만 돕니다.

**레포트 만들기**: 출력물(점검표, 집계표 등)은 화면을 따로 만들지 않고 레포트로 등록합니다. 공통 레포트 화면(`/Rpt/Viewer/Index/{레포트 ID}`, `src/Portal.Web/Areas/Rpt`)이 조건 입력·미리보기·PDF·엑셀을 제공합니다.

1. 프로그램을 만듭니다. 프로그램 ID가 레포트 ID입니다(`RPT_<대상>`). 주소는 `/Rpt/Viewer/Index/<ID>`이고, 메뉴에 이 주소로 넣습니다. 기능은 READ(미리보기), PRINT(PDF), EXPORT(엑셀)입니다. 화면 코드가 없으므로 `TB_PROGRAM_API`는 등록하지 않습니다(권한은 레포트 화면이 레포트 ID로 확인합니다).
2. 데이터 SP `<스키마>.USP_RPT_<대상>_GET_LIST`를 만듭니다. 결과셋은 하나이고, 컬럼은 그대로 표의 열이 됩니다(순서 유지). 조건 파라미터와 `@P_MAX_ROWS`를 받아 `SELECT TOP (@P_MAX_ROWS + 1)`로 돌려줍니다(넘치면 화면이 조건을 좁혀 달라고 합니다). 날짜만 있는 값은 `DATE`로, 일시는 UTC `DATETIME2`로 돌려주면 일시는 서버 현지 시간으로 표시합니다. 집계 레포트는 건수만 돌려주고 개인정보는 넣지 않습니다.
3. `TB_REPORT`에 행을 넣습니다(시드 `12_REPORT.sql`). `PARAMS_JSON`은 조건 목록입니다: `[{"name":"P_MONTH","type":"MONTH","required":true,"labelKey":"LBL.MONTH","default":"PREV_MONTH"}]`. 형식은 `DATE`(yyyy-MM-dd), `MONTH`(yyyy-MM), `TEXT`(`maxLength`, 기본 100), 기본값은 `TODAY`, `THIS_MONTH`, `PREV_MONTH`입니다. 정의에 없는 값은 SP로 넘어가지 않습니다. 용지 방향(`ORIENT_CD`), 결재란(`APPROVAL_YN`), 합계 행(`TOTAL_YN`), 최대 행 수(`MAX_ROWS`)도 여기서 정합니다.
4. 열 머리글은 문구 `RPT.<레포트 ID>.<컬럼>`으로 3개 언어를 넣습니다(없으면 컬럼 이름이 보입니다).
5. 기본 표로 충분하면 `TEMPLATE_VIEW`를 비웁니다. 점검 의견란처럼 표 밖의 내용이 필요하면 `Areas/Rpt/Views/Templates/<이름>.cshtml`을 만들어 이름을 넣습니다. 템플릿은 `Layout = "_ReportLayout.cshtml"`(제목·조건·출력자·일시·결재란)과 `<partial name="_ReportTable.cshtml" model="Model.Report" />`(표)를 쓰고, 스타일은 `wwwroot/css/report.css`에 둡니다. 미리보기는 이 파일을 연결하고 PDF는 같은 내용을 문서 안에 넣습니다. 스크립트·외부 그림은 쓰지 않습니다(미리보기 iframe과 PDF 모두 막습니다).
6. 개인정보가 든 레포트는 프로그램의 `PII_YN = 'Y'`로 표시합니다. 미리보기는 개인정보 조회로, PDF·엑셀은 사유를 받아 다운로드로 접속기록에 남습니다.

**업무 화면에서 레포트 발행**: 업무 화면에 `<fw-button func="PRINT" program="RPT_XXX" text-key="BTN.ISSUE" data-report="RPT_XXX" data-report-map="P_MONTH=log-from" />`를 넣으면 [발행] 버튼이 생깁니다(그 레포트의 PRINT 권한이 있을 때만 그려지고, 화면을 여는 READ는 서버가 다시 확인합니다). 누르면 레포트 화면이 **팝업**으로 열리고, 화면 검색 조건이 주소로 넘어가 조건 칸이 채워집니다(필수 조건이 모두 채워지면 바로 미리보기). 넘기는 조건은 `form.search-bar` 안의 이름이 `P_`로 시작하는 입력란과 `data-report-map`(`레포트조건=입력란 id 또는 name`, 쉼표로 여러 개)이 정한 입력란이며, 레포트에 정의된 조건만 받습니다. 월 조건에 날짜가 오면 그 달로 바꿉니다. 팝업에서 미리보기를 확인하고 [인쇄](브라우저 인쇄)·[PDF]·[엑셀]로 출력합니다. 개인정보 레포트는 [인쇄] 버튼이 없고 PDF·엑셀(사유·접속기록)만 씁니다. 예: 로그인·접근 기록 화면의 [발행] → 월별 접속기록 점검.

**행이 많은 레포트(백그라운드 생성)**: 미리보기·PDF·엑셀로 화면에서 바로 만드는 결과는 5,000행까지입니다(`ReportService.SyncMaxRows`). 레포트의 `MAX_ROWS`를 더 크게(최대 100,000) 등록하면 5,000행을 넘는 결과는 [PDF (백그라운드)]·[엑셀 (백그라운드)] 버튼(PDF는 PRINT, 엑셀은 EXPORT 권한)으로 요청하라고 안내합니다. 요청은 `TB_REPORT_RUN`에 쌓이고(조건은 바로 검사해 틀리면 그 자리에서 알려 줍니다, 사용자당 진행 중 5건까지), 웹 서버 안의 `ReportRunWorker`가 한 번에 하나씩 꺼내 **요청자의 권한·데이터 범위·언어로** 만듭니다(요청한 뒤 권한이 없어졌으면 실패). 결과 파일은 파일 저장소 `rpt/yyyy/MM/`에 두고, 끝나면 요청자에게 포털 알림(`TPL_REPORT_DONE`·`TPL_REPORT_FAILED`, 누르면 내 정보 > 내 레포트 `RPT_RUNS`)이 갑니다. 내 레포트 화면에서 진행을 보고 내려받거나 지웁니다. 완성된 파일은 7일 뒤 정리되고, 조건 값과 사유는 끝나면 바로 지웁니다. 개인정보 레포트는 만들 때 다운로드로 개인정보 접속기록에 남습니다. 한 요청은 20분, 만들다 서버가 멈춘 요청(30분 넘게 생성 중)은 다음에 실패로 정리됩니다. 설정 `Portal:ReportRuns`(`Enabled`, `PollSeconds`, `CleanupMinutes`)로 서비스를 끄거나 간격을 바꿀 수 있습니다. 새 레포트를 만들 때 따로 할 일은 없습니다(`MAX_ROWS`만 정하면 됩니다).

예: 월별 접속기록 점검(`RPT_LOG_MONTHLY`, `LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST`, 템플릿 `AccessMonthly`).

**화면 인쇄**: 조회 화면에 `<fw-button func="PRINT" text-key="BTN.PRINT" id="xxx-print" data-print-area="#xxx-screen" />`를 넣으면 [인쇄] 버튼이 생깁니다. 버튼은 그 화면 프로그램의 PRINT 권한이 있을 때만 그려지고, 누르면 `fw.js`가 인쇄 영역 안의 검색 조건(`form.search-bar`의 라벨과 값, 빈 값 제외)과 보이는 표(머리글과 칸 글자, 숨긴 표·빈 행 제외)를 모아 `/Rpt/Print/Pdf`로 보냅니다. 서버는 PRINT 권한을 다시 확인한 뒤 레포트와 같은 머리글(제목·조건·출력자·일시)과 바닥글·쪽 번호를 붙여 PDF를 내려 줍니다. HTML이 아닌 글자만 보내므로 서버가 인코딩해 그립니다. 열이 7개보다 많으면 가로로 출력합니다. 지금 쪽에 보이는 행만 나오고(표 5개, 행 합 5,000행까지) 전체 자료는 엑셀이나 레포트를 씁니다. 개인정보가 보이는 화면은 버튼에 `data-pii="Y"`를 붙여 사유를 묻고, 프로그램이 `PII_YN = 'Y'`이면 서버도 사유를 요구합니다(다운로드로 개인정보 접속기록). 브라우저 인쇄(Ctrl+P)는 머리·메뉴·버튼을 빼고 표를 펼쳐 찍습니다(`portal.css`의 `@media print`). 예: 로그 화면 3개.

**프레임워크 시스템 화면**(공통코드, 설정 관리 등)은 업무 모듈과 같은 구조로 `src/Portal.Web/Areas/Sys/`(Controllers, Services, Models, Views)와 `wwwroot/js/sys/`에 둡니다.

**첨부 파일**: 화면은 첨부를 직접 받지 않고 공통 API(`/File/Upload`, `/File/List`, `/File/Download/{id}`, `/File/Delete`)를 씁니다. 업무 테이블에는 `FILE_GROUP_ID BIGINT NULL` 컬럼 하나만 두고, 순서는 다음과 같습니다.

1. 화면은 첨부 부품 `FileBox`(`wwwroot/js/fw-file.js`)를 붙입니다. 파일 선택·끌어 놓기, 목록, 삭제, 모두 받기(ZIP)를 부품이 처리하고, 처음 올리면 임시 묶음이 생깁니다. 저장 요청에는 `files.getFileGroupId()`를 담습니다.

   ```js
   import { FileBox } from '../fw-file.js';
   const files = new FileBox(document.getElementById('vendor-files'), {
       programId: 'PUR_VENDOR_MGT',
       fileGroupId: vendor.fileGroupId,              // 새로 등록하면 null
       editable: !!document.getElementById('vendor-save'), // 저장 권한이 있을 때만 서버가 그린 버튼
   });
   ```
2. 업무 저장 SP가 업무 행에 `FILE_GROUP_ID`를 저장하고, 같은 트랜잭션에서 `EXEC [FW].[USP_FILE_GROUP_CONFIRM_EXEC] @P_FILE_GROUP_ID, @P_PROGRAM_ID = 'PUR_VENDOR_MGT', @P_SHARE_YN = NULL, @P_USER_ID = @P_USER_ID, @P_LANG_CD = @P_LANG_CD`를 부릅니다. 확정하지 않은 묶음은 정리 작업이 지웁니다.
3. 권한은 묶음의 프로그램으로 확인합니다. 목록·다운로드(ZIP 포함)는 `READ`, 업로드·삭제는 `CREATE` 또는 `UPDATE`가 필요합니다. 협력사 사용자는 자기 회사 묶음만 보고, 본사가 협력사에게 보여 줄 첨부(공지 등)는 확정할 때 `@P_SHARE_YN = 'Y'`를 넘깁니다.

허용 확장자는 공통코드 `FILE_EXT`, 크기·개수는 설정 `FILE.MAX_SIZE_MB`·`FILE.MAX_COUNT`가 정합니다. 실행·스크립트 형식(exe, js, html, svg 등)은 코드에 추가해도 `FileService`가 막고, 내용이 확장자와 맞는지(매직 넘버)도 확인합니다. 저장소 폴더는 `appsettings.json`의 `Portal:File:StorageRoot`(웹 루트 밖, 실행 권한 없음, 백업 대상)이고, 파일 이름은 GUID라 원래 이름은 DB에만 있습니다. IIS는 요청 본문 한도가 따로 있으므로 `FILE.MAX_SIZE_MB`를 30MB보다 크게 올리면 `web.config`의 `requestLimits maxAllowedContentLength`도 함께 올립니다.

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

- 화면에서 레이아웃을 지정하지 않습니다. `_ViewStart`가 고릅니다: 로그인 전은 `_AuthLayout`, 탭 안(`?_layout=tab` 또는 iframe 요청)은 `_TabLayout`, 그 밖에는 `_PortalLayout`입니다.
- 멀티 탭(`UI.TAB_MODE`=Y)이면 주소창으로 화면에 직접 들어와도 프레임워크가 셸(헤더·메뉴·탭)로 감싸고 그 화면을 첫 탭으로 엽니다. 화면 코드는 탭을 의식하지 않고 일반 `View()`를 돌려주면 됩니다.
- 메뉴에 보이는지는 프로그램의 READ 권한과 메뉴의 공개 범위(전체·임직원·협력사)로 정해집니다. 화면을 메뉴에 올리려면 `TB_PROGRAM`과 `TB_MENU`·`TB_MENU_LANG` 초기 데이터를 함께 추가합니다.
- 문구는 `@L["키"]`, 버튼은 `<fw-button func="UPDATE" text-key="BTN.SAVE" id="save" />`를 씁니다. `fw-button`은 현재 Controller의 `[Program]`에 그 기능 권한이 있을 때만 그려지므로, JS는 버튼이 없으면 그 기능도 쓰지 않으면 됩니다(서버는 Action의 `[Func]`로 다시 막습니다). 코드 선택 `<fw-select code-group="..." />`는 코드 캐시와 함께 추가할 예정입니다.
- 화면 제목은 메뉴명을 씁니다: `ViewData["Title"] = Menus.ProgramTitle(프로그램 ID, CurrentUser.LangCd)`, 본문 `<h1>`도 같은 값입니다. 화면 JS는 `@section Scripts { <script type="module" src="~/js/..."></script> }`로 넣습니다.
- `Html.Raw`는 금지입니다. 게시판 본문처럼 HTML이 필요하면 서버에서 필터를 거친 HTML을 `<fw-safe-html value="@Model.Content" />` 태그로만 출력합니다(그리기 직전에 한 번 더 거릅니다). 사용자가 입력한 HTML은 저장하기 전에 반드시 `Portal.Infrastructure.Html.HtmlSanitizer.Sanitize`를 거칩니다(게시판 `BoardService`가 예시). 허용 태그는 문단·목록·표·링크 정도이고, 스크립트·스타일·이미지·`on…` 속성은 지워집니다.

**JavaScript**

- 화면당 JS 파일 하나(`wwwroot/js/<모듈>/<화면>.js`), ES 모듈 형식. 외부 라이브러리를 화면에서 직접 불러오지 않습니다.
- 인라인 `<script>`와 `onclick=` 속성은 CSP에서 차단되므로 쓰지 않습니다. 이벤트는 `addEventListener`로 겁니다.
- 서버 호출은 `fw.ajax`만 씁니다(CSRF, 로딩, 오류 처리 자동). `fetch` 직접 호출 금지. 401이면 로그인 화면으로 이동하고 403이면 권한 안내를 띄우므로, 화면은 `result.success`만 보고 처리합니다.
- 문구 묶음은 비동기로 읽으므로 `fw.t`는 `await fw.ready` 이후에 씁니다. 짧은 안내는 `fw.notify(문구)`를 씁니다(셸 하단 안내 영역, 스크린리더가 읽음).
- 사용자 데이터는 `textContent` 또는 그리드 컴포넌트로만 넣고 `innerHTML`에 직접 넣지 않습니다.
- 메시지는 `fw.t('MSG.SAVED')`, 확인창은 `await fw.dialog.confirm(fw.t('CNF.SAVE'))`, 사유 같은 입력은 `await fw.dialog.prompt(문구, { label })`(취소하면 `null`)를 씁니다(브라우저 `confirm()`·`prompt()` 금지). 날짜·숫자 표시용 `fw.format`은 조회형 화면 작업에서 추가합니다.
- 편집 그리드는 `fw-grid.js`의 `Grid`를 씁니다. 컬럼에 `required`, `maxLength`, `pattern`을 주면 `validate()`가 서버와 같은 메시지 키로 검사하고 오류 칸으로 이동합니다. 여러 행 저장은 `getChanges()`(I/U/D 행)를 보내 TVP 일괄 저장 SP로 처리합니다. 행마다 요청을 보내지 않습니다. 저장하지 않은 변경이 있으면(`hasChanges()`) 다른 항목으로 옮기기 전에 `CNF.LEAVE`로 확인합니다. 행마다 칸 종류가 다르면 `type`·`required`·`placeholder`에 `(row) => 값` 함수를 주고(예: 설정 관리의 형식별 값 칸), 읽기 전용 칸의 표시는 `format`, 행별 버튼은 `type: 'button'`으로 그립니다. 행 추가·삭제가 없는 수정 전용 그리드는 `selectable: false`로 선택 칸을 뺍니다. 서버의 UTC 일시는 `fw.format.dateTime()`으로 보여 줍니다.
- 첨부는 `fw-file.js`의 `FileBox`를 씁니다(3장 "첨부 파일"). 파일 올리기는 `fw.upload()`, 크기 표시는 `fw.format.fileSize()`입니다. 첨부 API를 화면에서 직접 부르지 않습니다.

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
- 저장 SP는 `@P_ROW_VER`로 동시성을 검사하고, 감사 대상 테이블이면 `LOG.USP_AUDIT_WRITE`를 호출합니다. 등록·수정을 한 SP로 처리할 때는 `@P_ROW_VER = NULL`을 등록으로 봅니다(C#에서 `byte[]?`를 null로 넘기면 바이너리 NULL로 갑니다).
- 기준정보·코드·메뉴·권한·다국어·설정을 바꾸는 SP는 마지막에 `TB_CACHE_VERSION`을 올립니다. 빠뜨리면 화면에 변경이 반영되지 않습니다.
- 테이블 변경은 호환성을 유지합니다: 컬럼은 NULL 허용 또는 기본값과 함께 추가하고, 삭제·이름 변경은 다음 릴리스로 미룹니다(롤백 대비).
- 초기·등록 데이터 스크립트는 `MERGE`로 작성해 여러 번 실행해도 같은 결과가 나오게 합니다.
- 작성자·변경 이력은 SP 주석이 아니라 Git 이력으로 관리합니다. SP 첫 줄 주석에는 용도와 프로그램 ID만 씁니다.
- 업무 처리 결과를 메일로 알릴 때는 그 업무 SP 안에서 `FW.USP_MSG_QUEUE_SAVE`를 `@P_RESULT_YN = 'N'`으로 부릅니다. 업무와 같은 트랜잭션으로 대기열에 쌓이고(업무가 롤백되면 메일도 사라짐), 발송은 Portal.Worker가 합니다(본문은 발송할 때 받는 사람 언어의 템플릿으로 만들고, 치환값은 HTML 인코딩됩니다). 받는 사람은 `@P_TO_USER_ID`(사용자의 업무 이메일·언어 사용)로 주고, 치환값 JSON에는 비밀번호·인증 번호를 넣지 않습니다. 예: `USP_PARTNER_JOIN_APPROVE_EXEC`.
- 같은 호출이 포털 알림함에도 쌓습니다. 템플릿의 `PORTAL_YN = 'Y'`이고 받는 사람이 포털 사용자이면 됩니다. 알림을 눌렀을 때 열 화면은 템플릿의 `LINK_PROGRAM_ID`로 정합니다. 권한이 있는 사람 모두에게 보낼 때는 `FW.UFN_FUNC_USERS('프로그램', '기능')`로 받는 사람을 고릅니다. 평가 규칙은 화면 권한과 같습니다. 예: `USP_PARTNER_JOIN_SAVE`가 승인 담당자에게 보내는 가입 신청 알림.
- 화면이 사용자 조작 없이 주기적으로 부르는 조회는 `X-Portal-Background: 1` 헤더를 붙여 세션을 연장하지 않게 하고, Action에 `[SkipAccessLog]`를 붙여 접속기록에서 뺍니다. 개인정보를 다루는 조회에는 붙이지 않습니다. 예: 헤더 알림 배지.
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
- 접속기록·감사 로그는 프레임워크가 자동으로 남기므로 화면에서 따로 기록하지 않습니다. 개인정보를 파일로 내려주거나 마스킹을 풀 때만 Service가 `IPiiAccessLog`로 사유와 함께 기록합니다(3장 '개인정보가 든 파일 다운로드'). 개인정보 처리 화면이면 `TB_PROGRAM.PII_YN = 'Y'`로도 등록합니다.
- 개인정보를 불가피하게 저장할 때(협력사 업무용 이메일 등)는 컬럼을 `..._ENC VARBINARY`로 두고 `IPersonalDataProtector.Protect`로 암호화한 값만 SP에 넘깁니다. 목록 화면에는 평문 대신 `PersonalDataMask`로 가린 값(`ab***@example.com`)을 보내고, 암호문 속성에는 `[JsonIgnore]`를 붙입니다. 감사 로그 JSON에도 넣지 않습니다. 예: 협력사 가입 신청(`PartnerJoinService`), 협력사 계정 승인 화면.

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
| 단위 | `tests/Portal.UnitTests/<모듈>/` | Service에 업무 규칙을 추가·변경할 때 | DB 없이, `Portal.Testing`의 Fake(`FakeSpExecutor`, `FakeCurrentUser`, `FakeTimeProvider`, `FakePortalConfig`)로 의존성 교체 |
| SP 통합 | `tests/Portal.IntegrationTests/<모듈>/` | SP를 추가·변경할 때 (SP당 1개 이상) | 테스트마다 트랜잭션을 열고 끝나면 롤백. 데이터는 테스트 안에서 만듦 |
| 웹 통합 | `tests/Portal.IntegrationTests/Web/` | 권한·인증·API 응답 형식에 영향을 주는 변경 | `PortalWebFactory`가 실제 파이프라인을 띄우고 DB만 `FakeSpExecutor`로 바꿈. `SignIn(사용자, ["프로그램:기능"])`으로 세션·권한 지정 (DB 없이 실행) |
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

로컬에서는 `dotnet test`, 통합 테스트만 돌리려면 `dotnet test --filter Category=Integration`, DB 없이 단위 테스트만 돌리려면 `--filter Category!=Integration`을 씁니다. 테스트 DB는 `db/ops/local-setup.ps1`이 만든 로컬 DB(`PORTAL_LOCAL`)를 쓰고, 다른 DB를 쓰려면 환경 변수 `PORTAL_TEST_DB`에 연결 문자열을 넣습니다. 통합 테스트 클래스에는 `[Collection(DbTests.Name)]`, `[Trait("Category", "Integration")]`을 붙이고 `DbFixture.BeginAsync()`로 롤백되는 트랜잭션 안에서 실행합니다.

**언제 돌리나:** 기능을 개발하는 동안(기능 MR마다)은 DB 없는 테스트(`--filter Category!=Integration`)만 돌립니다. DB 통합 테스트(`Category=Integration`)는 개발 단계가 끝날 때와 운영 배포 전에 돌립니다(CI에서는 `db-integration` 작업을 수동 실행: GitHub Actions의 Run workflow, GitLab 파이프라인의 수동 작업). 단위 테스트에서 문제가 생기면 그때 통합 테스트까지 돌립니다. 기능 MR에서 통합 테스트로만 확인할 수 있는 점이나 예상되는 문제는 MR 본문에 적고, 단계 종료 통합 테스트 때 확인합니다. 새 SP의 통합 테스트 코드는 기능 MR에 함께 넣습니다.

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
| 화면을 끄거나 버튼(기능) 추가, 개인정보 처리 화면 표시 | 시스템 관리 > 프로그램 관리(`SYS_PROGRAM_MGT`, `PORTAL_ADMIN`)에서 프로그램을 골라 관리용 이름, 개인정보 처리 화면 여부(`PII_YN`), 사용 여부와 쓰는 기능(조회·등록·수정·삭제·내보내기·출력 등)을 고칩니다. 체크한 기능이 역할별 권한 화면의 체크 칸이 됩니다. 조회는 항상 쓰고, 서버 경로(`TB_PROGRAM_API`)가 요구하는 기능은 코드가 쓰므로 뺄 수 없습니다. 기능을 빼면 그 기능의 역할·사용자 예외 권한도 지워집니다. 사용 안 함으로 바꾸면 메뉴에서 빠지고 누구도 열 수 없습니다(홈과 프로그램 관리는 끌 수 없음). 프로그램 ID·모듈·화면 주소는 코드가 정하므로 등록할 때만 넣고, 새 업무 화면은 보통 3장 순서대로 시드 스크립트로 등록합니다. 서버 경로·메뉴·첨부 파일·메시지 템플릿이 연결된 프로그램은 지울 수 없습니다. 변경은 감사 로그에 남습니다 | 없음 |
| 새 역할 만들기, 역할의 데이터 범위 바꾸기 | 시스템 관리 > 역할 관리(`SYS_ROLE_MGT`, `PORTAL_ADMIN`)에서 [신규]로 역할 ID(영문 대문자로 시작하는 대문자·숫자·밑줄 2~30자, 저장 후 변경 불가), 역할명, 설명, 줄 수 있는 회사(모든 회사·본사·협력사), 데이터 범위, 정렬 순서를 넣습니다. 만든 역할은 역할별 권한 화면에서 화면·버튼을 체크하고, 사용자·부서 관리에서 부여합니다. 협력사 역할은 데이터 범위를 전체로 둘 수 없고, 이미 부여했거나 기능 권한이 있는 역할은 회사 유형을 바꿀 수 없습니다. 시스템 역할(초기 데이터 7개)은 지우거나 사용 안 함·회사 유형 변경을 할 수 없고, 시스템 관리자 역할은 시스템 관리자만 이름·설명·정렬 순서를 고칩니다. 자기가 가진 역할은 다른 관리자가 고칩니다. 사용 안 함으로 바꾸면 그 역할의 권한이 바로 빠지고, 계정·부서·AD 그룹·위임에 부여되지 않은 역할만 지울 수 있습니다(기능 권한도 함께 삭제). 변경은 감사 로그에 남습니다 | 없음 |
| 특정 역할에 버튼 권한 부여 | 시스템 관리 > 역할별 권한(`SYS_ROLE_AUTH_MGT`, `PORTAL_ADMIN`)에서 역할을 고르면 화면 × 기능(조회·등록·수정·삭제·내보내기·출력·업로드·승인·실행·개인정보) 체크표가 나옵니다. 체크한 버튼만 그 역할의 사용자에게 보이고 서버에서도 허용되며, 저장하면 바로 적용됩니다. 다른 기능을 켜면 조회도 켜지고 조회를 끄면 그 화면 기능이 모두 꺼집니다. 시스템 관리자 역할은 코드에서 모든 권한을 가지므로 고치지 않고, 자기가 가진 역할은 다른 관리자(또는 시스템 관리자)가 고칩니다. 본사용 역할에는 협력사 전용 화면을, 협력사용 역할에는 본사 전용 화면을 새로 줄 수 없습니다(메뉴의 공개 대상 기준). 변경 전후 목록은 감사 로그에 남습니다 | 없음 |
| 한 사람에게만 버튼 권한을 더 주거나 막기 | 시스템 관리 > 사용자별 예외 권한(`SYS_USER_AUTH_MGT`, `PORTAL_ADMIN`)에서 사용자를 고르면 그 사람의 예외 권한 목록과 기능별 역할 권한 여부가 나옵니다. 화면·기능·유형(추가 허용 또는 차단)·종료일로 줄을 추가하거나 지우고 저장하면 바로 적용됩니다. 차단은 역할 권한보다 우선하고, 종료일이 지나면 적용되지 않습니다(기본 종료일은 90일 뒤). 예외 권한은 버튼과 서버 기능만 바꾸며 볼 수 있는 데이터 범위는 역할을 따릅니다. 대상 회사 유형에 열리지 않은 화면(본사 사용자의 협력사 전용 화면 등)은 차단만 할 수 있고, 자기 예외 권한은 시스템 관리자만 고치며, 시스템 관리자에게는 예외 권한을 두지 않습니다. 오래 쓸 권한은 역할로 주는 것이 원칙입니다. 변경 전후 목록은 감사 로그에 남습니다 | 없음 |
| 로그인 실패·화면 접근 기록 확인 (보안 점검, 장애 추적) | 로그 > 로그인·접근 기록(`LOG_ACCESS_INQ`, `PORTAL_ADMIN`·`SECURITY_AUDITOR`)에서 기록 종류(로그인 / 화면·API 접근)를 고르고 기간(31일까지), 사용자 ID, 결과, 화면, 오류만, 접속 IP로 찾습니다. 사용자 ID와 IP는 정확히 일치하는 값만 찾고, 실패·오류 행은 빨간 글씨로 보입니다. 오류 화면의 추적 ID로 접근 기록을 찾으면 오류 로그와 맞춰 볼 수 있습니다. 데이터 범위가 전체인 역할은 없는 ID로 시도한 로그인과 로그인 전 요청까지 보고, 그 밖에는 범위 안 계정의 기록만 봅니다. [엑셀]은 지금 조건의 기록을 10,000건까지 내려받으며(`EXPORT` 권한), 접속 IP가 들어 있어 사유를 묻고 개인정보 접속기록에 남깁니다. 기록은 10년 보관하며 화면에서 고치거나 지울 수 없습니다 | 없음 |
| 누가 언제 어떤 데이터를 바꿨는지 확인 (데이터 변경 추적, 감사 대응) | 로그 > 데이터 변경 기록(`LOG_AUDIT_INQ`, `SECURITY_AUDITOR`)에서 기간(31일까지), 테이블, 작업(등록·수정·삭제), 대상 키(앞부분 일치), 변경자, 접속 IP로 찾습니다. 일시를 누르면 변경 전후 값을 항목별로 나란히 보여 주고 바뀐 항목은 굵게 표시하며, 목록 값(예: 사용자 예외 권한)은 빠진 항목(-)과 더한 항목(+)으로 보여 줍니다. 변경 내용에는 여러 회사의 데이터가 섞여 있어 데이터 범위가 전체인 역할만 볼 수 있고, 그 밖에는 아무것도 보이지 않습니다. 한 건을 열 때마다 개인정보 접속기록(조회)이 남고, [엑셀]은 변경 전후 값을 포함해 5,000건까지 내려받으며(`EXPORT` 권한) 사유를 묻습니다. 기록은 변경 SP가 같은 트랜잭션에서 남기므로 화면에서 고치거나 지울 수 없습니다 | 없음 |
| 개인정보를 누가 언제 왜 보거나 내려받았는지 확인 (개인정보 접속기록 점검) | 로그 > 개인정보 접속기록(`LOG_PII_INQ`, `SECURITY_AUDITOR`)에서 기간(31일까지), 사용자 ID, 화면, 작업(조회·다운로드·마스킹 해제), 접속 IP로 찾습니다. 대상에는 건수와 검색 조건만 있고 개인정보 원문은 없으며, 다운로드와 마스킹 해제는 사유가 함께 보입니다. 데이터 범위가 전체인 역할은 모든 기록을, 그 밖에는 범위 안 계정의 기록만 봅니다. [엑셀]은 10,000건까지 내려받으며(`EXPORT` 권한), 사용자 ID와 접속 IP가 들어 있어 사유를 묻고 이 내려받기도 개인정보 접속기록에 남깁니다 | 없음 |
| 로그 무결성 점검 결과 확인, 실패했을 때 | 매월 1일 06:00에 작업 `JOB_LOG_INTEGRITY`가 지난달 로그인·접근·데이터 변경·개인정보 접속기록의 해시 체인을 다시 계산합니다. 결과는 운영 > 스케줄 관리의 실행 이력에 "yyyy-MM 이상 없음 (로그별 건수)"로 남고, 지난달을 다시 보려면 [지금 실행]을 누릅니다. 실패하면 `PORTAL_ADMIN`에게 메일과 알림이 가며, 이력의 `LOG_INTEGRITY_BROKEN: yyyy-MM 테이블 LOG_ID n`은 그 테이블에서 처음 깨진 행입니다(그 행의 내용이 바뀌었거나 바로 앞 행이 지워짐). 이때는 로그를 고치지 말고 보안 담당자에게 알린 뒤, 백업본의 같은 행과 비교해 바뀐 내용과 시점을 확인합니다 | 없음 (보안 사고 절차) |
| 협력사 추가, 협력사 관리자 지정 | 시스템 관리 > 회사 관리(`SYS_COMPANY_MGT`, `PORTAL_ADMIN`)에서 [신규]로 협력사를 등록합니다(코드는 P0001부터 자동 부여, 사업자등록번호 필수·중복 불가). 협력사 관리자는 사용자 관리에서 지정합니다 | 없음 |
| 계정 잠금 해제, 퇴사·계약 종료 처리, 역할 부여 | 시스템 관리 > 사용자 관리(`SYS_USER_MGT`, `PORTAL_ADMIN`)에서 사용자를 골라 [잠금 해제](비밀번호 실패 잠금), 상태(사용·잠김·휴면·퇴사·계약 종료, 사용 외 상태는 바로 로그아웃), 계정 사용 기한, 소속 부서, 직접 부여 역할을 바꿉니다. 부서는 같은 회사의 사용 중인 부서로만 옮길 수 있고(지금 부서가 사용 안 함이면 그대로 둘 수는 있음), 부서를 바꾸면 부서 역할이 바로 다시 계산됩니다. 계정은 지우지 않고 상태로 둡니다. 협력사 사용자에게는 본사 역할을 줄 수 없고(역할의 `COMPANY_TYPE_CD`), 자기 계정의 상태·부서·역할과 시스템 관리자 계정은 바꿀 수 없습니다(시스템 관리자만 가능). 변경은 감사 로그에 남습니다. [엑셀]은 지금 검색 조건의 사용자를 5,000명까지 내려받으며(`EXPORT` 권한), 이름이 개인정보라서 사유를 묻고 개인정보 접속기록에 남깁니다 | 없음 |
| 휴면 계정 해제 (오래 로그인하지 않아 휴면이 된 사용자가 로그인할 수 없다고 할 때) | 작업 `JOB_USER_DORMANT`(매일 04:30)가 `USER.DORMANT_DAYS`(기본 365일)에서 `USER.DORMANT_NOTICE_DAYS`(기본 30일)를 뺀 기간 동안 로그인하지 않은 계정에 안내 메일을 보내고, 안내 후에도 로그인하지 않으면 휴면으로 바꿉니다. 휴면 계정은 "사용할 수 없는 계정입니다" 메시지로 로그인이 막힙니다. 시스템 관리 > 사용자 관리에서 본인 확인 후 상태를 [사용]으로 바꾸면 바로 로그인할 수 있고, 해제한 날부터 다시 미접속 기간을 셉니다. 휴면 전환은 감사 로그(데이터 변경 기록)에 `SYSTEM`이 바꾼 것으로 남습니다 | 없음 |
| 만료된 협력사 계정 되살리기 (계약을 연장한 협력사 사용자가 로그인할 수 없다고 할 때) | 작업 `JOB_PARTNER_EXPIRE`(매일 04:40)가 사용 기한(계정 사용 기한과 회사 계약 종료일 중 이른 날)이 `PARTNER.EXPIRE_NOTICE_DAYS`(기본 7일) 안으로 다가온 협력사 계정에 안내 메일을 보내고, 기한이 지나면 상태를 [기간 만료]로 바꿉니다. 회사 전체 계약을 연장했으면 시스템 관리 > 회사 관리에서 계약 종료일을 늘리면, 그 날짜를 따르던 계정의 기한이 함께 늘고 [기간 만료] 계정은 [사용]으로 돌아옵니다. 계정마다 따로 기한을 정했으면 사용자 관리에서 기한을 늘리고 상태를 [사용]으로 바꿉니다(기한만 늘리고 상태를 그대로 두면 계속 로그인할 수 없습니다). 만료 전환은 감사 로그에 `SYSTEM`이 바꾼 것으로 남습니다 | 없음 |
| 협력사 관리자가 자기 회사 사용자 관리 | 협력사 > 협력사 사용자 관리(`PTN_USER_MGT`, `PARTNER_ADMIN`)는 사용자 관리와 같은 화면이지만 자기 회사 사용자만 보이고 고칠 수 있습니다(SP가 데이터 범위 `UFN_DATA_SCOPE`로 강제, 다른 회사 사용자는 "없음"으로 응답). 줄 수 있는 역할은 협력사 역할(`PARTNER_ADMIN`, `PARTNER_USER`)뿐이고, 자기 회사 부서 지정, 잠금 해제·2단계 인증 초기화와 [엑셀] 내보내기(`EXPORT`, 기본으로 협력사 관리자에게 부여, 역할별 권한에서 뺄 수 있음, 사유를 받아 개인정보 접속기록에 남김)도 할 수 있습니다. 변경은 감사 로그에 남습니다 | 없음 |
| 부서 만들기·개편, 부서 역할 | 시스템 관리 > 부서 관리(`SYS_DEPT_MGT`, `PORTAL_ADMIN`)에서 회사를 고르고 트리에서 부서를 골라 이름·상위 부서·정렬 순서·사용 여부와 부서 역할을 고칩니다. [신규]는 고른 부서 아래에 만들고 부서 ID는 `D000001`부터 자동으로 붙습니다. 부서 역할은 그 부서 소속 계정이 받고, '하위 부서에도'를 켜면 아래 부서 계정도 받습니다(시스템 관리자 역할은 부서로 줄 수 없고, 회사 유형이 맞는 역할만). 사용 안 함 부서는 역할을 주지 않으며, 사용 중인 하위 부서가 있으면 사용 안 함으로 바꿀 수 없습니다. 하위 부서나 소속 계정(퇴사자 포함)이 있는 부서는 지울 수 없으니 조직 개편은 사용 안 함으로 둡니다. AD 동기화 부서(3단계)는 역할·정렬 순서만 바꿉니다. 역할이 바뀌면 권한이 바로 다시 계산되고 변경은 감사 로그에 남습니다 | 없음 |
| 협력사 계약 연장·종료, 접속 IP 변경 | 시스템 관리 > 회사 관리에서 계약 종료일·접속 허용 IP·사용 여부를 고칩니다. 계약 종료일을 바꾸면 이전 종료일을 사용 기한으로 쓰던 협력사 계정의 기한도 함께 바뀌고(따로 정한 기한은 그대로), 사용 안 함으로 바꾸면 그 회사 사용자는 바로 로그아웃됩니다. 허용 IP는 한 줄에 하나씩 CIDR 또는 IP로 넣고 다음 로그인부터 적용되며, 자기 회사 목록에서 지금 접속한 주소를 빼는 저장은 거부합니다. 계정·부서·가입 신청·첨부 파일이 있는 회사는 지울 수 없으니 사용 안 함으로 바꿉니다 | 없음 |
| 공통코드 값 추가·숨기기 | 시스템 관리 > 공통코드 (삭제 대신 사용 안 함) | 없음 |
| 화면 문구·번역 수정 | 시스템 관리 > 다국어 관리 | 없음 |
| 정책값 변경 (세션 시간, 비밀번호 규칙, 업로드 크기 등) | 시스템 관리 > 설정 관리 (`RESTART_YN = 'Y'` 항목은 앱 풀 재시작 필요) | 없음 |
| 게시판 추가 | 게시판 관리에서 생성 후 메뉴에 연결 | 없음 |
| 대시보드 위젯 추가 | 데이터 SP 작성 → 위젯 관리에 등록 | SP만 |
| 레포트 추가 | 데이터 SP + Razor 템플릿 작성 → 레포트 관리에 등록 | SP + 템플릿 |
| 배치 작업 추가 | DB만 다루면 `FW.USP_JOB_<대상>_EXEC` SP(결과 행 `PROCESSED_CNT`, 많은 행은 5000건씩 나눠 처리)를, 파일·외부 연동이 필요하면 `IScheduledJob` 구현(Portal.Worker에 `AddScoped<IScheduledJob, 내작업>()`으로 등록)을 만들고, `db/seed/FW/11_JOB.sql`에 작업 ID·주기(Cron, 한국 시간)·제한 시간을 추가합니다. 같은 작업은 Worker가 여러 대여도 동시에 돌지 않고, 놓친 예약은 한 번만 실행합니다. 결과 메시지에는 개인정보를 넣지 않습니다 | SP 또는 클래스 |
| 배치 작업 즉시 실행·주기 변경 | 운영 > 스케줄 관리(`SYS_JOB_MGT`, `PORTAL_ADMIN`)에서 작업을 골라 주기·제한 시간·사용 여부·설명을 저장하거나 [지금 실행]을 누릅니다. 실행 대상(SP·클래스)은 화면에서 바꾸지 않고 배포로만 바꿉니다. 주기를 바꾸거나 다시 사용하면 다음 실행 시각을 새로 계산하고, 실행은 15초 안에 시작합니다. 결과는 같은 화면의 실행 이력(`FW.TB_JOB_HIST`)에서 보고, 변경·실행 요청은 감사 로그에 남습니다 | 없음 |
| Portal.Worker 설치 | 웹과 같은 연결 문자열, Data Protection 키 폴더(`Portal:DataProtection:KeyPath`), 첨부 저장소(`Portal:File:StorageRoot`)를 `appsettings.Production.json`에 넣고 `sc.exe create PortalWorker binPath= "...\Portal.Worker.exe" start= delayed-auto`로 서비스를 등록합니다. 시간대는 `Portal:Scheduler:TimeZone`(기본 Asia/Seoul) | 없음 |
| 외부 API 클라이언트 추가 | 연계 관리 > API 클라이언트 (비밀값은 발급 시 1회만 표시) | 없음 |
| 새 언어 추가 | `TB_LANG`에 행 추가 → 다국어 관리에서 엑셀로 일괄 번역 입력 | 없음 |
| 새 설정 키 필요 | `ConfigKeys`에 상수 추가 + `db/seed`에 기본값 + 초기 데이터 정의서 갱신 | 있음 |
| 비밀값 설정 (SMTP·AD 계정 비밀번호 등) | 설정 관리에서 새 값을 입력하면 Data Protection(`ISecretProtector`)으로 암호화해 저장합니다. 화면·이력·감사 로그에는 값이 남지 않고, 키 폴더(`Portal:DataProtection:KeyPath`)를 잃으면 다시 입력해야 합니다 | 없음 |
| 협력사 계정 발급 | 회사 관리에 협력사(사업자등록번호, 계약 종료일)를 먼저 등록합니다. 협력사 담당자가 로그인 화면의 "가입 신청"에서 신청하면 시스템 관리 > 협력사 계정 승인에서 승인하거나 사유를 적어 반려합니다. 승인하면 협력사 사용자 역할이 부여되고, 계정 사용 기한은 회사 계약 종료일입니다 | 없음 |
| 협력사 전용 로그인 주소 | 협력사 직원에게는 `/Account/PartnerLogin`을 안내합니다(ID/PW + 2단계 인증 + 가입 신청 링크). 이 화면은 협력사 계정만 통과시키고 본사 계정으로 들어오면 비밀번호 오류와 같은 메시지로 거절하며(실패 횟수에는 합산), 본사 로그인 화면에도 협력사 로그인 링크가 있습니다. AD SSO 적용 전까지 본사 화면(`/Account/Login`)은 협력사 계정도 받습니다 | 없음 |
| 2단계 인증을 이메일 인증번호로 허용·금지 | 시스템 관리 > 설정 관리에서 `AUTH.PARTNER_EMAIL_OTP`(협력사, 기본 Y), `AUTH.ADMIN_EMAIL_OTP`(관리자 역할, 기본 N)을 바꿉니다. 등록된 이메일이 있는 계정의 2단계 인증 화면에 [이메일로 인증번호 받기]가 나옵니다. 메일 발송(`MAIL.ENABLED`, SMTP 설정)이 켜져 있어야 하며, 꺼진 환경(개발계)에서는 보내기가 실패 안내로 끝납니다. 번호 문구는 언어 리소스 `MAIL.OTP_SUBJECT`·`MAIL.OTP_BODY`입니다 | 없음 |
| 2단계 인증 재등록 (휴대폰 교체·분실) | 본인 확인 후 시스템 관리 > 사용자 관리에서 사용자를 골라 [2단계 인증 초기화]를 누릅니다(자기 계정은 다른 관리자가 해야 함). 다음 로그인에서 QR 등록 화면이 다시 나옵니다. Data Protection 키 폴더를 잃으면 모든 등록이 무효가 되므로 키 폴더를 백업합니다 | 없음 |
| 메일 발송 켜기 | 설정 관리에서 `MAIL.SMTP_HOST`·`MAIL.SMTP_PORT`·`MAIL.SMTP_SSL`·`MAIL.FROM`(필요하면 `MAIL.SMTP_USER`·`MAIL.SMTP_PASSWORD`)과 메일 속 링크 주소 `SITE.BASE_URL`을 넣고 `MAIL.ENABLED = Y`로 바꿉니다(개발계는 N 유지). 발송은 Windows 서비스 PortalWorker가 1분마다 대기열을 읽어 합니다. 서비스는 웹과 같은 DB 연결 문자열과 같은 Data Protection 키 폴더(`Portal:DataProtection:KeyPath`)를 써야 암호화한 주소·비밀번호를 풀 수 있습니다. 실패하면 5분·10분 뒤 다시 보내고 3번 실패하면 FAILED로 남기며, `TB_MSG_QUEUE.ERROR_MSG`에 원인 코드(`SMTP_...`, `ADDRESS_INVALID` 등, 주소는 남기지 않음)가 남습니다 | 없음 |
| 화면 변경이 반영 안 됨 | 운영 모니터링 > 캐시 강제 갱신. 자주 발생하면 해당 SP의 `TB_CACHE_VERSION` 누락 확인 | 경우에 따라 |

관리 화면에서 바꾼 값은 설정 이력·감사 로그에 남으므로 누가 언제 바꿨는지 나중에 확인할 수 있습니다.
