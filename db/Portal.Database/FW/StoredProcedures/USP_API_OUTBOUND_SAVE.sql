-- 송신 대상 일괄 저장 (추가·수정·삭제). 프로그램: SYS_API_OUTBOUND_MGT
-- @P_ROWS_JSON: [{rowStat(I/U/D), targetKey, targetNm, baseUrl, authTypeCd, authHeader, authUser, secretMode('KEEP'/'SET'), secretEnc, timeoutSec, retryCount, allowPrivateYn, useYn, rowVer(16진수; U·D 필수)}]
-- secretEnc는 웹서버가 암호화한 값이다. KEEP이면 기존 비밀값을 그대로 둔다. 인증 방식이 NONE이면 비밀값·사용자·헤더를 지운다.
-- 감사 로그에는 비밀값을 넣지 않는다(설정 여부만). 결과: AFFECTED_CNT
CREATE PROCEDURE [FW].[USP_API_OUTBOUND_SAVE]
  @P_ROWS_JSON  NVARCHAR(MAX),
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  IF ISJSON(@P_ROWS_JSON) <> 1
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  DECLARE @R TABLE (
    [ROW_STAT] CHAR(1), [TARGET_KEY] VARCHAR(30) PRIMARY KEY, [TARGET_NM] NVARCHAR(100), [BASE_URL] VARCHAR(500), [AUTH_TYPE_CD] VARCHAR(10),
    [AUTH_HEADER] VARCHAR(50), [AUTH_USER] NVARCHAR(100), [SECRET_MODE] VARCHAR(4), [SECRET_ENC] NVARCHAR(2000),
    [TIMEOUT_SEC] INT, [RETRY_COUNT] INT, [ALLOW_PRIVATE_YN] CHAR(1), [USE_YN] CHAR(1), [ROW_VER] BINARY(8));
  INSERT INTO @R
  SELECT [rowStat], [targetKey], [targetNm], [baseUrl], [authTypeCd], NULLIF([authHeader], ''), NULLIF([authUser], ''), ISNULL([secretMode], 'KEEP'), NULLIF([secretEnc], ''),
         [timeoutSec], [retryCount], [allowPrivateYn], [useYn], CONVERT(BINARY(8), [rowVer], 2)
    FROM OPENJSON(@P_ROWS_JSON) WITH (
      [rowStat] CHAR(1), [targetKey] VARCHAR(30), [targetNm] NVARCHAR(100), [baseUrl] VARCHAR(500), [authTypeCd] VARCHAR(10),
      [authHeader] VARCHAR(50), [authUser] NVARCHAR(100), [secretMode] VARCHAR(4), [secretEnc] NVARCHAR(2000),
      [timeoutSec] INT, [retryCount] INT, [allowPrivateYn] CHAR(1), [useYn] CHAR(1), [rowVer] VARCHAR(16));

  IF NOT EXISTS (SELECT 1 FROM @R)
    THROW 50005, 'MSG.NO_CHANGES', 1;
  IF EXISTS (SELECT 1 FROM @R WHERE [ROW_STAT] NOT IN ('I', 'U', 'D') OR [TARGET_KEY] IS NULL OR [TARGET_KEY] LIKE '%[^A-Z0-9_]%'
                                   OR ([ROW_STAT] IN ('I', 'U') AND (ISNULL([TARGET_NM], N'') = N'' OR ISNULL([BASE_URL], '') = ''
                                        OR [AUTH_TYPE_CD] NOT IN ('NONE', 'BASIC', 'BEARER', 'API_KEY') OR [AUTH_TYPE_CD] IS NULL
                                        OR [TIMEOUT_SEC] NOT BETWEEN 1 AND 300 OR [RETRY_COUNT] NOT BETWEEN 0 AND 5
                                        OR [ALLOW_PRIVATE_YN] NOT IN ('Y', 'N') OR [ALLOW_PRIVATE_YN] IS NULL OR [USE_YN] NOT IN ('Y', 'N') OR [USE_YN] IS NULL
                                        OR [SECRET_MODE] NOT IN ('KEEP', 'SET')
                                        OR ([SECRET_MODE] = 'SET' AND [SECRET_ENC] IS NULL)
                                        OR ([AUTH_TYPE_CD] = 'API_KEY' AND [AUTH_HEADER] IS NULL)
                                        OR ([AUTH_TYPE_CD] = 'BASIC' AND [AUTH_USER] IS NULL)
                                        OR (BASE_URL NOT LIKE 'https://%' AND NOT (BASE_URL LIKE 'http://%' AND [ALLOW_PRIVATE_YN] = 'Y'))))
                                   OR ([ROW_STAT] IN ('U', 'D') AND [ROW_VER] IS NULL))
    THROW 50005, 'MSG.INVALID_INPUT', 1;
  IF EXISTS (SELECT 1 FROM @R R JOIN [FW].[TB_API_OUTBOUND] O ON O.[TARGET_KEY] = R.[TARGET_KEY] WHERE R.[ROW_STAT] = 'I')
    THROW 50001, 'MSG.DUP_API_OUTBOUND', 1;
  IF EXISTS (SELECT 1 FROM @R R LEFT JOIN [FW].[TB_API_OUTBOUND] O ON O.[TARGET_KEY] = R.[TARGET_KEY] WHERE R.[ROW_STAT] IN ('U', 'D') AND O.[TARGET_KEY] IS NULL)
    THROW 50004, 'MSG.NOT_FOUND', 1;
  -- 새 대상이나 인증 방식 변경으로 비밀값이 필요한데 없는 경우
  IF EXISTS (SELECT 1 FROM @R R LEFT JOIN [FW].[TB_API_OUTBOUND] O ON O.[TARGET_KEY] = R.[TARGET_KEY]
              WHERE R.[ROW_STAT] IN ('I', 'U') AND R.[AUTH_TYPE_CD] <> 'NONE' AND R.[SECRET_MODE] = 'KEEP' AND O.[AUTH_SECRET_ENC] IS NULL)
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  DECLARE @CNT INT = 0, @N INT;
  DECLARE @BEFORE NVARCHAR(MAX), @AFTER NVARCHAR(MAX);

  BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM @R R JOIN [FW].[TB_API_OUTBOUND] O WITH (UPDLOCK) ON O.[TARGET_KEY] = R.[TARGET_KEY]
                WHERE R.[ROW_STAT] IN ('U', 'D') AND O.[ROW_VER] <> R.[ROW_VER])
      THROW 50002, 'MSG.CONCURRENCY', 1;

    SET @BEFORE = (SELECT O.[TARGET_KEY], O.[TARGET_NM], O.[BASE_URL], O.[AUTH_TYPE_CD], O.[AUTH_HEADER], O.[AUTH_USER],
                          CASE WHEN O.[AUTH_SECRET_ENC] IS NULL THEN 'N' ELSE 'Y' END AS [SECRET_YN],
                          O.[TIMEOUT_SEC], O.[RETRY_COUNT], O.[ALLOW_PRIVATE_YN], O.[USE_YN]
                     FROM [FW].[TB_API_OUTBOUND] O JOIN @R R ON R.[TARGET_KEY] = O.[TARGET_KEY] FOR JSON PATH);

    INSERT INTO [FW].[TB_API_OUTBOUND] ([TARGET_KEY], [TARGET_NM], [BASE_URL], [AUTH_TYPE_CD], [AUTH_HEADER], [AUTH_USER], [AUTH_SECRET_ENC],
                                         [TIMEOUT_SEC], [RETRY_COUNT], [ALLOW_PRIVATE_YN], [USE_YN], [REG_ID], [UPD_ID])
    SELECT [TARGET_KEY], [TARGET_NM], [BASE_URL], [AUTH_TYPE_CD],
           CASE WHEN [AUTH_TYPE_CD] = 'API_KEY' THEN [AUTH_HEADER] END,
           CASE WHEN [AUTH_TYPE_CD] = 'BASIC' THEN [AUTH_USER] END,
           CASE WHEN [AUTH_TYPE_CD] <> 'NONE' THEN [SECRET_ENC] END,
           [TIMEOUT_SEC], [RETRY_COUNT], [ALLOW_PRIVATE_YN], [USE_YN], @P_USER_ID, @P_USER_ID
      FROM @R WHERE [ROW_STAT] = 'I';
    SET @N = @@ROWCOUNT; SET @CNT += @N;

    UPDATE O
       SET O.[TARGET_NM] = R.[TARGET_NM], O.[BASE_URL] = R.[BASE_URL], O.[AUTH_TYPE_CD] = R.[AUTH_TYPE_CD],
           O.[AUTH_HEADER] = CASE WHEN R.[AUTH_TYPE_CD] = 'API_KEY' THEN R.[AUTH_HEADER] END,
           O.[AUTH_USER] = CASE WHEN R.[AUTH_TYPE_CD] = 'BASIC' THEN R.[AUTH_USER] END,
           O.[AUTH_SECRET_ENC] = CASE WHEN R.[AUTH_TYPE_CD] = 'NONE' THEN NULL WHEN R.[SECRET_MODE] = 'SET' THEN R.[SECRET_ENC] ELSE O.[AUTH_SECRET_ENC] END,
           O.[TIMEOUT_SEC] = R.[TIMEOUT_SEC], O.[RETRY_COUNT] = R.[RETRY_COUNT], O.[ALLOW_PRIVATE_YN] = R.[ALLOW_PRIVATE_YN], O.[USE_YN] = R.[USE_YN],
           O.[UPD_ID] = @P_USER_ID, O.[UPD_DT] = SYSUTCDATETIME()
      FROM [FW].[TB_API_OUTBOUND] O JOIN @R R ON R.[TARGET_KEY] = O.[TARGET_KEY]
     WHERE R.[ROW_STAT] = 'U';
    SET @N = @@ROWCOUNT; SET @CNT += @N;

    DELETE O
      FROM [FW].[TB_API_OUTBOUND] O JOIN @R R ON R.[TARGET_KEY] = O.[TARGET_KEY]
     WHERE R.[ROW_STAT] = 'D';
    SET @N = @@ROWCOUNT; SET @CNT += @N;

    SET @AFTER = (SELECT R.[ROW_STAT], R.[TARGET_KEY], R.[TARGET_NM], R.[BASE_URL], R.[AUTH_TYPE_CD], R.[AUTH_HEADER], R.[AUTH_USER], R.[SECRET_MODE],
                         R.[TIMEOUT_SEC], R.[RETRY_COUNT], R.[ALLOW_PRIVATE_YN], R.[USE_YN]
                    FROM @R R FOR JSON PATH);
    EXEC [LOG].[USP_AUDIT_WRITE]
      @P_TABLE_NM = 'FW.TB_API_OUTBOUND', @P_ACTION_CD = 'U', @P_ROW_KEY = N'API_OUTBOUND',
      @P_BEFORE_JSON = @BEFORE, @P_AFTER_JSON = @AFTER,
      @P_USER_ID = @P_USER_ID, @P_LANG_CD = @P_LANG_CD, @P_CLIENT_IP = @P_CLIENT_IP;

    COMMIT;

    SELECT @CNT AS [AFFECTED_CNT];
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT > 0
      ROLLBACK;
    THROW;
  END CATCH
END
