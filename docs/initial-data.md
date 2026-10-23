# 포털 초기 데이터 정의서

Oct 21, 2026 · @사랑나무아빠

## 1. 개요

이 문서는 포털을 처음 설치할 때 들어가는 역할, 메뉴, 공통코드, 설정값의 기준표입니다. 실제 데이터는 GitLab의 `db/seed/` 스크립트(`MERGE`, 반복 실행 가능)가 배포 때마다 넣고, 이 문서와 스크립트는 같은 MR에서 함께 고칩니다. 표의 값은 설계 단계 초안이며 1단계 구축 중 확정합니다.

**시드 규칙**

- 시드 스크립트는 없는 행만 추가하고, 운영자가 화면에서 바꾼 값(메뉴 이름, 설정값 등)은 덮어쓰지 않습니다. 예외는 `SYSTEM_YN = 'Y'`인 시스템 코드로, 이것은 항상 스크립트 값을 따릅니다.
- 시드로 넣은 행은 삭제하지 않고 `USE_YN = 'N'`으로 끕니다.
- 비밀번호, SMTP 계정, AD 연결 정보 같은 비밀값은 시드에 넣지 않고, 설치 후 설정 관리 화면에서 입력합니다.

**환경별 차이**

| 항목 | 개발계 | 운영계 |
| --- | --- | --- |
| 메일 발송 (`MAIL.ENABLED`) | N (큐에만 저장) | Y |
| 사이트 상단 표시 | "개발계" 배지 표시 | 없음 |
| 테스트 계정 | 본사/협력사 테스트 계정 생성 | 없음 |
| 외부 API 송신 대상 | 각 시스템의 테스트 주소 | 운영 주소 |

## 2. 언어·회사·초기 관리자 계정

설치 직후에는 언어 3개, 본사 회사 1개, 관리자 계정 1개만 있습니다. 협력사와 일반 사용자는 운영 중 화면에서 등록하거나 AD 동기화로 들어옵니다.

**언어 (`TB_LANG`)**

| 코드 | 이름 | 기본 | 정렬 |
| --- | --- | --- | --- |
| `ko` | 한국어 | Y | 1 |
| `en` | English | N | 2 |
| `zh-CN` | 简体中文 | N | 3 |

**회사 (`TB_COMPANY`)**

| 회사 코드 | 이름 | 유형 | 비고 |
| --- | --- | --- | --- |
| `HQ` | (본사명, 설치 시 입력) | `HQ` | 코드는 확정 후 변경 가능. 협력사는 `P0001`부터 자동 채번 |

부서(`TB_DEPT`)는 초기 데이터가 없습니다. 본사 부서는 3단계 AD 동기화로 들어오고, 협력사 부서와 그 전의 부서는 부서 관리 화면에서 만들며 ID는 `D000001`부터 자동으로 붙습니다.

**초기 관리자 계정**

| 항목 | 값 |
| --- | --- |
| 사용자 ID | `sysadmin` |
| 유형 / 회사 | `SYSTEM` / `HQ` |
| 역할 | `SYS_ADMIN` |
| 비밀번호 | 설치 스크립트 실행 시 임시 비밀번호를 무작위 생성해 화면에 1회만 출력. 최초 로그인 시 변경 강제 + 2단계 인증 등록 |

이 계정은 초기 데이터 스크립트에 넣지 않습니다(해시를 저장소에 둘 수 없으므로). 설치 도구(웹 호스트의 `init-admin` 명령: `dotnet Portal.Web.dll init-admin`)가 `AdminBootstrapService`(`FW.USP_AUTH_ADMIN_INIT_EXEC`)로 만들고, 같은 명령을 다시 실행하면 비상 재설정(새 임시 비밀번호, 잠금 해제, 열린 세션 종료, 감사 로그 기록)이 됩니다.

`sysadmin`은 비상용으로만 쓰고, 실제 관리자에게는 각자 계정에 `PORTAL_ADMIN` 역할을 줍니다. 그래야 감사 로그에 누가 바꾼는지 남습니다.

## 3. 기본 역할

기본 역할은 7개이며, 업무 역할(구매 담당 등)은 업무 개발 단계에서 추가합니다. "줄 수 있는 회사"(`TB_ROLE.COMPANY_TYPE_CD`)가 정해진 역할은 그 유형 회사의 사용자에게만 줄 수 있습니다. 본사 사용자는 AD 동기화 시 `EMP_USER`, 협력사 사용자는 승인 시 `PARTNER_USER`를 자동으로 받습니다.

