# 01. 프레임워크 사용·확장 안내 (업무 개발자용 요약)

기준 시점: 2026-10-23 (`main` `cdecf6f`). 자세한 규칙은 [개발 표준 가이드](../dev-guide.md)와 [설계서](../framework-design.md)가 기준입니다. 이 문서는 업무 화면을 만들 때 꼭 알아야 할 것만 모았습니다. 기능별 구현 여부는 [02-framework-status.md](02-framework-status.md)를 봅니다.

## 1. 한눈에 보기

| 항목 | 내용 |
| --- | --- |
| 실행 환경 | Windows Server 2022 + IIS, SQL Server 2022. 운영계·개발계 각각 웹 1대 + DB 1대 |
| 기술 | .NET 10 (LTS), ASP.NET Core MVC/Razor 모듈형 모놀리스, 배치는 Windows 서비스 `Portal.Worker` |
| DB 접근 | **SP만** 사용. EF Core·인라인 SQL 금지. `ISpExecutor`(Microsoft.Data.SqlClient 위 자체 구현) 하나로 호출. 앱 DB 계정은 SP `EXECUTE` 권한만 가짐(DB 역할 `portal_app`) |
| 사용자 | 본사 임직원 + 다수 협력사. 협력사는 자기 회사 데이터만 봄(데이터 범위 함수) |
| 로그인 | ID/PW + 관리자·협력사 2단계 인증(TOTP 또는 이메일 인증번호). 협력사 전용 로그인 `/Account/PartnerLogin`. AD SSO(도메인 PC 자동 로그인)는 **프레임워크 범위 밖, 업무 저장소에서 개발** |
| 언어 | 한국어 `ko`, 영어 `en`, 중국어 `zh-CN`. 화면 문구는 모두 다국어 키 |
| UI | 헤더 + 좌측 메뉴 트리 + 멀티 탭. 자체 JS 컴포넌트(`fw.*`), Bootstrap 5. 외부 CDN 없음 |
| OSS | 허용 목록(`eng/allowed-packages.txt`)에 있는 것만. GPL/AGPL·매출 기준 유료 라이선스 금지 |
| 원칙 | 하드코딩 금지(문구·URL·정책 숫자·코드값은 다국어 키·설정·공통코드), 개인정보 저장 안 함(불가피하면 암호화), 접속기록 10년 보관 |

## 2. 솔루션 구조와 참조 방향

```
src/
  Portal.Core            공통 계약: ApiResult, PageResult, BizException, ConfigKeys·MessageKeys·FuncCodes, ICurrentUser, 보호기 인터페이스
  Portal.Data            ISpExecutor, TVP, 결과 매퍼(SpRow), SP 오류 변환, 트랜잭션
  Portal.Security        인증(ID/PW, MFA), 권한 평가, 비밀번호, 비밀값·개인정보 암호화, 접속 IP 검사
  Portal.Infrastructure  캐시, 설정, 다국어, 로그, 파일, 엑셀, HTML 필터, 메일 큐·발송, PDF, 스케줄러
  Portal.Framework.Web   PortalController, [Program]/[Func] 권한 필터, 세션 인증, 보안 헤더, 접근 로그, Tag Helper, 셸 레이아웃
  Portal.Web             웹 호스트 + 화면(Areas: Sys, Ptn, Log, Rpt, Board, Dash) + wwwroot(fw.js, fw-grid.js, fw-tree.js, fw-file.js)
  Portal.Worker          Windows 서비스: 스케줄러, 메일 발송
db/
  Portal.Database        SQL 프로젝트(DACPAC): 스키마별 Tables / StoredProcedures / Functions / Types
  seed/FW                초기 데이터 MERGE 스크립트(배포 후 자동 실행, 반복 실행 가능)
  ops                    로컬 설치 등 운영 스크립트
tests/
  Portal.Testing         공용 Fake(FakeSpExecutor, FakeCurrentUser, FakeTimeProvider, FakePortalConfig ...)
  Portal.UnitTests       단위 + 아키텍처 테스트(참조 방향, 허용 패키지, 금지 패턴)
  Portal.IntegrationTests  SQL Server 통합 테스트(테스트마다 롤백) + 웹 테스트(Web/, DB 없이)
```

