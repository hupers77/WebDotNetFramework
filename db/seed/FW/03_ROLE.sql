-- 초기 데이터 (초기 데이터 정의서 3장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 기본 역할 (시스템 역할, 삭제 불가)
MERGE [FW].[TB_ROLE] AS T
USING (VALUES
  (N'SYS_ADMIN', N'시스템 관리자', N'ALL', N'비상 계정 전용. 모든 기능 허용(감사 로그 삭제 불가)', 1, N'Y'),
  (N'PORTAL_ADMIN', N'포털 운영 관리자', N'ALL', N'사용자·회사·역할·메뉴·코드·다국어·설정·게시판 관리', 2, N'Y'),
  (N'SECURITY_AUDITOR', N'보안 감사자', N'ALL', N'로그인·접근·감사·개인정보 접속기록 조회', 3, N'Y'),
  (N'EMP_USER', N'본사 일반 사용자', N'SELF', N'본사 임직원 전체 (AD 동기화 시 자동 부여)', 4, N'Y'),
  (N'PARTNER_ADMIN', N'협력사 관리자', N'COMPANY', N'자기 회사 사용자 신청·잠금 해제, 소속 회사 데이터 조회', 5, N'Y'),
  (N'PARTNER_USER', N'협력사 사용자', N'SELF', N'협력사 직원 전체 (승인 시 자동 부여)', 6, N'Y'),
  (N'API_CLIENT', N'외부 연동', N'SELF', N'허용된 API만 호출', 7, N'Y')
) AS S ([ROLE_ID], [ROLE_NM], [DATA_SCOPE_CD], [DESCRIPTION], [SORT_NO], [SYSTEM_YN])
   ON T.[ROLE_ID] = S.[ROLE_ID]
WHEN MATCHED THEN UPDATE SET T.[SYSTEM_YN] = S.[SYSTEM_YN]
WHEN NOT MATCHED THEN INSERT ([ROLE_ID], [ROLE_NM], [DATA_SCOPE_CD], [DESCRIPTION], [SORT_NO], [SYSTEM_YN]) VALUES (S.[ROLE_ID], S.[ROLE_NM], S.[DATA_SCOPE_CD], S.[DESCRIPTION], S.[SORT_NO], S.[SYSTEM_YN]);
GO