| 역할 ID | 이름 | 대상 | 줄 수 있는 회사 | 데이터 범위 | 주요 권한 |
| --- | --- | --- | --- | --- | --- |
| `SYS_ADMIN` | 시스템 관리자 | 비상 계정 | 본사 | 전체 | 모든 기능 (감사 로그 삭제 불가) |
| `PORTAL_ADMIN` | 포털 운영 관리자 | 본사 운영 담당 | 본사 | 전체 | 사용자·회사·역할·메뉴·코드·다국어·설정·게시판 관리 |
| `SECURITY_AUDITOR` | 보안 감사자 | 정보보안 담당 | 본사 | 전체 | 로그인·접근·감사·개인정보 접속기록 조회 (변경 권한 없음) |
| `EMP_USER` | 본사 일반 사용자 | 본사 임직원 전체 | 본사 | 본인 | 홈, 게시판 읽기·글쓰기, 공통 조회 화면, 내 정보 |
| `PARTNER_ADMIN` | 협력사 관리자 | 협력사별 담당자 | 협력사 | 소속 회사 | 자기 회사 사용자 신청·잠금 해제, 소속 회사 데이터 조회 |
| `PARTNER_USER` | 협력사 사용자 | 협력사 직원 전체 | 협력사 | 본인 (최대 소속 회사) | 홈, 공개 게시판 읽기, 공통 조회 화면, 내 정보 |
| `API_CLIENT` | 외부 연동 | 연동 시스템 계정 | 본사 | 클라이언트별 지정 | 허용된 API만 호출 |

## 4. 시스템 프로그램·메뉴 목록과 기본 권한

프레임워크가 제공하는 화면은 아래 35개이고, 모두 하나의 메뉴 트리에 들어갑니다. "공개"는 메뉴의 `OPEN_SCOPE_CD`이고, "기본 권한"은 설치 시 부여되는 역할과 기능입니다(`SYS_ADMIN`은 전부 허용이므로 생략). 약어: R 조회, C 등록, U 수정, D 삭제, E 내보내기.