- 참조 방향은 `tests/Portal.UnitTests/Architecture/ProjectReferenceTests.cs`가 강제합니다. `Portal.Modules.*` 프로젝트는 프레임워크 5개(Core, Data, Security, Infrastructure, Framework.Web)만 참조할 수 있습니다.
- **현재 화면은 모두 `Portal.Web/Areas/<영역>`에 있습니다.** 설계서 4장의 `Portal.Modules.<업무>` 별도 프로젝트 방식은 아직 쓰는 곳이 없고, 권한 매트릭스 테스트(`PermissionMatrixTests`)가 `Portal.Web` 어셈블리의 컨트롤러만 검사하며, 별도 프로젝트에 Razor 뷰를 두는 설정(Razor 클래스 라이브러리)도 아직 없습니다. 업무 저장소의 배치 방법은 [04 문서](04-new-business-repo.md) 4장을 봅니다.
- 금지 패턴(아키텍처 테스트가 검사): EF Core, 인라인 SQL, `DateTime.Now`(→ `TimeProvider`), `Html.Raw`, JS `innerHTML`.

## 3. 명명 규칙

| 대상 | 규칙 | 예 |
| --- | --- | --- |
| 스키마 | 프레임워크 `FW`, 로그 `LOG`, 업무는 모듈별 스키마 | `GAT`(경비대 반출입, 제안) |
| 테이블 | `스키마.TB_대상[_세부]`, 다국어 명칭은 `_LANG` 테이블 | `FW.TB_CODE`, `FW.TB_CODE_LANG` |
| 컬럼 | 대문자 스네이크 + 성격 접미어 `_ID _CD _NM _YN _DT _CNT` | `USE_YN`, `REG_DT` |
| 공통 컬럼 | `REG_ID, REG_DT, UPD_ID, UPD_DT, ROW_VER rowversion` | 낙관적 잠금은 `ROW_VER` |
| SP | `스키마.USP_대상_동작`, 동작은 6개 고정 | `GAT.USP_CARRY_OUT_GET_LIST` |
| 함수 / TVP | `스키마.UFN_…` / `스키마.TVP_…` | `FW.UFN_DATA_SCOPE` |
| 인덱스 | `IX_테이블_컬럼`, 고유 `UX_` | |
| 프로그램 ID | `<모듈>_<대상>_<유형>`, 유형 `MGT`(관리) `INQ`(조회) `RPT`(레포트) `POP`(팝업) | `GAT_OUT_HIST_INQ` |
| 다국어 키 | `LBL.` `BTN.` `MSG.` `VAL.` `CNF.` 공통, 화면 전용은 `<모듈>.<화면>.` | `GAT.OUT.TITLE` |
| C# | .NET 기본 규칙, DTO는 `…Dto`, 검색 조건 `…Search`, 비동기 `…Async` | `CarryOutService.GetListAsync` |
| SP 오류 번호 | `FW` 50001~50999, `BRD` 51000~, `RPT` 52000~, 이후 모듈마다 1,000 단위 | 업무 모듈은 53000~부터 배정 제안 |

**기존 업무 테이블 예외**(사용자 결정 2026-10-23, 설계서 7장): 기존 DB의 업무 테이블(`dooinfoiv.CARRY_IN*`, `dooinfoiv.CARRY_OUT*`)은 이름·컬럼을 그대로 씁니다. 위 규칙은 **새로 만드는 테이블과 SP에만** 적용합니다. 자세한 내용은 [03 문서](03-business-gate.md) 5장.

## 4. SP 규칙 (설계서 8장 요약)

| 접미어 | 용도 | 반환 |
| --- | --- | --- |
| `_GET_LIST` | 목록(페이징) | 1) 목록 2) 전체 건수 |
| `_GET` | 단건 | 1행 (+하위 결과셋) |
| `_GET_TREE` | 트리 | 레벨·경로 포함 목록 |
| `_SAVE` | 등록/수정(단건 또는 TVP 일괄) | 처리 건수, 신규 키 |
| `_DEL` | 삭제(기본 논리삭제) | 처리 건수 |
| `_EXEC` | 업무 처리(확정·통과·취소 등) | 처리 결과 |

