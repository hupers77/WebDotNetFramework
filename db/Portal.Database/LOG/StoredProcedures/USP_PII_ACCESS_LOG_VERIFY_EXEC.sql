-- 개인정보 접속기록 해시 체인 무결성 점검 (월별 배치 JOB_LOG_INTEGRITY, 보안 감사 화면). 프로그램: LOG_PII_INQ
-- 결과: 점검 건수와 처음 깨진 LOG_ID(없으면 NULL).
-- 기간 [@P_FROM_DT, @P_TO_DT)의 행을 점검하되, 체인의 앞 행을 맞춰 보려고 앞뒤 30분을 더 읽는다(접근 기록은 요청 시각이 적재 순서와 조금 어긋날 수 있다).
-- 기간의 첫 행도 바로 앞 기간의 마지막 행과 이어지는지 본다. 그래서 기간을 잘라 여러 번 점검해도(배치는 6시간씩) 경계의 삭제를 놓치지 않는다.
CREATE PROCEDURE [LOG].[USP_PII_ACCESS_LOG_VERIFY_EXEC]
  @P_FROM_DT    DATETIME2(3),
  @P_TO_DT      DATETIME2(3),
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  DECLARE @X_FROM DATETIME2(3) = DATEADD(MINUTE, -30, @P_FROM_DT), @X_TO DATETIME2(3) = DATEADD(MINUTE, 30, @P_TO_DT);

  -- 읽는 범위 바로 앞의 체인 행 (그 앞 30분 중 LOG_ID가 가장 큰 행, 없으면 그 전의 마지막 행)
  DECLARE @SEED BINARY(32) = (SELECT TOP (1) A.[ROW_HASH] FROM [LOG].[TB_PII_ACCESS_LOG] A
                                WHERE A.[LOG_DT] >= DATEADD(MINUTE, -30, @X_FROM) AND A.[LOG_DT] < @X_FROM ORDER BY A.[LOG_ID] DESC);
  IF @SEED IS NULL
    SET @SEED = (SELECT TOP (1) A.[ROW_HASH] FROM [LOG].[TB_PII_ACCESS_LOG] A WHERE A.[LOG_DT] < @X_FROM ORDER BY A.[LOG_DT] DESC, A.[LOG_ID] DESC);

  WITH [CHECKED] AS (
    SELECT A.[LOG_ID], A.[LOG_DT], A.[PREV_HASH], A.[ROW_HASH],
           LAG(A.[ROW_HASH], 1, @SEED) OVER (ORDER BY A.[LOG_ID]) AS [EXPECTED_PREV],
           H.[ROW_HASH] AS [RECALC_HASH]
      FROM [LOG].[TB_PII_ACCESS_LOG] A
     CROSS APPLY [LOG].[UFN_PII_HASH](A.[PREV_HASH], A.[LOG_DT], A.[USER_ID], A.[PROGRAM_ID], A.[ACTION_CD], A.[TARGET_DESC], A.[REASON], A.[CLIENT_IP]) H
     WHERE A.[LOG_DT] >= @X_FROM
       AND A.[LOG_DT] < @X_TO
  )
  SELECT COUNT(*) AS [CHECKED_CNT],
         MIN(CASE WHEN [ROW_HASH] <> [RECALC_HASH]
                    OR ([EXPECTED_PREV] IS NOT NULL AND [PREV_HASH] <> [EXPECTED_PREV])
                  THEN [LOG_ID] END) AS [BROKEN_LOG_ID]
    FROM [CHECKED]
   WHERE [LOG_DT] >= @P_FROM_DT
     AND [LOG_DT] < @P_TO_DT;
END
