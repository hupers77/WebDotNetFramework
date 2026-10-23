# CLAUDE.md 초안 (새 업무 저장소용)

> 이 파일은 프레임워크 저장소의 `docs/handover/new-repo-CLAUDE.md`입니다. 새 업무 저장소 루트에 `CLAUDE.md`로 복사하고, `<…>` 칸을 채운 뒤 이 인용 블록은 지웁니다. 비밀번호·서버 주소·개인 이름은 넣지 않습니다.

---

# <업무 시스템 이름> (예: 경비대 반출입 시스템)

이 저장소는 사내 웹 포털 프레임워크(`WebDotNetFramework`, 시작 버전 `<fw-v1.0.0>`) 위에 **<업무 시스템 이름>** 업무 화면을 개발하는 저장소입니다. 프레임워크는 표준 모델이며, 이 저장소는 프레임워크를 고치지 않고 업무만 더합니다.

## 먼저 읽을 것

1. `docs/handover/README.md` → 01~04 (프레임워크 사용법, 구현 현황, 업무 이관 내용, 저장소 운영 방식)
2. `docs/dev-guide.md` 3장(새 화면 추가 순서), 6장(SP 규칙), 9장(테스트), 10장(MR 체크리스트), 11장(자주 하는 작업)
3. `docs/framework-design.md` 7장(DB, 기존 업무 테이블 예외), 8장(SP 표준)
4. `docs/business/` (이 업무의 화면·데이터 흐름, 정리표, 마이그레이션 계획서)

## 업무 기본값

| 항목 | 값 |
| --- | --- |
| 업무 스키마 | `<GAT>` |
| MVC 영역 | `<Gat>` (`src/Portal.Web/Areas/<Gat>` 또는 `src/Portal.Modules.<Gate>`) |
| 프로그램 ID 접두어 | `<GAT>_` (형식 `<모듈>_<대상>_<유형>`, 유형 MGT/INQ/RPT/POP) |
| 다국어 키 접두어 | `<GAT>.` |
| SP 오류 번호 대역 | `<53000~53999>` |
| 메뉴 위치 | `<홈 > 경비대>` |
| 기존 업무 테이블 | `<dooinfoiv.CARRY_IN*, dooinfoiv.CARRY_OUT*>`: 이름·컬럼 유지, 신규 SP로만 접근 |
| 프레임워크 원격 | `framework` (업데이트는 태그 병합) |

## 꼭 지킬 규칙

- **프레임워크 파일은 고치지 않습니다.** 소유 경계는 `docs/handover/04-new-business-repo.md` 4장. 프레임워크 버그·부족한 기능은 사용자에게 알리고 프레임워크 저장소에서 고친 뒤 업데이트로 받습니다. 공유 접점 파일(`PostDeployment` 시드 등록, 서비스 등록 등)은 정해진 확장 파일이나 맨 끝 줄 추가만 합니다.
- DB 접근은 SP만(`ISpExecutor`). 인라인 SQL·EF Core·동적 SQL 금지. 신규 SP는 `<GAT>.USP_대상_동작`(`_GET_LIST _GET _GET_TREE _SAVE _DEL _EXEC`), 공통 파라미터 `@P_USER_ID @P_LANG_CD @P_CLIENT_IP`, `SET NOCOUNT ON; SET XACT_ABORT ON;`, 오류는 `THROW 5xxxx, 'MSG.키', 1`.
- 기존 업무 테이블은 이름·컬럼을 바꾸지 않습니다. 기존 SQL은 그대로 옮기지 않고 신규 SP 표준으로 다시 씁니다. 관리·공통 데이터(`ADM_*`)는 FW 테이블로 변환합니다.
- 회사 소유 데이터 SP는 `FW.UFN_DATA_SCOPE`를 거칩니다. 화면·버튼은 협력사 여부가 아니라 기능 권한으로 제어합니다.
- 하드코딩 금지: 문구는 다국어 키(ko/en/zh-CN 모두), 정책 숫자·주소는 설정(`TB_CONFIG`), 코드값·바코드 규칙·원천 시스템 구분은 공통코드.
- 새 Action은 `[Program]`/`[Func]` + `TB_PROGRAM_API` 시드 한 줄(경로 `/{Area}/{Controller}/{Action}`).
- 화면은 프레임워크 표준 틀(헤더·메뉴·제목·조회 조건·버튼·그리드)을 바꾸지 않습니다. 내용은 기존 화면과 비슷하게, 꾸밈은 따라 하지 않습니다.
- 개인정보는 새로 저장하지 않습니다. 불가피하면 암호화·마스킹·`PII_YN`. 로그·커밋·문서에 비밀번호, 연결 문자열, 서버 주소, 개인 이름을 넣지 않습니다.
- **전자결재 연동, AD master 연동(조직 동기화), AD SSO(도메인 PC 자동 로그인)는 이 저장소가 만듭니다**(프레임워크 제공 범위 밖). 프레임워크의 AD 관련 테이블·설정 키(`TB_USER_LOGIN_MAP`, `TB_USER_AD_GROUP`, `TB_AD_GROUP_ROLE`, `AUTH.MODE`, `AD.SYNC_VIEW` 등)는 채워 쓰는 확장 지점입니다(`docs/handover/01-framework-guide.md` 8장). 업무 스키마·설정·스케줄러 작업으로 구현하고, AD 연결이 필요하면 프레임워크 LDAP 모듈을, 외부 호출은 프레임워크 외부 API 송신(`IOutboundApiClient`, 대상은 송신 대상 화면에 등록)을 씁니다. 외부에 데이터를 줄 때는 수신 API(`MapApi`, API 클라이언트 화면에서 허용)를 씁니다. FW 테이블은 프레임워크 SP로 갱신합니다.
- 새 NuGet 패키지는 프레임워크 허용 목록에 있는 것만. 필요하면 사용자 승인 후 프레임워크 저장소에 먼저 추가합니다.