- `SET NOCOUNT ON; SET XACT_ABORT ON;` + `TRY/CATCH` + SP 안의 트랜잭션.
- 공통 파라미터 `@P_USER_ID`, `@P_LANG_CD`, `@P_CLIENT_IP`는 항상 받습니다. `SpExecutor`가 넣으므로 DTO·화면에서 보내지 않습니다.
- 업무 오류는 `THROW 5xxxx, 'MSG.키', 1`. 앱이 사용자 언어로 번역합니다.
- 페이징 `OFFSET … FETCH`, 선택 조건 `(@P_X IS NULL OR COL = @P_X)`에는 `OPTION (RECOMPILE)`.
- 동적 SQL 금지(정렬 컬럼만 화이트리스트 + `QUOTENAME`). `SELECT *`, `WITH (NOLOCK)` 금지(DB가 READ_COMMITTED_SNAPSHOT).
- 저장 SP는 `@P_ROW_VER`로 동시성 검사(`MSG.CONCURRENCY`), 감사 대상이면 `LOG.USP_AUDIT_WRITE`.
- 회사 소유 데이터의 목록·단건 SP는 반드시 `FW.UFN_DATA_SCOPE(@P_USER_ID, '프로그램 ID')`를 거칩니다(협력사 격리).
- 코드·메뉴·권한·다국어·설정을 바꾸는 SP는 마지막에 `FW.TB_CACHE_VERSION`을 올립니다.
- 메일·포털 알림은 업무 SP 안에서 `FW.USP_MSG_QUEUE_SAVE … @P_RESULT_YN = 'N'`(같은 트랜잭션, 발송은 Worker).
- 첨부는 업무 행에 `FILE_GROUP_ID BIGINT NULL` 하나만 두고, 저장 SP에서 `FW.USP_FILE_GROUP_CONFIRM_EXEC` 호출.
- 저장 SP는 화면이 넘긴 `FILE_GROUP_ID`가 그 행이 이미 가진 묶음이거나 본인이 올린 확정 전 묶음인지 확인한 뒤 확정합니다. 자식 기록(상세 행·댓글)을 다루는 SP는 부모가 권한을 확인한 대상인지 SP 안에서 다시 확인합니다(2026-10 보안 점검, [../security/apply-to-business-repos.md](../security/apply-to-business-repos.md) 3장).
- 테이블 변경은 호환성 유지(컬럼은 NULL 또는 기본값으로 추가, 삭제·이름 변경은 다음 릴리스).
- 1초 넘는 SP는 느린 SP 로그에 남습니다.

## 5. 새 화면 만드는 순서 (개발 표준 가이드 3장 요약)

1. 프로그램 ID를 정합니다.
2. DB 객체: `db/Portal.Database/<스키마>/Tables|StoredProcedures|Types`에 파일 하나당 객체 하나.
3. 등록 데이터: `db/seed/<스키마>/…sql`에 `TB_PROGRAM`, `TB_PROGRAM_FUNC`, `TB_PROGRAM_API`, `TB_MENU(+_LANG)`, 역할 기본 권한 `TB_ROLE_PROGRAM_FUNC`, 다국어 `TB_LANG_RESOURCE`를 `MERGE`로(여러 번 실행해도 같은 결과). 새 파일은 `db/Portal.Database/Scripts/PostDeployment.sql`에 `:r` 한 줄로 등록.
4. Service(`I…Service` + 구현)에 업무 규칙. SP 호출은 `ISpExecutor`로만.
5. Controller는 `PortalController` 상속, 클래스에 `[Program("…")]`, 변경 Action에 `[Func(FuncCodes.…)]`. 업무 로직은 두지 않습니다.
6. View(`@L["키"]`, `<fw-button func="…" text-key="…">`)와 화면당 JS 하나(ES 모듈, `fw.ajax`만, `innerHTML`·인라인 스크립트 금지).
7. 테스트: Service 단위 테스트, SP당 통합 테스트 1개 이상, 회사 격리 테스트.
8. **Action을 추가하면 `TB_PROGRAM_API`에 `(메서드, /{Area}/{Controller}/{Action}, 프로그램, 기능)` 한 줄을 꼭 함께 넣습니다.** 다르면 권한 매트릭스 테스트가 실패합니다.

주의: 프레임워크 시드(`db/seed/FW/*.sql`)는 저장소 밖의 생성기 스크립트로 만듭니다. 업무 저장소는 이 파일들을 고치지 말고 업무 스키마 폴더(`db/seed/GAT/`)에 손으로 쓴 `MERGE` 파일을 둡니다. 프레임워크 시드의 `MERGE`는 없는 행만 넣거나 같은 키만 고치고, 다른 행을 지우지 않으므로 업무 시드와 함께 써도 됩니다.

## 6. 권한·메뉴

