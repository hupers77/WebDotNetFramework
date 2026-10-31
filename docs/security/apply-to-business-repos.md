# 2026-10 보안 점검 결과를 업무 저장소에 반영하는 방법

기준 시점: 2026-10-31. 대상 변경은 프레임워크 PR #66(보안 점검과 취약점 패치)입니다. 점검 결과 전체는 [2026-10-security-audit.md](2026-10-security-audit.md)에 있습니다.

이 문서는 프레임워크에서 시작한 업무 저장소(경비대 반출입 등. 이하 "업무 저장소")를 맡은 개발자와 Claude Code가 읽습니다. 업무 저장소가 프레임워크를 가져오는 방식은 [04-new-business-repo.md](../handover/04-new-business-repo.md) 7장 "프레임워크 업데이트 반영"을 따릅니다. 이 문서는 그 절차에 이번 보안 변경 때문에 더 할 일을 적은 것입니다.

## 1. 한눈에 보기

| 순서 | 할 일 | 누가 | 필수 |
| --- | --- | --- | --- |
| 1 | 프레임워크 새 태그를 업무 저장소에 병합 (2장) | 업무 저장소 개발자 | 필수 |
| 2 | 바뀐 호출 방식 맞추기: 댓글 서비스·SP, 검증 메시지 (3장 1~2) | 업무 저장소 개발자 | 해당 시 필수 |
| 3 | 업무 SP에 같은 규칙 적용: 첨부 묶음 검증, 기록 단위 권한 (3장 3~4) | 업무 저장소 개발자 | 필수 |
| 4 | `web.config`·패키지·SDK 충돌 정리 (3장 5~6) | 업무 저장소 개발자 | 해당 시 |
| 5 | 빌드·단위·DB 통합 테스트, 배포 스크립트 검토 (4장) | 업무 저장소 개발자 | 필수 |
| 6 | 서버 .NET 10.0.14 Hosting Bundle, 빌드 PC·러너 SDK 10.0.114 (5장) | 운영 담당 | 필수 |
| 7 | 업무 저장소에 같은 보안 점검 다시 실행 (6장) | 업무 저장소 개발자 (Claude Code) | 권장 |

## 2. 프레임워크 태그 병합

프레임워크 저장소 관리자가 PR #66이 병합된 `main`에 태그를 붙입니다. 버전 규칙(04 문서 2장)상 이번 변경은 다음과 같습니다.

- 대부분 버그 수정과 보안 강화입니다(`수` 또는 `부`).
- 다만 **댓글 SP 3개에 필수 파라미터 `@P_BOARD_CD`가 추가**되었고, **`IBoardService`의 댓글 메서드 3개에 `boardCd` 인자가 앞에 추가**되었습니다. 2장 기준으로는 깨지는 변경입니다.
- 업무 저장소가 이 SP나 메서드를 직접 부르지 않으면 영향이 없습니다. 지금은 업무 저장소가 게시판 댓글을 직접 부르는 곳이 없으므로 **`fw-v1.2.0`을 제안**합니다. 이미 부르는 업무 저장소가 있다면 `fw-v2.0.0`으로 냅니다.

```bash
# 프레임워크 저장소 (관리자, 태그는 사용자가 직접 붙입니다)
git checkout main && git pull
git tag -a fw-v1.2.0 -m "보안 점검 2026-10 반영 (PR #66)"
git push origin fw-v1.2.0

# 업무 저장소
git fetch framework --tags
git checkout develop && git checkout -b chore/framework-fw-v1.2.0
git merge fw-v1.2.0
```

충돌은 04 문서 5장의 공유 접점에서만 나야 정상입니다. 이번 변경에서 충돌이 날 수 있는 파일은 `Directory.Packages.props`, `global.json`, `src/Portal.Web/Program.cs`, `src/Portal.Web/web.config`(새 파일)입니다. 3장 5~6을 보고 풉니다.

## 3. 업무 저장소에서 확인하고 고칠 것

### 3-1. 게시판 댓글을 직접 부르는 코드 (깨지는 변경)

이 명령으로 업무 코드에서 부르는 곳을 찾습니다. 결과가 없으면 할 일이 없습니다.

