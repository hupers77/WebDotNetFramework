-- 백그라운드 생성 서비스가 다음 요청 하나를 꺼내 RUNNING으로 바꾼다(여러 서비스가 동시에 불러도 같은 요청을 받지 않는다).
-- 만들다가 멈춘 요청(RUNNING으로 30분 넘음, 서비스가 꺼진 경우)은 먼저 FAILED(RPT.RUN_INTERRUPTED)로 끝낸다. 지운 요청은 꺼내지 않는다.
-- 사용자 없이 서비스가 부른다(@P_USER_ID는 비어 있다). 결과: 꺼낼 요청이 없으면 0행
CREATE PROCEDURE [FW].[USP_REPORT_RUN_CLAIM_EXEC]
  @P_USER_ID      VARCHAR(30) = NULL,
  @P_LANG_CD      VARCHAR(5),
  @P_CLIENT_IP    VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  DECLARE @NOW DATETIME2(0) = SYSUTCDATETIME();

  UPDATE [FW].[TB_REPORT_RUN]
     SET [STATUS_CD] = 'FAILED', [ERR_KEY] = 'RPT.RUN_INTERRUPTED', [END_DT] = @NOW, [EXPIRE_DT] = ISNULL([EXPIRE_DT], DATEADD(DAY, 7, @NOW))
   WHERE [STATUS_CD] = 'RUNNING' AND [START_DT] < DATEADD(MINUTE, -30, @NOW);

  WITH NEXT_RUN AS (
    SELECT TOP (1) *
      FROM [FW].[TB_REPORT_RUN] WITH (UPDLOCK, READPAST, ROWLOCK)
     WHERE [STATUS_CD] = 'QUEUED' AND ([EXPIRE_DT] IS NULL OR [EXPIRE_DT] > @NOW)
     ORDER BY [RUN_ID]
  )
  UPDATE NEXT_RUN
     SET [STATUS_CD] = 'RUNNING', [START_DT] = @NOW
  OUTPUT inserted.[RUN_ID], inserted.[REPORT_ID], inserted.[FORMAT_CD], inserted.[PARAMS_JSON], inserted.[REASON],
         inserted.[REQ_USER_ID], inserted.[REQ_COMPANY_CD], inserted.[REQ_USER_TYPE_CD], inserted.[REQ_LANG_CD];
END