- **권한 = 역할 × 프로그램 × 기능(`FUNC_CD`)**. 기능: `READ CREATE UPDATE DELETE EXPORT PRINT UPLOAD APPROVE PII EXEC`(`FuncCodes`). 프로그램이 쓰는 기능은 `TB_PROGRAM_FUNC`, 역할별 허용은 `TB_ROLE_PROGRAM_FUNC`, 개인 예외(GRANT/DENY, 종료일)는 `TB_USER_PROGRAM_FUNC`. 부서 역할(`TB_DEPT_ROLE`, 하위 부서 상속)도 있습니다.
- 서버: Controller `[Program]` + Action `[Func]`를 권한 필터가 검사(없으면 403). 그리드 일괄 저장은 `[Func("UPDATE")]` + `await DemandRowFuncsAsync(rows)`(추가 행 CREATE, 삭제 행 DELETE 추가 확인). 데이터에서 프로그램을 정하는 공통 화면(첨부·레포트)은 `[ProgramFromData]` + `DemandAnyFuncAsync`.
- 화면: `<fw-button func="UPDATE" …>`는 권한이 있을 때만 그려집니다. JS는 버튼이 없으면 그 기능을 쓰지 않으면 됩니다.
- 메뉴: `TB_MENU`가 프로그램을 가리키고, 프로그램 READ 권한 + 메뉴 공개 범위(`OPEN_SCOPE_CD` = `ALL`/`EMP`/`PARTNER`)로 표시됩니다. **공개 범위는 메뉴 표시만 정하고, 보안은 세션 + 권한으로만 판단합니다**(사용자 결정 2026-10-22).
- 사용자 결정(2026-10-22): 업무 화면도 "협력사인지"로 막지 말고 **기능 권한(버튼 권한)으로 제어**합니다. 협력사 역할에는 본사 전용 프로그램 권한을 주지 않습니다(시드 테스트가 검사).
- 기본 역할 7개: `SYS_ADMIN`, `PORTAL_ADMIN`, `SECURITY_AUDITOR`, `EMP_USER`, `PARTNER_ADMIN`, `PARTNER_USER`, `API_CLIENT`. 업무 역할(예: 경비대, 반출 사용자, 반입 사용자, SAP 전송 담당)은 업무 시드로 추가합니다. 역할마다 줄 수 있는 회사 유형(`COMPANY_TYPE_CD`)과 데이터 범위가 있습니다.
- 관리 화면(코드 수정 없이 운영): 역할 관리, 역할별 권한(역할 × 화면 × 기능 체크표), 사용자별 예외 권한, 프로그램 관리, 사용자·부서·회사 관리. 사용법은 개발 표준 가이드 11장.

## 7. 공통 기능 사용법