```bash
git grep -n -E "USP_COMMENT_(GET_LIST|SAVE|DEL)|GetCommentsAsync|SaveCommentAsync|DeleteCommentAsync" -- ':!src/Portal.Web/Areas/Board' ':!db/Portal.Database/FW' ':!tests'
```

- C#: `GetCommentsAsync(postId, manager)` → `GetCommentsAsync(boardCd, postId, manager)`. `SaveCommentAsync`와 `DeleteCommentAsync`도 첫 인자로 게시판 코드를 넘깁니다. 게시판 코드는 **권한을 확인한 게시판의 코드**여야 합니다(화면이 보낸 값을 그대로 쓰지 않습니다).
- SQL: `EXEC FW.USP_COMMENT_SAVE ... , @P_BOARD_CD = @게시판코드`. 글이 그 게시판의 글이 아니면 `MSG.NOT_FOUND` 오류가 납니다.

### 3-2. 입력 검증 메시지는 메시지 키로

JSON 요청의 입력 검증이 실패하면 화면에 돌려주는 값이 바뀌었습니다. 이제 **메시지 키 형식(대문자 구역.이름, 예 `MSG.INVALID_INPUT`)만** 그대로 돌려주고, 그 밖의 문구는 모두 `MSG.INVALID_INPUT`으로 바꿉니다. JSON 변환 오류 문구에 내부 형식 이름이 드러나는 것을 막기 위한 변경입니다.

```bash
git grep -n -E "ErrorMessage\s*=\s*\"" -- 'src/**/*.cs'
```

`ErrorMessage = "필수 입력입니다"`처럼 문구를 직접 쓴 업무 모델이 있으면 다국어 키(`MSG.GAT_REQ_TITLE_REQUIRED`처럼 대문자·숫자·`_`·`.`만 쓰고 점이 들어간 키)로 바꾸고 업무 시드의 `TB_LANG_RESOURCE`에 ko/en/zh 문구를 넣습니다.

### 3-3. 첨부 묶음을 저장하는 업무 SP (필수)

이번 점검의 높음 등급 취약점 중 하나는 **남의 첨부 묶음 ID를 자기 기록에 붙여 그 파일을 받아 가는 것**이었습니다. 게시판 `USP_POST_SAVE`에는 막는 코드가 들어갔습니다. 업무 테이블에 `FILE_GROUP_ID`를 두는 업무 저장 SP는 모두 같은 확인을 넣습니다(개발 표준 가이드 첨부 절 5번).

화면이 넘긴 묶음은 다음 둘 중 하나여야 합니다.

- 그 업무 행이 **이미 가진 묶음** (수정 저장)
- **본인이 올린 확정 전 묶음** (`TB_FILE_GROUP.REG_ID = @P_USER_ID AND CONFIRM_YN = 'N'`)

```sql
-- 업무 저장 SP의 USP_FILE_GROUP_CONFIRM_EXEC 호출 앞에 둔다 (예: 반출 신청 GAT.TB_CARRY_REQ)
IF @P_FILE_GROUP_ID IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [GAT].[TB_CARRY_REQ] R
                    WHERE R.[REQ_ID] = @P_REQ_ID AND R.[FILE_GROUP_ID] = @P_FILE_GROUP_ID)
   AND NOT EXISTS (SELECT 1 FROM [FW].[TB_FILE_GROUP] G
                    WHERE G.[FILE_GROUP_ID] = @P_FILE_GROUP_ID
                      AND G.[REG_ID] = @P_USER_ID
                      AND G.[CONFIRM_YN] = 'N')
  THROW 50004, 'MSG.NOT_FOUND', 1;
```

통합 테스트도 게시판과 같이 둡니다(`BoardSpTests`의 묶음 가로채기 테스트 참고). 확인할 것은 세 가지입니다. 남이 올린 확정 전 묶음은 거부되어야 하고, 다른 기록의 확정된 묶음도 거부되어야 하며, 자기 기록이 이미 가진 묶음으로 다시 저장하는 것은 통과해야 합니다.

찾는 명령:

```bash
git grep -n -l "USP_FILE_GROUP_CONFIRM_EXEC" -- 'db/Portal.Database' ':!db/Portal.Database/FW'
```

### 3-4. 기록 단위 권한 (필수)

