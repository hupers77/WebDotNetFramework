-- 사용자 예외 권한의 버전 값 (동시성 확인용). 예외 권한 행 전부(프로그램:기능:유형:종료일)를 이어 붙인 해시.
-- 예외 권한 화면이 조회 때 받은 값과 저장 때 다시 계산한 값이 다르면 다른 사람이 먼저 고친 것이다.
CREATE FUNCTION [FW].[UFN_USER_AUTH_VERSION] (@P_TARGET_USER_ID VARCHAR(30))
RETURNS TABLE
AS
RETURN
  SELECT CAST(HASHBYTES('SHA2_256',
           ISNULL((SELECT STRING_AGG(CAST(F.[PROGRAM_ID] + ':' + F.[FUNC_CD] + ':' + F.[GRANT_TYPE] + ':' + CONVERT(CHAR(10), F.[END_DT], 23) AS NVARCHAR(MAX)), ',')
                            WITHIN GROUP (ORDER BY F.[PROGRAM_ID], F.[FUNC_CD])
                     FROM [FW].[TB_USER_PROGRAM_FUNC] F WHERE F.[USER_ID] = @P_TARGET_USER_ID), N'')) AS BINARY(32)) AS [VERSION];