| 메뉴 경로 | 프로그램 ID | 공개 | 기본 권한 |
| --- | --- | --- | --- |
| 홈 | `DSH_HOME` | ALL | 모든 역할: R |
| 내 정보 > 내 정보 | `MY_PROFILE` | ALL | 모든 역할: R, U |
| 내 정보 > 비밀번호 변경 | `MY_PWD_CHG` | ALL | 모든 역할: R, U |
| 내 정보 > 알림함 | `MY_NOTIFICATION` | ALL | 모든 역할: R, D |
| 내 정보 > 내 레포트 | `RPT_RUNS` | ALL | 모든 역할: R, D |
| 게시판 > 공지사항 | `BRD_NOTICE` | ALL | 모든 역할: R / PORTAL\_ADMIN: C, U, D |
| 게시판 > 자료실 | `BRD_ARCHIVE` | ALL | 모든 역할: R / PORTAL\_ADMIN: C, U, D |
| 게시판 > FAQ·문의 | `BRD_QNA` | ALL | 모든 역할: R, C / PORTAL\_ADMIN: U, D |
| 게시판 > 사내 게시판 | `BRD_EMP_FREE` | EMP | EMP\_USER: R, C |
| 협력사 > 협력사 사용자 관리 | `PTN_USER_MGT` | PARTNER | PARTNER\_ADMIN: R, C, U, E |
| 시스템 관리 > 사용자 관리 | `SYS_USER_MGT` | EMP | PORTAL\_ADMIN: R, C, U, E |
| 시스템 관리 > 협력사 계정 승인 | `SYS_PARTNER_APPROVE` | EMP | PORTAL\_ADMIN: R, APPROVE |
| 시스템 관리 > 회사 관리 | `SYS_COMPANY_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 부서 관리 | `SYS_DEPT_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 역할 관리 | `SYS_ROLE_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 역할별 권한 | `SYS_ROLE_AUTH_MGT` | EMP | PORTAL\_ADMIN: R, U |
| 시스템 관리 > 사용자별 예외 권한 | `SYS_USER_AUTH_MGT` | EMP | PORTAL\_ADMIN: R, U |
| 시스템 관리 > 프로그램 관리 | `SYS_PROGRAM_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 메뉴 관리 | `SYS_MENU_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 공통코드 | `SYS_CODE_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D, E, UPLOAD |
| 시스템 관리 > 다국어 관리 | `SYS_LANG_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D, E, UPLOAD |
| 시스템 관리 > 설정 관리 | `SYS_CONFIG_MGT` | EMP | PORTAL\_ADMIN: R, U |
| 시스템 관리 > 휴일·달력 | `SYS_HOLIDAY_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 게시판 관리 | `BRD_BOARD_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 공지 팝업 | `SYS_POPUP_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 메시지 템플릿 | `SYS_MSG_TEMPLATE_MGT` | EMP | PORTAL\_ADMIN: R, U |
| 시스템 관리 > 위젯 관리 | `DSH_WIDGET_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 시스템 관리 > 레포트 관리 | `RPT_REPORT_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 연계 관리 > API 클라이언트 | `SYS_API_CLIENT_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 연계 관리 > 송신 대상 | `SYS_API_OUTBOUND_MGT` | EMP | PORTAL\_ADMIN: R, C, U, D |
| 운영 > 스케줄 관리 | `SYS_JOB_MGT` | EMP | PORTAL\_ADMIN: R, U, EXEC |
| 운영 > 운영 모니터링 | `SYS_MONITOR_INQ` | EMP | PORTAL\_ADMIN: R |
| 로그 > 로그인·접근 기록 | `LOG_ACCESS_INQ` | EMP | PORTAL\_ADMIN, SECURITY\_AUDITOR: R, E, PRINT |
| 로그 > 데이터 변경 기록 | `LOG_AUDIT_INQ` | EMP | SECURITY\_AUDITOR: R, E, PRINT |
| 로그 > 개인정보 접속기록 | `LOG_PII_INQ` | EMP | SECURITY\_AUDITOR: R, E, PRINT |
| 로그 > 월별 접속기록 점검 | `RPT_LOG_MONTHLY` | EMP | SECURITY\_AUDITOR: R, E, PRINT |

`EXEC`(작업 수동 실행)는 `FUNC_CD`에 추가되는 사용자 정의 기능입니다. `PRINT`는 레포트 PDF와 화면의 [인쇄] 버튼(현재 화면 PDF)입니다(8장). 메뉴는 고정 키(`TB_MENU.MENU_KEY`, 위 표의 프로그램 ID 또는 폴더 키 `MY`, `BRD`, `PTN`, `SYS`, `INTG`, `OPS`, `LOG`)로 식별하므로 운영 중 메뉴를 옮기거나 이름을 바꿔도 다음 배포의 시드가 덮어쓰지 않습니다. 시드 원본은 `db/seed/FW/06_PROGRAM.sql`, `07_MENU.sql`입니다. 같은 `06_PROGRAM.sql`의 `TB_PROGRAM_API`는 요청 경로(예: `POST /Sys/Code/Save`)별 프로그램·기능으로, 코드의 `[Program]`/`[Func]`와 같아야 하며 화면이 만들어질 때마다 추가됩니다(이 표는 운영자가 바꾸는 값이 아니므로 시드가 항상 덮어씁니다). 표준 화면 예제 8종(`SMP_*`)은 개발계에만 등록하고 운영계에서는 메뉴에 넣지 않습니다.

## 5. 공통코드 그룹과 값

아래 그룹은 프레임워크 동작에 필요한 시스템 코드(`SYSTEM_YN = 'Y'`)로, 화면에서 명칭·정렬·사용 여부는 바꿀 수 있지만 코드값은 추가·삭제할 수 없습니다. `FUNC_CD`, `FILE_EXT`, `ICON`은 예외로 값을 추가할 수 있습니다(`TB_CODE_GROUP.ALLOW_ADD_YN = 'Y'`). 시드 원본은 `db/seed/FW/04_CODE.sql`이며 코드명은 3개 언어로 들어갑니다.

| 그룹 코드 | 이름 | 값 (코드: 명칭) |
| --- | --- | --- |
| `USER_TYPE` | 사용자 유형 | EMP: 본사, PARTNER: 협력사, SYSTEM: 시스템 |
| `USER_STAT` | 사용자 상태 | PENDING: 승인 대기, ACTIVE: 사용, LOCKED: 잠김, DORMANT: 휴면, EXPIRED: 기간 만료, RETIRED: 퇴사·계약 종료 |
| `COMPANY_TYPE` | 회사 유형 | HQ: 본사, PARTNER: 협력사 |
| `DATA_SCOPE` | 데이터 범위 | ALL: 전체, COMPANY: 소속 회사, DEPT\_SUB: 소속 부서+하위, DEPT: 소속 부서, SELF: 본인 |
| `FUNC_CD` | 기능(버튼) | READ, CREATE, UPDATE, DELETE, EXPORT, PRINT, UPLOAD, APPROVE, PII, EXEC |
| `MENU_TYPE` | 메뉴 유형 | FOLDER, PAGE, LINK, POPUP |
| `OPEN_SCOPE` | 공개 대상 | ALL: 모든 회사, EMP: 본사, PARTNER: 협력사 |
| `BOARD_TYPE` | 게시판 유형 | NORMAL: 일반, NOTICE: 공지, FAQ, ARCHIVE: 자료실, QNA: 문의 |
| `CONFIG_CATEGORY` | 설정 분류 | AUTH, PWD, SESSION, USER, FILE, UI, SITE, MAIL, MSG, PARTNER, JOB, LOG, SP, PDF, API, AD, APPROVAL, NOTIFY, SYS |
| `VAL_TYPE` | 설정값 형식 | STRING, INT, BOOL, JSON, SECRET |
| `FILE_EXT` | 업로드 허용 확장자 | pdf, xlsx, xls, docx, doc, pptx, ppt, hwp, hwpx, txt, csv, jpg, jpeg, png, gif, zip |
| `GRANT_TYPE` | 개인 권한 유형 | GRANT: 추가 허용, DENY: 차단 |
| `APPROVAL_STAT` | 결재 상태 | DRAFT: 작성, REQUESTED: 상신, APPROVED: 승인, REJECTED: 반려, CANCELED: 회수 |
| `JOB_STAT` | 배치 실행 결과 | RUNNING, SUCCESS, FAILED, SKIPPED |
| `JOB_RUN_TYPE` | 배치 실행 구분 | SCHEDULE: 예약, MANUAL: 수동 |
| `MSG_CHANNEL` | 알림 채널 | PORTAL: 포털 알림, MAIL: 메일 |
| `LOGIN_RESULT` | 로그인 결과·세션 종료 사유 | SUCCESS, FAIL\_PWD(없는 ID 포함), FAIL\_LOCKED, FAIL\_EXPIRED, FAIL\_MFA, FAIL\_IP, FAIL\_DUP: 중복 로그인 거부, LOGOUT, KICKED: 중복 로그인 종료, TIMEOUT: 비활동 만료, DISABLED: 로그인 중 계정 잠김·만료 |
| `ICON` | 메뉴 아이콘 | home, user, board, chart, report, setting, link 등 (자체 SVG 아이콘 세트 이름) |

업무 코드(직급, 직책, 거래처 유형 등)는 업무 개발 단계에서 `SYSTEM_YN = 'N'`으로 추가합니다.

## 6. 설정 키와 기본값

설정값은 설정 관리 화면에서 바꾸며 변경 이력이 남습니다. "재시작"이 Y인 항목만 앱 풀 재시작 후 반영되고, 나머지는 5초 안에 반영됩니다. SECRET 형식은 Data Protection으로 암호화해 저장하고 화면·변경 이력·감사 로그에 값을 남기지 않습니다(입력 여부만 표시). 설치 후 "(입력)" 항목은 설정 관리 화면에서 채웁니다.

| 설정 키 | 기본값 | 형식 | 재시작 | 설명 |
| --- | --- | --- | --- | --- |
| `AUTH.MODE` | BOTH | STRING | N | FORM / AD / BOTH |
| `AUTH.AD_AUTO_PROVISION` | Y | BOOL | N | AD 로그인 시 포털 계정 자동 생성 |
| `AUTH.PARTNER_MFA` | Y | BOOL | N | 협력사 2단계 인증 |
| `AUTH.ADMIN_MFA` | Y | BOOL | N | 관리자 역할 2단계 인증 |
| `AUTH.PARTNER_EMAIL_OTP` | Y | BOOL | N | 협력사 2단계 인증에 이메일 인증번호 허용 |
| `AUTH.ADMIN_EMAIL_OTP` | N | BOOL | N | 관리자 역할 2단계 인증에 이메일 인증번호 허용 |
| `AUTH.LOGIN_RATE_PER_MIN` | 10 | INT | N | IP당 분당 로그인 시도 한도 |
| `PWD.MIN_LEN_3TYPES` | 8 | INT | N | 3종류 조합 최소 길이 |
| `PWD.MIN_LEN_2TYPES` | 10 | INT | N | 2종류 조합 최소 길이 |
| `PWD.HISTORY_COUNT` | 3 | INT | N | 재사용 금지 이력 수 |
| `PWD.EXPIRE_DAYS` | 90 | INT | N | 변경 주기 |
| `PWD.EXPIRE_NOTICE_DAYS` | 7 | INT | N | 비밀번호 만료 사전 안내 (만료 며칠 전, 0이면 안내 안 함) |
| `PWD.LOCK_COUNT` | 5 | INT | N | 실패 잠금 횟수 |
| `PWD.LOCK_MINUTES` | 30 | INT | N | 자동 해제 시간 (0 = 관리자 해제만) |
| `SESSION.IDLE_MINUTES` | 30 | INT | N | 비활동 만료 |
| `SESSION.CONCURRENT_POLICY` | KICK\_OLD | STRING | N | KICK\_OLD / DENY\_NEW / ALLOW |
| `SESSION.KEEP_DAYS` | 30 | INT | N | 끝난 세션 행 보관 기간(일). 로그인 기록은 따로 10년 보관 |
| `USER.DORMANT_DAYS` | 365 | INT | N | 미접속 휴면 전환 (일, 0이면 휴면 전환 안 함) |
| `USER.DORMANT_NOTICE_DAYS` | 30 | INT | N | 휴면 전환 사전 안내 (전환 며칠 전, 안내 후 이 기간이 지나야 전환) |
| `FILE.MAX_SIZE_MB` | 20 | INT | N | 파일 1개 최대 크기 |
| `FILE.MAX_COUNT` | 10 | INT | N | 첨부 묶음 하나의 최대 파일 수 |
| `FILE.DELETED_KEEP_DAYS` | 30 | INT | N | 삭제한 첨부를 저장소에서 지우기까지 기간(일) |
| `UI.TAB_MODE` | Y | BOOL | N | 멀티 탭 사용 |
| `UI.MAX_TABS` | 10 | INT | N | 최대 탭 수 |
| `UI.PAGE_SIZE` | 50 | INT | N | 그리드 기본 행 수 |
| `UI.DEFAULT_LANG` | ko | STRING | N | 기본 언어 |
| `SITE.NAME` | (설치 시 입력) | STRING | N | 사이트명 (다국어 키 `SITE.NAME`을 우선 사용) |
| `SITE.THEME_COLOR` | #1F5FBF | STRING | N | 브랜드 색상 |
| `SITE.BASE_URL` | (설치 시 입력) | STRING | N | 포털 주소 (메일 링크, 예: https://portal.example.com) |
| `MAIL.ENABLED` | N | BOOL | N | 실제 발송 여부 |
| `MAIL.SMTP_HOST` / `MAIL.SMTP_PORT` | (입력) / 587 | STRING / INT | N | SMTP 서버 |
| `MAIL.SMTP_USER` | (입력) | STRING | N | SMTP 계정 (비우면 인증 없이 발송: 사내 릴레이) |
| `MAIL.SMTP_SSL` | Y | BOOL | N | SMTP 암호화(STARTTLS) 사용 |
| `MAIL.SMTP_PASSWORD` | (입력) | SECRET | N | SMTP 계정 비밀번호 |
| `MAIL.FROM` | (입력) | STRING | N | 보내는 주소 |
| `MSG.KEEP_DAYS` | 90 | INT | N | 발송을 마친 메일 대기열 보관 기간(일) |
| `PARTNER.JOIN_KEEP_DAYS` | 30 | INT | N | 처리한 가입 신청의 이메일 보관 기간(일) |
| `PARTNER.EXPIRE_NOTICE_DAYS` | 7 | INT | N | 협력사 계정 만료 사전 안내 (기한 며칠 전, 0이면 안내 안 함) |
| `JOB.HIST_KEEP_DAYS` | 365 | INT | N | 배치 작업 실행 이력 보관 기간(일) |
| `LOG.RETENTION_YEARS` | 10 | INT | N | 접속기록·감사 로그 보관 기간 |
| `LOG.ERROR_RETENTION_DAYS` | 365 | INT | N | 오류 로그 보관 기간 |
| `LOG.SLOW_SP_MS` | 1000 | INT | N | 느린 SP 기준 |
| `SP.COMMAND_TIMEOUT_SEC` | 30 | INT | Y | SP 명령 타임아웃 |
| `PDF.MAX_CONCURRENCY` | 4 | INT | Y | 동시 PDF 변환 수 |
| `PDF.EDGE_PATH` | C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe | STRING | N | Edge 실행 파일 경로 |
| `API.TOKEN_MINUTES` | 30 | INT | N | 외부 API 토큰 유효 시간 |
| `API.DEFAULT_RATE_PER_MIN` | 600 | INT | N | 클라이언트 기본 호출 한도 |
| `AD.SYNC_VIEW` | (입력) | STRING | N | AD 마스터 뷰 이름 (조직·사용자 동기화 원본) |
| `APPROVAL.GATEWAY` | NONE | STRING | Y | 전자결재 연동 구현 이름 (업무 개발 시 지정) |
| `APPROVAL.VIEW_URL` | (입력) | STRING | N | 전자결재 화면 주소 (결재 문서 번호 자리에 `{DOC_ID}`) |
| `NOTIFY.POLL_SECONDS` | 60 | INT | N | 알림 확인 주기 (헤더 배지, 세션을 연장하지 않는 요청) |
| `NOTIFY.KEEP_DAYS` | 90 | INT | N | 포털 알림 보관 기간(일, 읽었는지와 상관없이) |
| `SYS.MAINTENANCE_MODE` | N | BOOL | N | Y면 관리자만 접속 |

서버마다 다른 값(DB 연결 문자열, 파일 저장 루트, 활성 모듈, 로그 파일 경로, Data Protection 키 폴더)은 이 표가 아니라 `appsettings.{환경}.json`에 둡니다.

## 7. 기본 배치 작업·메시지 템플릿

배치 작업은 `TB_JOB`에 등록하고 Portal.Worker가 실행합니다(`db/seed/FW/11_JOB.sql`). 주기는 Cron 식(분 시 일 월 요일)이며 한국 시간 기준이고, 백업 작업(02:00\~03:00)과 겹치지 않게 배치했습니다. 실패하면 `PORTAL_ADMIN` 역할 사용자에게 메일(`TPL_JOB_FAILED`)이 가고, 같은 작업이 연속으로 실패하면 다시 성공할 때까지는 한 번만 보냅니다. 같은 내용이 포털 알림함에도 들어갑니다. 작업은 그 기능을 만드는 변경에서 시드에 추가합니다.

| 작업 ID | 내용 | 주기 (Cron) | 상태 |
| --- | --- | --- | --- |
| `JOB_MAIL_SEND` | 메일 큐 발송, 실패 시 3회 재시도 | 1분마다 (`* * * * *`) | 등록됨 (클래스 `MailSend`) |
| `JOB_SESSION_CLEANUP` | 시간이 지난 세션 종료, 끝난 지 30일 지난 세션 행 삭제 | 10분마다 (`*/10 * * * *`) | 등록됨 (SP `FW.USP_JOB_SESSION_CLEANUP_EXEC`) |
| `JOB_TEMP_CLEANUP` | 확정되지 않은 첨부(하루 지남)·삭제한 지 30일 지난 첨부를 저장소에서 삭제 | 매일 05:00 (`0 5 * * *`) | 등록됨 (클래스 `FileCleanup`) |
| `JOB_DATA_CLEANUP` | 처리한 가입 신청 이메일(30일), 발송 끝난 메일 대기열(90일), 작업 이력(365일), 포털 알림(90일) 삭제 | 매일 05:10 (`10 5 * * *`) | 등록됨 (SP `FW.USP_JOB_DATA_CLEANUP_EXEC`) |
| `JOB_AD_SYNC` | AD 마스터 뷰에서 조직·사용자 동기화, 퇴사자 잠금 | 매일 04:00 | 3단계 |
| `JOB_USER_DORMANT` | 미접속 계정 휴면 전환 (30일 전 사전 안내, 안내 없이 바로 전환하지 않음, 시스템 계정 제외) | 매일 04:30 (`30 4 * * *`) | 등록됨 (SP `FW.USP_JOB_USER_DORMANT_EXEC`) |
| `JOB_PARTNER_EXPIRE` | 사용 기한(계정 기한과 회사 계약 종료일 중 이른 날)이 지난 협력사 계정 만료(`EXPIRED`), 7일 전 사전 안내 (기한을 늘리면 다시 안내) | 매일 04:40 (`40 4 * * *`) | 등록됨 (SP `FW.USP_JOB_PARTNER_EXPIRE_EXEC`) |
| `JOB_PWD_EXPIRE_NOTICE` | 비밀번호 만료 7일 전 안내 (메일과 포털 알림, 변경 주기마다 한 번, 이미 만료·변경 강제·AD 전용·시스템 계정 제외) | 매일 08:00 (`0 8 * * *`) | 등록됨 (SP `FW.USP_JOB_PWD_EXPIRE_NOTICE_EXEC`) |
| `JOB_LOG_PARTITION` | 2개월 뒤까지 로그 월 파티션 생성, 보관 기간 지난 파티션 비우기 (접속기록·감사 10년 `LOG.RETENTION_YEARS`, 오류 로그 365일 `LOG.ERROR_RETENTION_DAYS`, 0이면 지우지 않음) | 매월 1일 05:00 (`0 5 1 * *`) | 등록됨 (SP `LOG.USP_JOB_LOG_PARTITION_EXEC`) |
| `JOB_LOG_INTEGRITY` | 지난달 로그인·접근·데이터 변경·개인정보 접속기록의 해시 체인 점검 (위·변조나 삭제가 있으면 실패로 알림) | 매월 1일 06:00 (`0 6 1 * *`) | 등록됨 (클래스 `LogIntegrity`) |

**메시지 템플릿 (`TB_MSG_TEMPLATE`, 3개 언어)**

| 템플릿 ID | 용도 | 채널 |
| --- | --- | --- |
| `TPL_PARTNER_SIGNUP_REQ` | 협력사 가입 신청 접수 (승인 권한 `SYS_PARTNER_APPROVE:APPROVE`이 있는 사용자에게, 알림을 누르면 협력사 계정 승인 화면) | 메일, 포털 |
| `TPL_PARTNER_APPROVED` | 계정 승인 완료 및 최초 로그인 안내 | 메일 |
| `TPL_PARTNER_REJECTED` | 가입 반려 안내 | 메일 |
| `TPL_PWD_RESET` | 비밀번호 초기화 링크 (30분 유효) | 메일 |
| `TPL_PWD_EXPIRE` | 비밀번호 만료 예정 (알림을 누르면 비밀번호 변경 화면) | 메일, 포털 |
| `TPL_DORMANT_NOTICE` | 휴면 전환 예정 | 메일 |
| `TPL_ACCOUNT_EXPIRE` | 협력사 계정 만료 예정 | 메일 |
| `TPL_ACCOUNT_LOCKED` | 로그인 실패로 계정 잠김 | 메일 |
| `TPL_JOB_FAILED` | 배치 작업 실패 (관리자에게, 알림을 누르면 스케줄 관리 화면) | 메일, 포털 |
| `TPL_REPORT_DONE` | 레포트 백그라운드 생성 완료 (요청자에게, 알림을 누르면 내 레포트 화면) | 포털 |
| `TPL_REPORT_FAILED` | 레포트 백그라운드 생성 실패 (요청자에게) | 포털 |
| `TPL_BOARD_REPLY` | 내 글에 답글이 달림 (원 글 작성자에게, 본인 제외) | 포털 |
| `TPL_BOARD_COMMENT` | 내 글에 댓글이 달림 (글 작성자에게, 본인 제외) | 포털 |

2단계 인증 이메일 인증번호(5분 유효)는 메일 대기열을 거치지 않고 로그인 도중 바로 보내므로(번호를 대기열 테이블에 남기지 않기 위해) 템플릿이 아니라 언어 리소스 `MAIL.OTP_SUBJECT`·`MAIL.OTP_BODY`(`{0}` = 번호)를 씁니다.

템플릿 본문의 치환 변수는 `{{USER_NM}}`, `{{SITE_NM}}`, `{{LINK}}`, `{{EXPIRE_DT}}`처럼 쓰고, 치환값은 HTML 인코딩되어 들어갑니다. 메일 본문에 비밀번호 자체는 넣지 않습니다.

템플릿은 그 기능을 만드는 변경에서 `db/seed/FW/10_MSG_TEMPLATE.sql`에 추가합니다(현재: `TPL_PARTNER_SIGNUP_REQ`, `TPL_PARTNER_APPROVED`, `TPL_PARTNER_REJECTED`, `TPL_PWD_EXPIRE`, `TPL_DORMANT_NOTICE`, `TPL_ACCOUNT_EXPIRE`, `TPL_JOB_FAILED`, `TPL_REPORT_DONE`, `TPL_REPORT_FAILED`). 템플릿마다 메일(`MAIL_YN`)·포털(`PORTAL_YN`) 채널을 켜고 끌 수 있고, `USE_YN = 'N'`이면 쌓지 않습니다. 포털 알림은 받는 사람이 포털 사용자일 때만 쌓이고, 제목은 템플릿 제목(앞의 [사이트명] 제외), 내용은 본문에서 글자만 남긴 것이며, 볼 때 보는 사람의 화면 언어로 만듭니다. `LINK_PROGRAM_ID`를 정하면 알림을 눌렀을 때 그 화면을 탭으로 엽니다. `{{SITE_NM}}`(받는 사람 언어의 사이트명)과 `{{LINK}}`(포털 주소)는 발송 작업이 채우고, 나머지 치환값은 업무 SP가 JSON으로 넘깁니다. 받는 사람 언어의 문구가 없으면 기본 언어 문구로 보냅니다.

## 8. 기본 레포트

레포트는 `TB_REPORT`에 등록하면 레포트 화면(`/Rpt/Viewer/Index/{레포트 ID}`)에서 조건 입력·미리보기·PDF·엑셀을 쓸 수 있습니다(`db/seed/FW/12_REPORT.sql`). 레포트 ID는 그 레포트의 프로그램 ID와 같고, 메뉴에는 레포트 화면 주소로 등록합니다(4장 표). 권한은 조회(미리보기) R, PDF `PRINT`, 엑셀 E입니다. 조건·최대 행 수는 운영 중 바꿀 수 있고 다음 배포의 시드가 덮어쓰지 않습니다.

| 레포트 ID | 내용 | 데이터 SP | 조건 | 출력 |
| --- | --- | --- | --- | --- |
| `RPT_LOG_MONTHLY` | 월별 접속기록 점검: 그 달 날마다 로그인 성공·실패·잠긴 계정 시도, 요청·권한 거부(401·403)·서버 오류(500 이상), 개인정보 조회·다운로드·마스킹 해제, 데이터 변경 건수와 합계, 점검 의견란 | `LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST` | 월 (기본: 지난달, 필수) | A4 가로, 결재란(담당·검토·승인), 최대 40행 |

`RPT_LOG_MONTHLY`는 개인정보 안전성 확보조치 기준의 "접속기록 월 1회 이상 점검"을 출력물로 남기기 위한 것입니다. 회사에 속하지 않는 기록(없는 ID로 시도한 로그인 등)까지 세므로 데이터 범위가 '전체'인 역할(`SECURITY_AUDITOR`)만 볼 수 있고, 건수만 보여 주므로 개인정보 레포트(`PII_YN`)가 아닙니다. 매월 초 지난달을 PDF로 출력해 점검 의견을 적고 결재합니다.

## 9. 기본 게시판

게시판은 `TB_BOARD`에 등록하고(`db/seed/FW/13_BOARD.sql`), 게시판 하나가 프로그램 하나에 대응합니다. 화면 주소는 `/Board/{게시판 코드}`이고 권한은 게시판의 프로그램에 4장의 기본 권한으로 겁니다. 이름은 `TB_BOARD_LANG`에 언어별로 둡니다. 게시판 관리 화면(`BRD_BOARD_MGT`)에서 설정을 바꿀 수 있고, 시드는 이미 있는 게시판의 설정을 덮어쓰지 않습니다.

| 게시판 코드 | 프로그램 | 유형 | 답글 | 댓글 | 첨부 | 익명 | 비밀글 | 게시기간 | 상단 고정 | 조회자 목록 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Notice` | `BRD_NOTICE` | NOTICE | - | - | O | - | - | O | O | O |
| `Archive` | `BRD_ARCHIVE` | ARCHIVE | - | - | O | - | - | - | - | - |
| `Qna` | `BRD_QNA` | QNA | O | O | O | - | O | - | O | - |
| `EmpFree` | `BRD_EMP_FREE` | NORMAL | - | O | O | - | - | - | - | - |

