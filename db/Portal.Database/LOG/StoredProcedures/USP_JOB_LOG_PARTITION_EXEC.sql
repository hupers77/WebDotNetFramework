-- 배치 작업 JOB_LOG_PARTITION: 로그 테이블 월 파티션(PF_LOG_MONTH, 경계는 UTC 월 1일)을 관리한다(설계서 7장).
--   1) 경계 추가: 이번 달 기준 2개월 뒤 1일까지 경계를 만든다. 다음 달 로그가 들어갈 파티션을 미리, 비어 있을 때 나눠 데이터 이동이 없다.
--   2) 보관 기간 정리: 끝이 LOG.RETENTION_YEARS(기본 10년) 전보다 이른 월 파티션을 모든 로그 테이블에서 비우고(TRUNCATE),
--      오류 로그(TB_ERROR_LOG)는 LOG.ERROR_RETENTION_DAYS(기본 365일) 기준으로 비운다. 비운 파티션 경계는 합쳐(MERGE) 개수를 유지한다.
--      설정이 1보다 작으면 지우지 않는다. 모든 파티션은 PRIMARY 파일그룹에 둔다(읽기 전용 파일그룹으로 옮기지 않는다, 2026-10-23 결정).
--   앱 계정은 로그를 지우거나 DDL을 실행할 수 없으므로 소유자 권한으로 실행한다(EXECUTE AS OWNER). 로그 행을 하나씩 지우지 않는다.
-- 결과: PROCESSED_CNT (추가한 경계 + 비운 파티션 + 합친 경계 수)
CREATE PROCEDURE [LOG].[USP_JOB_LOG_PARTITION_EXEC]
  @P_USER_ID       VARCHAR(30) = NULL,
  @P_LANG_CD       VARCHAR(5),
  @P_CLIENT_IP     VARCHAR(45) = NULL
