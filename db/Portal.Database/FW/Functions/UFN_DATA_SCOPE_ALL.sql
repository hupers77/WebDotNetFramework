-- 데이터 범위가 '전체'인지 (UFN_DATA_SCOPE가 모든 회사를 부서·사용자 조건 없이 돌려주는지). 로그 조회처럼 회사에 속하지 않는 행
-- (없는 ID로 시도한 로그인 등)까지 보여 줄지 정할 때 쓴다. 반환: ALL_YN (Y/N), 항상 1행.
CREATE FUNCTION [FW].[UFN_DATA_SCOPE_ALL] (@P_USER_ID VARCHAR(30), @P_PROGRAM_ID VARCHAR(30))
RETURNS TABLE
AS
RETURN
  SELECT IIF(EXISTS (SELECT 1 FROM [FW].[UFN_DATA_SCOPE](@P_USER_ID, @P_PROGRAM_ID))
             AND NOT EXISTS (SELECT 1 FROM [FW].[TB_COMPANY] C
                              WHERE NOT EXISTS (SELECT 1 FROM [FW].[UFN_DATA_SCOPE](@P_USER_ID, @P_PROGRAM_ID) S
                                                 WHERE S.[COMPANY_CD] = C.[COMPANY_CD] AND S.[DEPT_ID] IS NULL AND S.[USER_ID] IS NULL)),
             'Y', 'N') AS [ALL_YN];