| 기능 | 업무 화면에서 쓰는 방법 |
| --- | --- |
| 공통코드 | `FW.TB_CODE_GROUP` / `TB_CODE`(+`_LANG`), 그룹-코드 2단, 추가 속성 5개, 유효기간. 업무 코드(반출 상태, 반입 시스템, 바코드 접두어 규칙 등)는 업무 시드로 그룹을 추가하고 관리 화면 `SYS_CODE_MGT`에서 운영. 화면용 `<fw-select code-group="…">`는 아직 예정(02 문서) |
| 설정 | `FW.TB_CONFIG` + `ConfigKeys` 상수 + `IPortalConfig`. 새 키는 상수 + 시드 기본값 + 초기 데이터 정의서 갱신. 비밀값은 설정 화면에서 입력하면 암호화 저장 |
| 다국어 | `TB_LANG_RESOURCE`, Razor `@L["키"]`, JS `await fw.ready` 후 `fw.t('키')`. 3개 언어 모두 입력. 기존 문구는 고치지 말고 새 키 추가(시드 MERGE는 없는 행만 넣음) |
| 그리드 | `fw-grid.js`의 `Grid`: 서버 페이징, 인라인 편집, I/U/D 추적 `getChanges()` → TVP 일괄 저장, `validate()` |
| 트리 | `fw-tree.js` (부서 관리 화면 참고) |
| 대화상자 | `fw.dialog.confirm`, `fw.dialog.prompt`(사유 입력), `fw.notify` |
| 첨부 | `fw-file.js`의 `FileBox` + 공통 API `/File/*`. 확장자는 공통코드 `FILE_EXT`, 크기 `FILE.MAX_SIZE_MB` |
| 엑셀 | 내보내기 `ExcelWriter`(`[Func(Export)]`), 업로드 `ExcelReader` → 미리보기 → `grid.applyChanges` → 저장. 개인정보 목록은 사유 + `IPiiAccessLog` |
| 게시판 | 게시판 관리(`BRD_BOARD_MGT`)에서 만들면 게시판 하나 = 프로그램 하나, 주소 `/Board/{게시판 코드}`. 권한은 그 프로그램에. 기존 시스템의 공지사항은 게시판 `BRD_NOTICE`로 대체(별도 개발 없음) |
| 대시보드 | 위젯 = 데이터 SP(`스키마.USP_WIDGET_…`, 파라미터는 공통 3개, 결과 열 `LABEL, VALUE, TEXT, LINK, DT`) + 표시 유형(`CARD` 숫자 카드 / `LIST` 목록) + 캐시 초 + 권한 프로그램. `TB_WIDGET(+_LANG)` 시드로 등록하고 위젯 관리(`DSH_WIDGET_MGT`)에서 제목·칸 수·캐시·정렬·사용 여부 조정. 예: 오늘 반출 대기 건수 카드 |
| 레포트·출력 | 출력물은 레포트로 등록: 데이터 SP `스키마.USP_RPT_…_GET_LIST`(결과셋 하나, `@P_MAX_ROWS`) + `TB_REPORT` 행(조건 JSON) + 선택 Razor 템플릿. 공통 화면 `/Rpt/Viewer/Index/{ID}`가 미리보기·PDF·엑셀 제공. 업무 화면 [발행] 버튼(`data-report`)은 팝업으로 열림(사용자 결정). 화면 [인쇄]는 `<fw-button func="PRINT" data-print-area="#…">`. 큰 결과는 백그라운드 생성 |
| PDF | `IPdfConverter`(서버 Edge 헤드리스) |
| 메일·알림 | 템플릿 `TB_MSG_TEMPLATE(+_LANG)` 시드 + 업무 SP에서 `FW.USP_MSG_QUEUE_SAVE`. 포털 알림은 템플릿 `PORTAL_YN = 'Y'`, 받는 사람을 권한으로 고를 때 `FW.UFN_FUNC_USERS('프로그램', '기능')`. 기존 SP의 `pSendMail` 직접 발송·하드코딩 주소는 이것으로 바꿉니다 |
| 배치 | DB만 다루면 `스키마.USP_JOB_<대상>_EXEC` SP(결과 `PROCESSED_CNT`), 파일·외부 연동이면 `IScheduledJob` 클래스. `TB_JOB` 시드(Cron 5필드, 한국 시간). 운영 > 스케줄 관리에서 주기 변경·즉시 실행. 예: 기존 08:00 반출 기한 초과 반려 배치 → `GAT.USP_JOB_CARRY_OUT_EXPIRE_EXEC`(제안) |
| 휴일·영업일 | `FW.TB_HOLIDAY`, `FW.UFN_IS_BUSINESS_DAY` (D+3 같은 기한 계산에 사용 가능) |
| 로그 | 로그인·화면 접근·데이터 변경(감사)·개인정보 접속기록은 프레임워크가 자동으로 남김(10년, 해시 체인). 업무 화면은 개인정보 다운로드·마스킹 해제 때만 `IPiiAccessLog` |

## 8. 외부 API·EAI(SAP)·LDAP·전자결재·AD master 연동

외부 API 수신·송신(관리 화면 포함)과 LDAP 조회 모듈은 **프레임워크가 제공**하며 구현이 끝났습니다(02 문서). 업무 저장소에서 따로 만들지 말고 가져다 씁니다. 자세한 작성법은 개발 표준 가이드 8장과 3장 "LDAP 조회 쓰는 법".

**전자결재 연동, AD master 연동(조직 동기화), AD SSO(도메인 PC 자동 로그인)는 프레임워크 제공 범위 밖입니다**(사용자 결정 2026-10-23). 사이트마다 결재 시스템과 AD 원천이 다르므로 **각 업무 저장소가 업무 개발 때** 만듭니다. 프레임워크에는 전자결재 골격(`IApprovalGateway`, `TB_APPROVAL_LINK`)과 AD 동기화 작업(`JOB_AD_SYNC`)을 넣지 않습니다. 전자결재 구성안(`IApprovalGateway`, `APPROVAL.VIEW_URL`)과 이전 AD 뷰 동기화 설계안은 설계서에 참고용으로 남아 있고, 아래 표의 해당 행은 그 요약입니다.

