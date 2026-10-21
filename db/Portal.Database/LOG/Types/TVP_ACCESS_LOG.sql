-- 접근 기록 일괄 적재 (LOG.USP_ACCESS_LOG_WRITE). 웹서버가 응답 후 큐에 모았다가 한 번에 보낸다.
-- 컬럼 순서가 C# AccessLogEntry 속성 순서와 같아야 한다.
CREATE TYPE [LOG].[TVP_ACCESS_LOG] AS TABLE (
  [SEQ_NO]         INT            NOT NULL,             -- 배치 안 순서 (해시 체인 순서)
  [LOG_DT]         DATETIME2(3)   NOT NULL,             -- 요청 시각 (UTC)
  [USER_ID]        VARCHAR(30)    NULL,
  [PROGRAM_ID]     VARCHAR(30)    NULL,
  [FUNC_CD]        VARCHAR(20)    NULL,
  [HTTP_METHOD]    VARCHAR(10)    NOT NULL,
  [REQUEST_PATH]   VARCHAR(400)   NOT NULL,
  [STATUS_CODE]    SMALLINT       NOT NULL,
  [ELAPSED_MS]     INT            NOT NULL,
  [CLIENT_IP]      VARCHAR(45)    NULL,
  [TRACE_ID]       VARCHAR(64)    NULL,
  PRIMARY KEY ([SEQ_NO])
);
