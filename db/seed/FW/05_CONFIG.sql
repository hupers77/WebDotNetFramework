-- 초기 데이터 (초기 데이터 정의서 6장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- SECRET 형식과 '(입력)' 항목은 값 없이 만들고, 설치 후 설정 관리 화면에서 입력한다.

-- 설정 키와 기본값
MERGE [FW].[TB_CONFIG] AS T
USING (VALUES
  (N'AUTH.MODE', N'BOTH', N'STRING', N'N', N'AUTH', N'FORM / AD / BOTH', N'N'),
  (N'AUTH.AD_AUTO_PROVISION', N'Y', N'BOOL', N'N', N'AUTH', N'AD 로그인 시 포털 계정 자동 생성', N'N'),
  (N'AUTH.PARTNER_MFA', N'Y', N'BOOL', N'N', N'AUTH', N'협력사 2단계 인증', N'N'),
  (N'AUTH.ADMIN_MFA', N'Y', N'BOOL', N'N', N'AUTH', N'관리자 역할 2단계 인증', N'N'),
  (N'AUTH.LOGIN_RATE_PER_MIN', N'10', N'INT', N'N', N'AUTH', N'IP당 분당 로그인 시도 한도', N'N'),
  (N'PWD.MIN_LEN_3TYPES', N'8', N'INT', N'N', N'PWD', N'3종류 조합 최소 길이', N'N'),
  (N'PWD.MIN_LEN_2TYPES', N'10', N'INT', N'N', N'PWD', N'2종류 조합 최소 길이', N'N'),
  (N'PWD.HISTORY_COUNT', N'3', N'INT', N'N', N'PWD', N'재사용 금지 이력 수', N'N'),
  (N'PWD.EXPIRE_DAYS', N'90', N'INT', N'N', N'PWD', N'변경 주기(일)', N'N'),
  (N'PWD.LOCK_COUNT', N'5', N'INT', N'N', N'PWD', N'실패 잠금 횟수', N'N'),
  (N'PWD.LOCK_MINUTES', N'30', N'INT', N'N', N'PWD', N'자동 해제 시간(분, 0 = 관리자 해제만)', N'N'),
  (N'SESSION.IDLE_MINUTES', N'30', N'INT', N'N', N'SESSION', N'비활동 만료(분)', N'N'),
  (N'SESSION.CONCURRENT_POLICY', N'KICK_OLD', N'STRING', N'N', N'SESSION', N'KICK_OLD / DENY_NEW / ALLOW', N'N'),
  (N'USER.DORMANT_DAYS', N'365', N'INT', N'N', N'USER', N'미접속 휴면 전환(일)', N'N'),
  (N'FILE.MAX_SIZE_MB', N'20', N'INT', N'N', N'FILE', N'파일 1개 최대 크기(MB)', N'N'),
  (N'FILE.MAX_COUNT', N'10', N'INT', N'N', N'FILE', N'첨부 묶음 하나의 최대 파일 수', N'N'),
  (N'UI.TAB_MODE', N'Y', N'BOOL', N'N', N'UI', N'멀티 탭 사용', N'N'),
  (N'UI.MAX_TABS', N'10', N'INT', N'N', N'UI', N'최대 탭 수', N'N'),
  (N'UI.PAGE_SIZE', N'50', N'INT', N'N', N'UI', N'그리드 기본 행 수', N'N'),
  (N'UI.DEFAULT_LANG', N'ko', N'STRING', N'N', N'UI', N'기본 언어', N'N'),
  (N'SITE.NAME', NULL, N'STRING', N'N', N'SITE', N'사이트명 (다국어 키 SITE.NAME을 우선 사용, 설치 시 입력)', N'N'),
  (N'SITE.THEME_COLOR', N'#1F5FBF', N'STRING', N'N', N'SITE', N'브랜드 색상', N'N'),
  (N'MAIL.ENABLED', N'N', N'BOOL', N'N', N'MAIL', N'실제 발송 여부 (개발계 N)', N'N'),
  (N'MAIL.SMTP_HOST', NULL, N'STRING', N'N', N'MAIL', N'SMTP 서버', N'N'),
  (N'MAIL.SMTP_PORT', N'587', N'INT', N'N', N'MAIL', N'SMTP 포트', N'N'),
  (N'MAIL.SMTP_PASSWORD', NULL, N'SECRET', N'Y', N'MAIL', N'SMTP 계정 비밀번호', N'N'),
  (N'MAIL.FROM', NULL, N'STRING', N'N', N'MAIL', N'보내는 주소', N'N'),
  (N'LOG.RETENTION_YEARS', N'10', N'INT', N'N', N'LOG', N'접속기록·감사 로그 보관 기간(년)', N'N'),
  (N'LOG.ERROR_RETENTION_DAYS', N'365', N'INT', N'N', N'LOG', N'오류 로그 보관 기간(일)', N'N'),
  (N'LOG.SLOW_SP_MS', N'1000', N'INT', N'N', N'LOG', N'느린 SP 기준(ms)', N'N'),
  (N'SP.COMMAND_TIMEOUT_SEC', N'30', N'INT', N'N', N'SP', N'SP 명령 타임아웃(초)', N'Y'),
  (N'PDF.MAX_CONCURRENCY', N'4', N'INT', N'N', N'PDF', N'동시 PDF 변환 수', N'Y'),
  (N'PDF.EDGE_PATH', N'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', N'STRING', N'N', N'PDF', N'Edge 실행 파일 경로', N'N'),
  (N'API.TOKEN_MINUTES', N'30', N'INT', N'N', N'API', N'외부 API 토큰 유효 시간(분)', N'N'),
  (N'API.DEFAULT_RATE_PER_MIN', N'600', N'INT', N'N', N'API', N'클라이언트 기본 호출 한도', N'N'),
  (N'AD.LDAP_SERVER', NULL, N'STRING', N'N', N'AD', N'LDAPS 서버 (636)', N'N'),
  (N'AD.BASE_DN', NULL, N'STRING', N'N', N'AD', N'동기화 대상 OU', N'N'),
  (N'AD.BIND_PASSWORD', NULL, N'SECRET', N'Y', N'AD', N'동기화 계정 비밀번호 (gMSA 사용 시 불필요)', N'N'),
  (N'APPROVAL.GATEWAY', N'NONE', N'STRING', N'N', N'APPROVAL', N'전자결재 연동 구현 이름 (업무 개발 시 지정)', N'Y'),
  (N'NOTIFY.POLL_SECONDS', N'60', N'INT', N'N', N'NOTIFY', N'알림 확인 주기(초)', N'N'),
  (N'SYS.MAINTENANCE_MODE', N'N', N'BOOL', N'N', N'SYS', N'Y면 관리자만 접속', N'N')
) AS S ([CONFIG_KEY], [CONFIG_VAL], [VAL_TYPE_CD], [ENC_YN], [CATEGORY_CD], [DESCRIPTION], [RESTART_YN])
   ON T.[CONFIG_KEY] = S.[CONFIG_KEY]
WHEN MATCHED THEN UPDATE SET T.[VAL_TYPE_CD] = S.[VAL_TYPE_CD], T.[ENC_YN] = S.[ENC_YN], T.[CATEGORY_CD] = S.[CATEGORY_CD], T.[DESCRIPTION] = S.[DESCRIPTION], T.[RESTART_YN] = S.[RESTART_YN]
WHEN NOT MATCHED THEN INSERT ([CONFIG_KEY], [CONFIG_VAL], [VAL_TYPE_CD], [ENC_YN], [CATEGORY_CD], [DESCRIPTION], [RESTART_YN]) VALUES (S.[CONFIG_KEY], S.[CONFIG_VAL], S.[VAL_TYPE_CD], S.[ENC_YN], S.[CATEGORY_CD], S.[DESCRIPTION], S.[RESTART_YN]);
GO
