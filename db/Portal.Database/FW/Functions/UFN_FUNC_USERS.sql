-- 어떤 기능 권한을 가진 사용자 목록 (승인 담당자에게 알림을 보낼 때 등). 평가 규칙은 PermissionEvaluator와 같다(설계서 6장):
-- 유효 역할이 하나 이상 있고, SYS_ADMIN 역할이거나, (역할 허용 또는 사용자 GRANT)이면서 사용자 DENY가 없을 때.
-- 사용 중인 프로그램, 사용 중이고 잠기지 않은 계정만. 사용자마다 역할을 계산하므로 드물게 일어나는 일(가입 신청 등)에만 쓴다.
CREATE FUNCTION [FW].[UFN_FUNC_USERS] (@P_PROGRAM_ID VARCHAR(30), @P_FUNC_CD VARCHAR(20))
RETURNS TABLE
AS
RETURN
  SELECT U.[USER_ID]
    FROM [FW].[TB_USER] U
    JOIN [FW].[TB_PROGRAM] P ON P.[PROGRAM_ID] = @P_PROGRAM_ID AND P.[USE_YN] = 'Y'
   CROSS APPLY (SELECT MAX(CASE WHEN R.[ROLE_ID] = 'SYS_ADMIN' THEN 1 ELSE 0 END) AS [IS_SYS_ADMIN],
                       MAX(CASE WHEN RF.[ROLE_ID] IS NOT NULL THEN 1 ELSE 0 END) AS [ROLE_ALLOWED],
                       COUNT(*) AS [ROLE_CNT]
                  FROM [FW].[UFN_USER_ROLES](U.[USER_ID]) R
                  LEFT JOIN [FW].[TB_ROLE_PROGRAM_FUNC] RF
                    ON RF.[ROLE_ID] = R.[ROLE_ID] AND RF.[PROGRAM_ID] = @P_PROGRAM_ID AND RF.[FUNC_CD] = @P_FUNC_CD) A
   WHERE U.[USER_STAT_CD] = 'ACTIVE'
     AND U.[LOCK_YN] = 'N'
     AND ISNULL(U.[ACCOUNT_END_DT], '9999-12-31') >= CAST(GETDATE() AS DATE)
     AND A.[ROLE_CNT] > 0
     AND (A.[IS_SYS_ADMIN] = 1
          OR ((A.[ROLE_ALLOWED] = 1
               OR EXISTS (SELECT 1 FROM [FW].[TB_USER_PROGRAM_FUNC] G
                           WHERE G.[USER_ID] = U.[USER_ID] AND G.[PROGRAM_ID] = @P_PROGRAM_ID AND G.[FUNC_CD] = @P_FUNC_CD
                             AND G.[GRANT_TYPE] = 'GRANT' AND G.[END_DT] >= CAST(GETDATE() AS DATE)))
              AND NOT EXISTS (SELECT 1 FROM [FW].[TB_USER_PROGRAM_FUNC] D
                               WHERE D.[USER_ID] = U.[USER_ID] AND D.[PROGRAM_ID] = @P_PROGRAM_ID AND D.[FUNC_CD] = @P_FUNC_CD
                                 AND D.[GRANT_TYPE] = 'DENY' AND D.[END_DT] >= CAST(GETDATE() AS DATE))));