프로그램 권한(읽기·쓰기)만 확인하면 같은 프로그램의 모든 기록을 번호만 바꿔 다룰 수 있습니다. 이번에 고친 댓글 취약점이 이런 경우였습니다. 업무 SP는 다음을 지킵니다(개발 표준 가이드 첨부 절 4번).

1. **자식 기록(댓글, 상세 행, 첨부)을 다루는 SP는 부모가 Controller가 권한을 확인한 대상인지** SP 안에서 다시 확인합니다. 예: 상세 행 저장 SP는 `@P_REQ_ID`가 그 행의 부모인지 `JOIN`으로 확인합니다.
2. 회사·부서·본인만 보는 기록은 목록 SP뿐 아니라 **한 건 조회·수정·삭제 SP에도 같은 조건**을 넣습니다.
3. 권한이 없으면 `MSG.NOT_FOUND`로 답합니다. 기록이 있는지조차 알려 주지 않기 위해서입니다.

**첨부 다운로드의 현재 한계**: 공통 `/File/Download`·`/File/Image`는 이제 게시글 첨부·본문 사진에 글 보기 규칙을 적용합니다. 그러나 업무 기록에 붙은 묶음은 **묶음 프로그램 권한과 회사 격리까지만** 확인합니다. 그 프로그램 읽기 권한이 있으면 같은 회사의 다른 기록 첨부도 번호로 받을 수 있습니다. 업무 기록 첨부에 "그 기록을 볼 수 있는 사람만" 규칙이 필요하면, 업무 저장소에서 프레임워크 파일을 고치지 말고 **프레임워크 저장소에 확장 지점(묶음 프로그램별 업무 접근 확인 SP 연결)을 먼저 요청**합니다. 그 전까지는 민감한 첨부를 프로그램 단위로 나누어(예: 보안 반출 첨부는 별도 프로그램) 권한으로 막습니다.

### 3-5. `web.config` (새 파일)

프레임워크가 `src/Portal.Web/web.config`를 새로 가집니다. 이 파일은 다음을 처리합니다.

- `Server`·`X-Powered-By` 헤더를 지웁니다.
- 디렉터리 목록을 끕니다.
- TRACE·TRACK 메서드와 WebDAV 메서드를 막습니다.
- `App_Data`·`keys`·`logs` 경로를 숨깁니다.
- 요청 본문 크기를 30MB로 제한합니다.

- 업무 저장소에 `web.config`가 없으면 그대로 받습니다.
- 업무 저장소가 이미 `web.config`를 가지고 있으면 병합 충돌이 납니다. 프레임워크 파일을 기준으로 두고 업무에 꼭 필요한 설정만 더합니다. 그 설정이 모든 업무에 필요하면 프레임워크에 올립니다.
- 업무 첨부 한도(`FILE.MAX_SIZE_MB`)를 30MB보다 크게 쓰면 `maxAllowedContentLength`도 함께 올립니다(바이트 단위, 한도보다 조금 크게).
- 보안 응답 헤더(CSP·HSTS 등)는 애플리케이션이 붙이므로 `web.config`에 다시 넣지 않습니다. 업무 화면이 외부 스크립트·글꼴을 써야 하면 CSP 설정을 프레임워크 저장소에서 바꿉니다.

### 3-6. 패키지·SDK·그 밖의 변경

| 변경 | 업무 저장소 영향 |
| --- | --- |
| `Directory.Packages.props`: Microsoft 패키지 4개 10.0.13 → 10.0.14 | 업무 저장소가 같은 줄을 고쳤다면 높은 버전(10.0.14)을 택합니다. 업무 전용 패키지도 `dotnet list package --vulnerable --include-transitive`로 확인합니다 |
| `global.json`: SDK 10.0.114 이상 | 개발 PC·GitLab 러너에 SDK 10.0.114 이상 설치(5장) |
| `/health`가 외부 호출에는 버전을 주지 않음 | 모니터링이 버전을 읽었다면 서버 자신(localhost)에서 부르도록 바꿉니다 |
| HSTS 1년 + 하위 도메인 | 포털 도메인의 하위 도메인에 HTTP 전용 사이트가 있으면 운영 담당과 먼저 확인 |
| 서식 편집기 HTML 필터(`HtmlSanitizer`) 정규식 변경 | 업무 화면도 HTML 본문은 반드시 `HtmlSanitizer.Sanitize`를 거쳐 저장합니다. 업무 코드에서 정규식으로 HTML을 거르지 않습니다 |
| 업로드 차단 확장자 추가(mht, chm, iso, vhd, ps1xml, appx 등) | 업무에서 이런 파일을 받아야 하면 프레임워크 저장소에 예외를 요청합니다 |
| 송신 API 사설 주소 판단(NAT64·6to4 등) | 업무 송신 대상이 이런 주소면 사내망 허용(`ALLOW_PRIVATE_YN`)을 켜야 합니다 |

