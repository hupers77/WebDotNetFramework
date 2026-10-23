-- 외부 API 요청/응답 기록 일괄 적재 (수신 IN·송신 OUT). 웹서버의 API 기록 큐가 호출한다.
-- @P_ROWS_JSON: [{logDt, directionCd, clientId, httpMethod, url, statusCode, elapsedMs, requestBody, responseBody, traceId}]
-- 본문은 웹서버가 민감 필드를 가린 뒤 보낸다. 결과: AFFECTED_CNT
CREATE PROCEDURE [LOG].[USP_API_LOG_WRITE]
  @P_ROWS_JSON  NVARCHAR(MAX),
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  IF ISJSON(@P_ROWS_JSON) <> 1
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  INSERT INTO [LOG].[TB_API_LOG]
    ([LOG_DT], [DIRECTION_CD], [CLIENT_ID], [HTTP_METHOD], [URL], [STATUS_CODE], [ELAPSED_MS], [REQUEST_BODY], [RESPONSE_BODY], [TRACE_ID])
  SELECT ISNULL(TRY_CONVERT(DATETIME2(3), [logDt], 126), SYSUTCDATETIME()), [directionCd], [clientId], [httpMethod], [url],
         [statusCode], [elapsedMs], [requestBody], [responseBody], [traceId]
    FROM OPENJSON(@P_ROWS_JSON) WITH (
      [logDt] VARCHAR(33), [directionCd] VARCHAR(3), [clientId] VARCHAR(50), [httpMethod] VARCHAR(10), [url] VARCHAR(1000),
      [statusCode] SMALLINT, [elapsedMs] INT, [requestBody] NVARCHAR(MAX), [responseBody] NVARCHAR(MAX), [traceId] VARCHAR(64))
   WHERE [directionCd] IN ('IN', 'OUT') AND [httpMethod] IS NOT NULL AND [url] IS NOT NULL;

  SELECT @@ROWCOUNT AS [AFFECTED_CNT];
END