WITH EXECUTE AS OWNER
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  DECLARE @NOW DATETIME2(3) = SYSUTCDATETIME(), @CNT INT = 0, @SQL NVARCHAR(MAX);
  DECLARE @THIS_MONTH DATETIME2(3) = DATEFROMPARTS(YEAR(@NOW), MONTH(@NOW), 1);
  DECLARE @KEEP_YEARS INT = ISNULL((SELECT TRY_CAST([CONFIG_VAL] AS INT) FROM [FW].[TB_CONFIG] WHERE [CONFIG_KEY] = 'LOG.RETENTION_YEARS'), 10);
  DECLARE @ERROR_DAYS INT = ISNULL((SELECT TRY_CAST([CONFIG_VAL] AS INT) FROM [FW].[TB_CONFIG] WHERE [CONFIG_KEY] = 'LOG.ERROR_RETENTION_DAYS'), 365);

  DECLARE @FUNC_ID INT = (SELECT [function_id] FROM sys.partition_functions WHERE [name] = 'PF_LOG_MONTH');
  DECLARE @NEXT DATETIME2(3), @LAST DATETIME2(3);

  -- 1) 경계 추가
  SET @LAST = (SELECT MAX(CAST([value] AS DATETIME2(3))) FROM sys.partition_range_values WHERE [function_id] = @FUNC_ID);
  WHILE @LAST < DATEADD(MONTH, 2, @THIS_MONTH)
  BEGIN
    SET @NEXT = DATEADD(MONTH, 1, @LAST);
    ALTER PARTITION SCHEME [PS_LOG_MONTH] NEXT USED [PRIMARY];
    ALTER PARTITION FUNCTION [PF_LOG_MONTH]() SPLIT RANGE (@NEXT);
    SET @LAST = @NEXT;
    SET @CNT += 1;
  END

  -- 2) 보관 기간 정리: 파티션 번호 N의 범위는 [경계 N-1, 경계 N). 끝(경계 N)이 기준일 이하인 파티션을 비운다.
  DECLARE @PARTS TABLE ([TABLE_NM] SYSNAME, [PARTITION_NO] INT, PRIMARY KEY ([TABLE_NM], [PARTITION_NO]));

  INSERT INTO @PARTS ([TABLE_NM], [PARTITION_NO])
  SELECT T.[name], P.[partition_number]
    FROM sys.tables T
    JOIN sys.indexes I ON I.[object_id] = T.[object_id] AND I.[index_id] IN (0, 1)
    JOIN sys.partition_schemes S ON S.[data_space_id] = I.[data_space_id] AND S.[function_id] = @FUNC_ID
    JOIN sys.partitions P ON P.[object_id] = T.[object_id] AND P.[index_id] = I.[index_id]
    JOIN sys.partition_range_values R ON R.[function_id] = @FUNC_ID AND R.[boundary_id] = P.[partition_number]
   WHERE T.[schema_id] = SCHEMA_ID('LOG')
     AND P.[rows] > 0
     AND CAST(R.[value] AS DATETIME2(3)) <= CASE WHEN T.[name] = 'TB_ERROR_LOG'
                                                 THEN IIF(@ERROR_DAYS >= 1, DATEADD(DAY, -@ERROR_DAYS, @NOW), NULL)
                                                 ELSE IIF(@KEEP_YEARS >= 1, DATEADD(YEAR, -@KEEP_YEARS, @NOW), NULL) END;

  DECLARE @TABLE_NM SYSNAME, @PARTITION_NO INT;
  DECLARE PARTS CURSOR LOCAL FAST_FORWARD FOR SELECT [TABLE_NM], [PARTITION_NO] FROM @PARTS ORDER BY [TABLE_NM], [PARTITION_NO];
  OPEN PARTS;
  FETCH NEXT FROM PARTS INTO @TABLE_NM, @PARTITION_NO;
  WHILE @@FETCH_STATUS = 0
  BEGIN
    SET @SQL = N'TRUNCATE TABLE [LOG].' + QUOTENAME(@TABLE_NM) + N' WITH (PARTITIONS (' + CAST(@PARTITION_NO AS NVARCHAR(10)) + N'));';
    EXEC sys.sp_executesql @SQL;
    SET @CNT += 1;
    FETCH NEXT FROM PARTS INTO @TABLE_NM, @PARTITION_NO;
  END
  CLOSE PARTS;
  DEALLOCATE PARTS;

  -- 맨 앞 두 파티션이 모든 테이블에서 비어 있고 둘째 파티션의 끝도 보관 기준일 이하이면 첫 경계를 합친다(가장 오래된 빈 달 하나로 줄인다).
  WHILE @KEEP_YEARS >= 1
    AND (SELECT COUNT(*) FROM sys.partition_range_values WHERE [function_id] = @FUNC_ID) > 1
    AND (SELECT CAST([value] AS DATETIME2(3)) FROM sys.partition_range_values WHERE [function_id] = @FUNC_ID AND [boundary_id] = 2) <= DATEADD(YEAR, -@KEEP_YEARS, @NOW)
    AND NOT EXISTS (SELECT 1
                      FROM sys.tables T
                      JOIN sys.indexes I ON I.[object_id] = T.[object_id] AND I.[index_id] IN (0, 1)
                      JOIN sys.partition_schemes S ON S.[data_space_id] = I.[data_space_id] AND S.[function_id] = @FUNC_ID
                      JOIN sys.partitions P ON P.[object_id] = T.[object_id] AND P.[index_id] = I.[index_id]
                     WHERE P.[partition_number] IN (1, 2) AND P.[rows] > 0)
  BEGIN
    SET @NEXT = (SELECT CAST([value] AS DATETIME2(3)) FROM sys.partition_range_values WHERE [function_id] = @FUNC_ID AND [boundary_id] = 1);
    ALTER PARTITION FUNCTION [PF_LOG_MONTH]() MERGE RANGE (@NEXT);
    SET @CNT += 1;
  END

  SELECT @CNT AS [PROCESSED_CNT];
END