| 연동 | 설계(현재 기준) | 업무에서 쓰는 방법 |
| --- | --- | --- |
| 외부 API 수신 | `/api/v1/<리소스>` REST. `app.MapApi("GET", "/vendors/{vendorCd}", "VENDOR_GET", "설명", handler)`로 등록(세 번째 값이 API 코드). 관문(`ApiGatewayMiddleware`)이 토큰·사용 여부·허용 IP·허용 코드·분당 한도를 확인하고 모든 호출을 `LOG.TB_API_LOG`에 남김(비밀번호·토큰 등은 `***`). 토큰 `POST /api/v1/token`(OAuth2 Client Credentials, 유효 시간 `API.TOKEN_MINUTES`). 서명 키 `Api:SigningKey`(Base64, 32바이트 이상)는 **운영 환경 변수 `Api__SigningKey` 또는 비밀 저장소로만 주입**. 문서 `/api/v1/openapi.json` 자동. CSRF·화면 권한 필터는 적용되지 않으므로 Handler에서 입력을 직접 검증하고 DB는 SP로만. 호출자는 `http.GetApiCaller()` | 외부 시스템이 포털 데이터를 가져가야 할 때 `MapApi` 등록. 클라이언트 등록·허용 코드·허용 IP는 **외부 연계 > API 클라이언트**(`SYS_API_CLIENT_MGT`, 비밀값은 발급 때 1회만 표시, 변경은 최대 30초 뒤 반영). 주의: `MapApi`는 `Program.cs`(프레임워크 파일)에 쓰므로 04 문서 5장 공유 접점 |
| 외부 API 송신 | **외부 연계 > 송신 대상**(`SYS_API_OUTBOUND_MGT`, `TB_API_OUTBOUND`)에 대상 키별 기본 주소·인증(BASIC/BEARER/API_KEY 헤더, 비밀값 암호화)·제한 시간·재시도 등록. 코드는 `IOutboundApiClient.SendAsync(new OutboundRequest("대상키", HttpMethod.Get, "/경로"))`. 재시도는 다시 보내도 되는 메서드만(POST는 상대가 중복을 걸러 줄 때 `Idempotent: true`), 연속 5번 실패하면 30초 차단(`CircuitOpen`). 4xx·5xx는 예외가 아니라 `StatusCode`/`IsSuccess`로 확인, 연결 실패 등만 `OutboundApiException`. 사내망(사설 IP)은 대상에 `사내망 허용`을 켜야 하고 루프백·링크로컬은 항상 차단, 리디렉트 안 따라감, 응답 1MB까지. 모든 호출은 방향 OUT으로 `LOG.TB_API_LOG` | 반입 바코드 SAP 실시간 조회처럼 포털이 EAI를 직접 불러야 하는 경우. 개발/운영 주소는 각 환경의 등록값으로 분리 |
| EAI(SAP) 전송 | 사용자 결정(2026-10-23): **반출 MM I/F·EL I/F 버튼은 포털이 SAP를 직접 부르지 않고 레코드에 "전송 대기"를 표시하면 EAI가 가져갑니다.** SAP 전송 버튼은 별도 기능 권한(역할별) | 전송 대기 표시 컬럼·값과 EAI의 수집 방식은 기존 SQL 매퍼와 EAI 담당과 확인 후 확정(03 문서 미확인 항목) |
| 전자결재 (**업무 저장소에서 구현**) | 참고 설계: 결재 과정은 사내 전자결재에 위임, 포털은 결재 화면 URI를 띄우고 결과(진행·승인·반려)만 확인. 업무 키 ↔ 결재 문서 매핑 테이블, 결과 확인 방식(API / DB 뷰 / 콜백)은 사이트별로 결정. 업무 스키마(예: `GAT`)에 테이블·SP를 두고, 결재 시스템 주소는 설정(`TB_CONFIG`), 외부 호출은 프레임워크 외부 API 송신 사용 | 반출입의 승인·불가 "사유 입력"은 전자결재가 아니라 단순 텍스트입니다. 결재가 필요한 업무가 생길 때 구현 |
| LDAP 조회 모듈 (**프레임워크 제공**, 선택) | **읽기 전용 조회**만: `ILdapDirectory.FindUserAsync("계정")`, `SearchUsersAsync("검색어", 최대건수)`. 아무것도 저장하지 않고, 비밀번호를 받아 AD 인증을 하지 않으며, 조회 값은 필터 인젝션 방지 이스케이프. **기본 꺼짐 `LDAP.ENABLED = N`**. 사이트에서 설정 관리로 `LDAP.HOST`, `LDAP.PORT`(636), `LDAP.USE_SSL`(Y), `LDAP.BASE_DN`, `LDAP.BIND_DN`, `LDAP.BIND_PASSWORD`(암호화), `LDAP.USER_FILTER`, `LDAP.USER_SEARCH_FILTER`, `LDAP.TIMEOUT_SEC`를 채우고 켬. 꺼져 있거나 설정이 비면 `LdapNotConfiguredException`, 연결·조회 실패는 `LdapQueryException`(메시지에 서버 주소·계정 없음) | 업무 저장소의 AD master 연동(사용자·조직 동기화, 퇴사자 잠금)이 원천 조회에 쓸 수 있음. 동기화 자체는 업무 저장소가 개발 |
| AD master 연동·조직 동기화 (**업무 저장소에서 구현**) | 참고 설계: AD master 테이블을 링크드 서버 뷰(또는 LDAP 모듈)로 읽어 배치 작업(`스키마.USP_JOB_…_EXEC` 또는 `IScheduledJob`)으로 `FW.TB_DEPT`/`TB_USER`/`TB_USER_LOGIN_MAP`/`TB_USER_AD_GROUP`에 반영, 원천에 없거나 비활성이면 잠금. FW 테이블은 프레임워크 SP(`USP_USER_SAVE`, `USP_DEPT_SAVE` 등)나 업무 SP로 갱신하고 감사 로그를 남김 | 기존 시스템의 사용자 동기화 배치(`ADM_DOODREAM_INTERFACE_BATCH_STR`)를 참고하고 이관 후 중지. AD 그룹 → 역할 매핑(`TB_AD_GROUP_ROLE`, 프레임워크 테이블)으로 업무 역할 자동 부여 가능. AD 뷰 컬럼·위치는 사이트마다 확인 |
| AD SSO (**업무 저장소에서 구현**) | 사용자 결정 2026-10-23: 도메인 PC 자동 로그인(Negotiate/Windows 인증)은 프레임워크에서 빼고 업무 저장소에서 개발. 프레임워크는 ID/PW + 2단계 인증만 | 아래 "이미 있는 AD 관련 부분의 취급"과 SSO 구현 방법 참고. 협력사는 협력사 전용 로그인 |