글을 고치거나 상단 고정·공개 대상 회사를 정하는 "관리자"는 그 게시판 프로그램의 수정(UPDATE) 권한이 있는 사람입니다. 사내 게시판(`BRD_EMP_FREE`)은 수정 권한이 없으므로 모두 자기 글만 고칠 수 있습니다.


## 10. 기본 대시보드 위젯

홈 화면의 위젯은 `TB_WIDGET`에 등록하고(제목·칸 수·캐시·정렬·사용 여부는 위젯 관리 화면 `DSH_WIDGET_MGT`에서 바꾸며 위젯 추가와 데이터 SP는 개발 배포로 합니다)(`db/seed/FW/14_WIDGET.sql`), 제목은 `TB_WIDGET_LANG`에 언어별로 둡니다. 위젯의 데이터는 `USP_WIDGET_`로 시작하는 SP가 `LABEL, VALUE, TEXT, LINK, DT` 열로 돌려주고, 유형(`CARD` 숫자 카드, `LIST` 목록)이 표시 방법을 정합니다. `PROGRAM_ID`가 있으면 그 프로그램의 읽기(READ) 권한이 있는 사용자만 위젯을 보며, 데이터는 사용자별로 `CACHE_SEC`초 동안 서버 메모리에 둡니다. 시드는 이미 있는 위젯의 설정을 덮어쓰지 않습니다.

| 위젯 코드 | 유형 | 데이터 SP | 권한 프로그램 | 칸 수 | 캐시(초) |
| --- | --- | --- | --- | --- | --- |
| `NOTICE_RECENT` | LIST | `FW.USP_WIDGET_NOTICE_RECENT` | `BRD_NOTICE` | 8 | 60 |
| `NOTI_UNREAD` | CARD | `FW.USP_WIDGET_NOTI_UNREAD` | `MY_NOTIFICATION` | 4 | 30 |
