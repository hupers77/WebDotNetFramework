-- 초기 데이터 (초기 데이터 정의서 7장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 주기(CRON_EXPR)는 분 시 일 월 요일, Portal.Worker 시간대(기본 한국 시간) 기준. 다음 실행 시각은 Worker가 채운다.

-- 기본 배치 작업
MERGE [FW].[TB_JOB] AS T
USING (VALUES
  (N'JOB_MAIL_SEND', N'메일 발송', N'CLASS', N'MailSend', N'* * * * *', 10, N'메일 대기열 발송, 실패 시 3회 재시도'),
  (N'JOB_SESSION_CLEANUP', N'세션 정리', N'SP', N'FW.USP_JOB_SESSION_CLEANUP_EXEC', N'*/10 * * * *', 10, N'시간이 지난 세션 종료, 오래된 세션 행 삭제'),
  (N'JOB_TEMP_CLEANUP', N'첨부 정리', N'CLASS', N'FileCleanup', N'0 5 * * *', 60, N'확정되지 않은 첨부와 삭제한 지 오래된 첨부를 저장소에서 삭제'),
  (N'JOB_DATA_CLEANUP', N'보관 기간 지난 데이터 정리', N'SP', N'FW.USP_JOB_DATA_CLEANUP_EXEC', N'10 5 * * *', 60, N'처리한 가입 신청 이메일, 발송 끝난 메일 대기열, 오래된 작업 이력·포털 알림 삭제')
) AS S ([JOB_ID], [JOB_NM], [JOB_TYPE_CD], [JOB_TARGET], [CRON_EXPR], [TIMEOUT_MIN], [DESCRIPTION])
   ON T.[JOB_ID] = S.[JOB_ID]
WHEN NOT MATCHED THEN INSERT ([JOB_ID], [JOB_NM], [JOB_TYPE_CD], [JOB_TARGET], [CRON_EXPR], [TIMEOUT_MIN], [DESCRIPTION]) VALUES (S.[JOB_ID], S.[JOB_NM], S.[JOB_TYPE_CD], S.[JOB_TARGET], S.[CRON_EXPR], S.[TIMEOUT_MIN], S.[DESCRIPTION]);
GO