**프레임워크에 이미 있는 AD 관련 부분의 취급**(사용자 결정 2026-10-23, AD SSO도 업무 저장소로): 아래는 지우지 않고 **업무 저장소가 채워 쓰는 확장 지점**으로 둡니다. 프레임워크는 이 값을 채우는 로직(SSO, 동기화)을 갖지 않습니다.

| 이미 있는 것 | 프레임워크에서의 상태 | 업무 저장소가 할 일 |
| --- | --- | --- |
| `FW.TB_USER_LOGIN_MAP`(외부 계정 ↔ 포털 사용자, `PROVIDER_CD`=AD, `EXTERNAL_ID`=objectSid, `EXTERNAL_LOGIN`=sAMAccountName) | 테이블만 있음, 쓰는 코드 없음 | AD master 연동이 채우고, SSO가 Windows 계정으로 포털 사용자를 찾을 때 씀 |
| `FW.TB_USER_AD_GROUP` + `FW.TB_AD_GROUP_ROLE`, `FW.UFN_USER_ROLES` | 유효 역할 계산에 이미 포함(AD 그룹 → 역할) | 동기화가 `TB_USER_AD_GROUP`을 채우고, 그룹-역할 매핑은 업무 시드로 |
| `FW.TB_DEPT.AD_OU_PATH`, 부서 관리 화면의 "AD 동기화 부서는 역할·정렬만 수정" 규칙 | 동작함(값이 있으면 AD 부서로 봄) | 동기화가 채움 |
| `FW.TB_USER.PWD_HASH` NULL 허용(AD 전용 계정) | ID/PW 로그인은 비밀번호 없는 계정을 거부 | SSO 전용 계정 생성 시 사용 |
| 설정 `AUTH.MODE`(시드 `BOTH`), `AUTH.AD_AUTO_PROVISION`, `AD.SYNC_VIEW` | 키만 있고 프레임워크 코드는 읽지 않음(프레임워크는 항상 ID/PW) | 업무 저장소의 SSO·동기화가 이 키를 읽어 씀. 의미를 바꾸면 업무 문서에 적음 |
| 협력사 전용 로그인 `/Account/PartnerLogin` | 동작함 | SSO 적용 후 본사 로그인 화면을 SSO 진입으로 바꿔도 협력사는 이 주소 유지 |

