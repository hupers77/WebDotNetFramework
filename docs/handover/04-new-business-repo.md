# 04. 새 업무 저장소 시작과 프레임워크 업데이트 반영

기준 시점: 2026-10-23. 이 문서의 가져오기·업데이트 방식은 **제안**이며, 첫 업무 저장소를 만들 때 사용자 확인을 받아 확정합니다.

## 1. 원칙

1. 업무 시스템 하나 = 저장소 하나. 저장소는 사용자가 GitLab에 만듭니다(지금 프레임워크는 GitHub `WebDotNetFramework`에서 개발 중이며, 운영 형상관리는 GitLab).
2. 모든 업무 저장소는 이 프레임워크의 **같은 구조·규칙·공통 기능**을 씁니다. 프레임워크를 업무 저장소에서 따로 고쳐 갈라지게 하지 않습니다.
3. **프레임워크 코드는 프레임워크 저장소에서만 고칩니다.** 업무 개발 중 프레임워크 버그나 부족한 기능을 찾으면 프레임워크 저장소에 먼저 반영하고, 업무 저장소는 업데이트로 받습니다.
4. 업무 저장소는 **새 파일을 더하는 방식**으로만 업무를 넣습니다. 프레임워크 파일을 고쳐야 하는 곳은 5장의 "공유 접점"뿐이며, 접점도 줄이도록 프레임워크에 확장 지점을 둡니다.
5. 프레임워크 제공 범위 밖(사용자 결정 2026-10-23): **전자결재 연동, AD master 연동(조직 동기화)**. 사이트마다 다르므로 업무 저장소가 자기 업무 스키마·배치·설정으로 만듭니다. 프레임워크의 FW 테이블(사용자·부서·AD 그룹 매핑)과 LDAP 모듈, 외부 API 송신, 스케줄러를 써서 만들고, 프레임워크 파일은 고치지 않습니다. 여러 업무 저장소에서 같은 연동이 반복되면 그때 프레임워크로 올릴지 검토합니다.
6. 확인 필요: 업무 시스템마다 포털(로그인·메뉴·웹 앱·DB)을 따로 운영하는지, 하나의 포털에 여러 업무를 모으는지. 이 문서는 **업무 저장소마다 자기 배포 단위(웹 앱 + DB)를 가진다**고 가정합니다. 하나로 모으기로 하면 업무를 `Portal.Modules.<업무>` 패키지로 만들어 한 호스트에 올리는 방식(3장 방식 B)으로 바꿉니다.

## 2. 프레임워크 버전