## 빌드와 테스트

```powershell
dotnet build Portal.sln                                              # 경고 = 오류
dotnet test Portal.sln -c Release --filter "Category!=Integration"   # 기능 PR마다: 단위 테스트만
./db/ops/local-setup.ps1 -Server localhost -Database PORTAL_LOCAL    # 로컬 DB
dotnet test tests/Portal.IntegrationTests                            # 단계 종료 때, 프레임워크 업데이트 때
```

- 기능 PR마다 단위 테스트만 돌립니다. DB 통합 테스트는 단계가 끝날 때(CI `db-integration` 수동 실행), 그리고 단위 테스트가 문제를 드러냈을 때 돌립니다.
- 새 SP의 통합 테스트 코드는 기능 PR에 함께 넣고, 통합 테스트로만 확인할 수 있는 점은 `docs/business/integration-check-list.md`에 적어 단계 종료 때 확인합니다.
- 테스트를 Skip해서 통과시키지 않습니다. 버그는 실패 테스트부터 만듭니다.

## 작업 방식

- 작은 PR을 순서대로(화면 하나 또는 기능 하나 = PR 하나). PR 본문에 개발 표준 가이드 10장 체크리스트를 붙입니다.
- 브랜치: `feature/<이슈>-<요약>` → `develop`(개발계 자동 배포) → `release/*` → `main`(태그, 운영 수동 승인).
- 사용자가 정한 결정은 `docs/business/`의 결정 표에 날짜와 함께 적습니다(이름 대신 역할). 결정이 필요한 선택지는 추천안과 함께 사용자에게 묻습니다.
- 프레임워크 업데이트: `git fetch framework --tags` → 태그 병합 → 빌드·단위·통합 테스트 → 변경 스크립트 검토 → MR (`docs/handover/04-new-business-repo.md` 7장).

## 지금 상태 (<날짜> 기준, 계속 고침)

- 다음 할 일: <예: 기존 앱 소스(SQL 매퍼) 받기 → 사용/미사용 정리표 → 마이그레이션 계획서 → 화면별 데이터 흐름 → 반출 화면 3개 → 반입 화면 4개 → AD master 연동 → AD SSO → (필요 시) 전자결재 연동>
- 미확인 항목: `docs/business/`의 미확인 표 (출발점: `docs/handover/03-business-gate.md` 7장)
- 프레임워크에 요청한 것: <예: 업무 모듈 확장 지점, 외부 인증 세션 발급(AD SSO용)>
