-- 내 요청을 지운다: 목록에서 바로 빠지고(EXPIRE_DT = 지금) 파일과 행은 정리가 지운다. 만들고 있는 요청도 지울 수 있다(끝나도 알림 없이 정리된다).
-- 프로그램: RPT_RUNS (DELETE)
-- 결과: AFFECTED_CNT
CREATE PROCEDURE [FW].[USP_REPORT_RUN_DEL]
  @P_RUN_ID       BIGINT,
  @P_USER_ID      VARCHAR(30),
  @P_LANG_CD      VARCHAR(5),
  @P_CLIENT_IP    VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  UPDATE [FW].[TB_REPORT_RUN]
     SET [EXPIRE_DT] = DATEADD(SECOND, -1, SYSUTCDATETIME()) -- DATETIME2(0)는 반올림하므로 1초 빼야 지운 직후 목록에서 확실히 빠진다
   WHERE [RUN_ID] = @P_RUN_ID
     AND [REQ_USER_ID] = @P_USER_ID
     AND ([EXPIRE_DT] IS NULL OR [EXPIRE_DT] > SYSUTCDATETIME());

  SELECT @@ROWCOUNT AS [AFFECTED_CNT];
END