- 프레임워크 저장소는 업무 저장소가 가져갈 때마다 **릴리스 태그**를 붙입니다. 형식 `fw-v주.부.수`(유의적 버전). 지금은 태그가 없습니다.
- **제안: 첫 태그 `fw-v1.0.0`** 은 LDAP 모듈(PR #61), 이관 문서 최종본, AD SSO 처리 결정, 5장 확장 지점이 `main`에 들어간 직후 그 커밋에 붙입니다(AD SSO를 나중에 넣기로 하면 `fw-v1.1.0`으로). 사용자 확인 후 프레임워크 저장소에서 `git tag -a fw-v1.0.0 -m "…" && git push origin fw-v1.0.0`, GitLab 이전 후에는 GitLab에도 같은 태그를 둡니다.
- 버전 올리는 기준: 버그 수정·문서는 `수`, 기능 추가(기존 업무 코드 수정 불필요)는 `부`, 깨지는 변경은 `주`.
- 태그마다 변경 내역(추가 기능, 깨지는 변경, DB 변경, 업무 저장소가 할 일)을 GitLab/GitHub 릴리스 노트와 이 폴더 02 문서에 남깁니다.
- 깨지는 변경(SP 파라미터·결과 변경, 공통 JS API 변경, 설정 키 이름 변경)은 주 버전을 올리고 옮기는 방법을 함께 씁니다.

## 3. 가져오기 방식

| 방식 | 내용 | 장점 | 단점 | 판단 |
| --- | --- | --- | --- | --- |
| **A. 업스트림 병합** | 업무 저장소를 프레임워크 태그에서 시작하고, 프레임워크 저장소를 원격 `framework`로 등록해 새 태그를 `git merge`로 받음 | 지금 바로 가능. 웹 호스트·관리 화면·wwwroot·FW/LOG DB·시드·테스트가 모두 그대로 따라옴. 업무 코드가 프레임워크 코드와 같은 빌드·테스트를 받음 | 업무 저장소가 프레임워크 파일을 고치면 병합 충돌. 규칙(1장 3·4)과 확장 지점으로 막아야 함 | **추천 (지금)** |
| B. 내부 NuGet 패키지 | 설계서 17장: Core/Data/Security/Infrastructure/Framework.Web을 GitLab Package Registry 패키지로 배포 | 버전 고정·교체가 깔끔. 업무 저장소가 작아짐 | 웹 호스트·관리 화면(Razor)·wwwroot·DB(FW 스키마 dacpac)·시드까지 패키지로 만들어야 함(Razor 클래스 라이브러리, dacpac 패키지). 지금 구조로는 작업이 큼 | 업무 저장소가 2개 이상이 되고 프레임워크가 안정된 뒤 검토 |
| C. git submodule | 프레임워크를 하위 폴더로 넣음 | 프레임워크 원본이 분리됨 | 솔루션·DB 프로젝트 경로가 복잡, 시드·PostDeployment 연결이 어려움 | 비추천 |

## 4. 파일 배치 (업무 저장소)

**프레임워크 소유(고치지 않음)**: `src/Portal.Core`, `Portal.Data`, `Portal.Security`, `Portal.Infrastructure`, `Portal.Framework.Web`, `Portal.Worker`, `src/Portal.Web`의 기존 파일(Areas Sys/Ptn/Log/Rpt/Board/Dash, Controllers, Views/Shared, wwwroot의 `fw*.js`·css), `db/Portal.Database/FW|LOG|Security|Storage`, `db/seed/FW`, `db/ops`, `tests/Portal.Testing`과 기존 테스트, `eng/`, `Directory.*.props`, `global.json`, CI 파일, `docs/framework-design.md`·`dev-guide.md`·`initial-data.md`·`docs/handover/`.

**업무 소유(새로 추가)**: 경비대 반출입 예(스키마·영역 이름 `GAT`/`Gat`는 제안)

```
src/Portal.Web/Areas/Gat/                  방식 1 (지금 바로 동작): Controllers, Services, Models, Views
src/Portal.Web/wwwroot/js/gat/             화면당 JS 하나 (+ 업무 공통 부품: 바코드 입력란 등)
  또는 src/Portal.Modules.Gate/            방식 2 (5장 확장 지점이 생긴 뒤): Razor 클래스 라이브러리
db/Portal.Database/GAT/                    Tables, StoredProcedures, Functions, Types (신규 객체)
db/Legacy.Database/ (제안)                 기존 업무 테이블 DDL(dooinfoiv.CARRY_*) - 빌드 참조 전용, 배포하지 않음
db/seed/GAT/                               프로그램·기능·API 경로·메뉴·역할·권한·다국어·코드·작업·위젯·레포트 MERGE
src/Portal.Web/Areas/Gat/Integration/ (제안)  전자결재·AD master 연동 코드(업무 저장소 구현 범위), 연동 배치는 db/Portal.Database/GAT/에
db/migration/ (제안)                       기존 ADM_* → FW 변환 스크립트, 검증 쿼리, 운영 이관 절차
tests/Portal.UnitTests/Gat/, tests/Portal.IntegrationTests/Gat/
docs/business/                             업무 문서: 화면·데이터 흐름, 정리표, 마이그레이션 계획서, 튜닝 가이드
CLAUDE.md                                  new-repo-CLAUDE.md를 복사해 채움
```

- 솔루션·프로젝트 이름(`Portal.sln`, `Portal.Web` 등)은 바꾸지 않습니다(이름을 바꾸면 매번 병합 충돌).
- 기존 업무 테이블을 `db/Portal.Database`에 그대로 넣으면 DACPAC 배포가 기존 테이블을 "고치려" 할 수 있습니다. 빌드 확인용 별도 프로젝트(같은 DB 참조)로 두고 배포 대상에서 뺍니다. 처음 배포 전에 생성 스크립트(`SqlPackage /Action:Script`)를 반드시 검토합니다.
- 기존 DB에 배포하면 DB 옵션도 맞춰집니다(배포 프로필 `ScriptDatabaseOptions=True`: READ_COMMITTED_SNAPSHOT, 정렬 등). 기존 앱이 같은 DB를 쓰는 기간이 있으면 영향을 먼저 확인합니다.

## 5. 공유 접점과 프레임워크 확장 지점 (제안, 프레임워크 저장소에서 구현)

방식 A에서 업무 저장소가 프레임워크 파일을 건드려야 하는 곳입니다. 업무 저장소 시작 전에 프레임워크에 확장 지점을 넣으면 충돌이 거의 없어집니다.

| 접점 | 지금 | 제안 확장 지점 |
| --- | --- | --- |
| 배포 후 시드 등록 `db/Portal.Database/Scripts/PostDeployment.sql` | 업무 시드마다 `:r` 줄 추가 필요 | 맨 끝에 `:r ./PostDeployment.Business.sql` 한 줄을 프레임워크가 두고, 업무 저장소는 그 파일만 고침(프레임워크에는 빈 파일) |
| 서비스·수신 API 등록 `src/Portal.Web/Program.cs` | 업무 Service 등록 줄, 업무 수신 API의 `app.MapApi(...)` 줄 추가 필요 | 프레임워크가 `BusinessModules.cs`(partial 또는 확장 메서드, 빈 구현)를 두고 업무 저장소는 그 파일만 고침. 설계서 4장의 `IPortalModule` 방식으로 바꿔도 됨 |
| 권한 매트릭스 테스트 `PermissionMatrixTests` | `Portal.Web` 어셈블리 컨트롤러만 검사 | `Portal.Modules.*` 어셈블리도 검사하도록 확장 |
| 업무 모듈 프로젝트 | Razor 클래스 라이브러리 설정·ApplicationPart 등록 없음 | `Portal.Modules.*` 템플릿(RCL, 정적 파일 경로 `_content/…`) 추가 |
| Worker 작업 등록 `src/Portal.Worker/WorkerServices.cs` | `IScheduledJob` 클래스 작업이면 등록 줄 추가 | 위 `BusinessModules`와 같은 방식 |
| 패키지 `Directory.Packages.props`, `eng/allowed-packages.txt` | 업무에 새 패키지가 필요하면 고침 | 허용 목록은 프레임워크가 관리. 업무 전용 패키지는 사용자 승인 후 프레임워크 허용 목록에 먼저 추가 |
| 설정 `appsettings.json` | 업무 옵션 추가 시 고침 | 업무 설정은 가능한 한 `TB_CONFIG`(업무 시드)로. 파일 설정이 필요하면 `appsettings.Business.json`을 추가로 읽게 함 |

이 확장 지점이 생기기 전에는 방식 1(`Portal.Web/Areas/Gat`)로 시작하고, 위 접점 파일은 **맨 끝에 줄을 더하는 것만** 허용합니다(충돌이 나도 풀기 쉬움).

## 6. 새 업무 저장소 시작 절차

```bash
# 0) 사용자가 GitLab에 빈 저장소를 만든다 (예: gate-portal). 주소는 사용자에게 받는다.

# 1) 프레임워크 태그에서 시작
git clone <프레임워크 저장소 주소> gate-portal
cd gate-portal
git checkout -b main fw-v1.0.0            # 업무 저장소가 시작할 프레임워크 버전
git remote rename origin framework        # 프레임워크는 원격 'framework'로
git remote add origin <새 업무 저장소 주소>
git push -u origin main
git checkout -b develop && git push -u origin develop
```

2. 첫 MR(`feature/<이슈>-init-business`):
   - `CLAUDE.md`: [new-repo-CLAUDE.md](new-repo-CLAUDE.md)를 복사해 업무 이름, 스키마, 영역, 오류 번호 대역, 담당 정보(이름 없이 역할만)를 채웁니다.
   - `README.md` 첫 부분에 업무 시스템 설명과 프레임워크 버전(`fw-v1.0.0`)을 적습니다(프레임워크 README 본문은 그대로 두고 위에 덧붙임).
   - `docs/business/`: 이 폴더의 [03-business-gate.md](03-business-gate.md)를 출발점으로 옮기고 업무 문서를 여기서 키웁니다.
   - 업무 스키마 폴더 `db/Portal.Database/GAT/`, `db/seed/GAT/`(빈 시드라도 MERGE 형식), 시드 등록 한 줄.
   - CI: 프레임워크 CI를 그대로 쓰고 GitLab CI/CD 변수(Protected + Masked)만 설정합니다. 연결 문자열·비밀값은 저장소에 넣지 않습니다.
3. 로컬 확인: `dotnet build Portal.sln`, `dotnet test Portal.sln --filter "Category!=Integration"`, 로컬 DB 배포(`db/ops/local-setup.ps1`) 후 로그인.
4. 이 프레임워크 저장소의 아래 "업무 저장소 목록"에 한 줄 추가합니다(프레임워크 저장소 쪽 MR).

## 7. 프레임워크 업데이트 반영

```bash
git fetch framework --tags
git checkout develop && git checkout -b chore/framework-fw-v1.1.0
git merge fw-v1.1.0                        # 충돌은 5장 공유 접점에서만 나야 정상
```

1. 릴리스 노트와 `docs/handover/` 변경 내역을 읽고, 업무 저장소가 할 일(SP 파라미터 변경, 설정 키 추가 등)을 반영합니다.
2. 빌드 + 단위 테스트. 프레임워크 업데이트는 DB 변경을 포함할 수 있으므로 **DB 통합 테스트도 돌립니다**(CI `db-integration` 수동 실행).
3. 개발계 DB에 배포할 변경 스크립트(`SqlPackage /Action:Script`)를 검토합니다.
4. MR 제목 `[이슈] 프레임워크 fw-v1.1.0 반영`, 본문에 반영한 변경과 테스트 결과.
5. 하지 않을 것: 프레임워크 커밋 일부만 골라 가져오기(cherry-pick), 업무 저장소에서 프레임워크 파일 직접 수정. 운영 긴급 수정으로 어쩔 수 없이 고쳤다면 같은 수정을 바로 프레임워크 저장소에 올리고, 다음 업데이트 병합 때 프레임워크 쪽 변경을 택합니다.

## 8. 업무 저장소 목록

| 업무 시스템 | 저장소 | 시작 프레임워크 버전 | 스키마 / 영역 | 상태 |
| --- | --- | --- | --- | --- |
| 경비대 반출입 (기존 통합반출입 재구축) | 미생성 (GitLab, 사용자가 생성) | `fw-v1.0.0` 예정 | `GAT` / `Gat` (제안) | 프레임워크 완성 대기 |