SSO 구현 방법(제안): 업무 저장소 영역에 SSO 진입 Action을 두고 Windows 인증(Negotiate)으로 받은 계정을 `TB_USER_LOGIN_MAP`으로 찾아 포털 세션을 발급합니다. 지금 프레임워크에는 "외부에서 인증된 사용자로 세션을 발급하는" 공개 API가 없으므로(`AuthService`는 ID/PW·MFA만), 이 진입점 하나는 프레임워크에 확장 지점으로 추가하는 것을 권합니다(04 문서 5장). 로그인 기록·잠금·세션 규칙은 프레임워크 것을 그대로 따릅니다.

## 9. 테스트 규칙 (사용자 규칙 2026-10-23)

- **기능 PR마다: 단위 테스트만** 돌립니다(DB 불필요).
  `dotnet test Portal.sln -c Release --filter "Category!=Integration"` (단위 + 아키텍처 + DB 없는 웹 테스트)
- **단계가 끝날 때: 통합 테스트**를 돌립니다(SP 통합 + 권한 매트릭스). CI `db-integration` 작업은 수동 실행(GitHub Actions "Run workflow", GitLab 수동 작업).
- 단위 테스트에서 문제가 드러나면 그때 통합 테스트도 돌립니다.
- 통합 테스트로만 확인할 수 있는 점·예상 문제는 확인 목록에 적어 두고 단계 종료 때 확인합니다. 새 SP의 통합 테스트 코드는 기능 PR에 함께 넣습니다.
- 테스트 이름 `대상_상황_기대결과`, Arrange → Act → Assert, Mock 라이브러리 없이 `Portal.Testing`의 수작업 Fake 사용. 테스트를 Skip해서 통과시키지 않습니다.
- 통합 테스트 클래스: `[Collection(DbTests.Name)]`, `[Trait("Category", "Integration")]`, `DbFixture.BeginAsync()`(롤백). 회사 소유 데이터 SP는 "협력사 A로 협력사 B 데이터 0건" 테스트.
- 로컬 DB: `./db/ops/local-setup.ps1 -Server localhost -Database PORTAL_LOCAL`, 다른 DB는 환경 변수 `PORTAL_TEST_DB`.

## 10. 개발 표준 가이드 요약 (반드시 지킬 것)

- 빌드 경고 = 오류, Nullable 켬(`!`로 끄지 않음), 생성자 주입만, `TimeProvider`, 예외를 삼키지 않음(업무 오류는 `BizException(MessageKeys.X)`), 모든 DB·외부 호출은 비동기 + `CancellationToken`.
- 새 NuGet 패키지는 허용 목록에 없으면 추가하지 않습니다. 빌드 전용 Microsoft 패키지도 추가할 때마다 사용자 승인을 받습니다.
- 화면: 레이아웃은 `_ViewStart`가 고름(화면에서 지정하지 않음), 제목은 메뉴명, `<h1>` 하나, 모든 입력에 `<label>`, 색만으로 상태 표시 금지(KWCAG 2.2).
- JS: 화면당 파일 하나, `fw.ajax`만(직접 `fetch` 금지), 브라우저 `confirm()`·`prompt()` 금지, 사용자 데이터는 `textContent`로.
- 개인정보: 새로 저장하지 않음. 불가피하면 `…_ENC VARBINARY` + `IPersonalDataProtector`, 화면은 마스킹, `TB_PROGRAM.PII_YN = 'Y'`.
- 로그: `ILogger<T>` 메시지 템플릿, 비밀번호·토큰·연결 문자열·개인정보 원문 금지.
- MR 체크리스트(개발 표준 가이드 10장)를 PR 본문에 붙입니다. PR 하나에 변경 하나. 프레임워크 프로젝트를 바꾸는 변경은 프레임워크 저장소에서.
- 브랜치·배포: `main`(운영, 태그 `vX.Y.Z`) / `develop`(개발계 자동) / `feature/*` / `release/*` / `hotfix/*`(설계서 17장). 운영 DB 직접 수정 금지, DB 변경은 DACPAC 비교 스크립트로.