## 4. 테스트와 배포 확인

1. 빌드와 단위 테스트: `dotnet build Portal.sln`, `dotnet test Portal.sln --filter "Category!=Integration"`
2. DB 통합 테스트(CI `db-integration` 수동 실행 또는 로컬 SQL Server). 바뀐 SP는 다음과 같습니다.
   - 댓글 SP 3개
   - `USP_POST_SAVE`
   - 새 SP `USP_FILE_GROUP_ACCESS_GET`
   - `TB_POST` 필터 인덱스 2개
3. 개발계 DB 배포 스크립트(`SqlPackage /Action:Script`)를 검토합니다. 이번 변경은 SP 바꾸기와 인덱스 추가만 있고 표 구조·데이터 변경은 없습니다.
4. 브라우저(Edge) 확인은 다음 네 가지입니다.
   - 남의 비밀글 첨부 번호로 `/File/Download?fileId=…`를 열면 404가 나야 합니다.
   - 읽기 권한만 있는 계정으로 남의 첨부를 지우면 403이 나야 합니다.
   - 업무 화면의 첨부 올리기·받기·지우기가 정상이어야 합니다.
   - 서식 편집기 저장이 정상이어야 합니다.
5. 실제 IIS 확인은 다음과 같습니다.
   - 응답에 `Server` 헤더가 없어야 합니다.
   - `TRACE` 요청이 405여야 합니다.
   - `/logs/`가 404여야 합니다.
   - 6장 도구 `Test-WebSecurity.ps1`로 한 번에 확인할 수 있습니다.
6. MR 본문에 반영한 항목(3장 번호)과 테스트 결과를 적습니다.

## 5. 서버·개발 PC 패치 (운영 담당)

| 대상 | 할 일 | 확인 |
| --- | --- | --- |
| 운영·개발 웹 서버 | .NET 10.0.14 **Hosting Bundle** 설치 후 IIS 재시작(`iisreset`). CVE-2026-111497(Brotli), CVE-2026-70197(소켓) | `dotnet --list-runtimes`에 `Microsoft.AspNetCore.App 10.0.14` |
| 개발 PC·GitLab 러너 | .NET SDK 10.0.114 이상 | `dotnet --version` |
| Windows·SQL Server | 최신 누적 업데이트(점검 보고서 R-02) | Windows Update, SQL Server CU 이력 |

업무 시스템마다 웹 서버가 따로 있으면 서버마다 설치합니다. 같은 서버에 여러 업무 사이트가 있으면 한 번 설치로 모두 적용됩니다.

## 6. 업무 저장소에 같은 점검 다시 하기

이번 점검 절차는 Claude Code skill `web-security-audit`로 만들어 두었습니다. 위치는 프레임워크 저장소 `.claude/skills/web-security-audit/`입니다. 업무 저장소는 프레임워크를 병합하면 같은 폴더를 받으므로 바로 쓸 수 있습니다.

```text
# 업무 저장소에서 Claude Code를 열고
/web-security-audit 이 저장소 전체를 KISA 기준으로 점검하고, 취약점은 패치까지 해줘
```

- 업무 코드만 보려면 "업무 영역(`Areas/Gat`, `db/Portal.Database/GAT`)만 점검해줘"처럼 범위를 말합니다.
- 결과는 `docs/security/<날짜>-security-audit.md`에 항목별 양호/취약/권고로 남습니다.
- 프레임워크 파일에서 나온 취약점은 업무 저장소에서 고치지 않고 프레임워크 저장소에 올립니다(04 문서 1장 3번).

사용법과 다른 사내 시스템에 쓰는 방법은 [web-security-audit 사용 안내](../../.claude/skills/web-security-audit/README.md)에 있습니다.
