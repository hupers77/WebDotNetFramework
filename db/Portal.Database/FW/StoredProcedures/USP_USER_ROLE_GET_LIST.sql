-- 사용자 한 명의 역할 선택지 (사용자 관리 화면 오른쪽). 프로그램: SYS_USER_MGT, PTN_USER_MGT
-- 대상 사용자가 @P_PROGRAM_ID의 데이터 범위(UFN_DATA_SCOPE) 밖이면 없는 것으로 본다(협력사 관리자는 자기 회사만).
-- 사용 중인 역할 전부를 돌려주고, 직접 부여 여부·실제 적용 여부(부서·AD 그룹·위임 포함)·이 사용자에게 줄 수 있는지를 함께 준다.
-- 줄 수 있는 역할: 역할의 COMPANY_TYPE_CD가 비었거나 사용자 회사 유형과 같은 것. SYS_ADMIN은 SYS_ADMIN만 주고 뺄 수 있다.
--   협력사 관리 화면(PTN_USER_MGT)에서는 협력사 역할(COMPANY_TYPE_CD = 'PARTNER')만 줄 수 있고, 목록에도 협력사 역할과 이미 가진 역할만 보인다.
-- 결과: ROLE_ID, ROLE_NM, DESCRIPTION, COMPANY_TYPE_CD, ASSIGNED_YN, EFFECTIVE_YN, ASSIGNABLE_YN, END_DT
CREATE PROCEDURE [FW].[USP_USER_ROLE_GET_LIST]
  @P_PROGRAM_ID     VARCHAR(30),
  @P_TARGET_USER_ID VARCHAR(30),
  @P_USER_ID        VARCHAR(30),
  @P_LANG_CD        VARCHAR(5),
  @P_CLIENT_IP      VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  IF @P_PROGRAM_ID NOT IN ('SYS_USER_MGT', 'PTN_USER_MGT')
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  DECLARE @COMPANY_TYPE VARCHAR(10) = (SELECT C.[COMPANY_TYPE_CD] FROM [FW].[TB_USER] U JOIN [FW].[TB_COMPANY] C ON C.[COMPANY_CD] = U.[COMPANY_CD]
                                        WHERE U.[USER_ID] = @P_TARGET_USER_ID
                                          AND EXISTS (SELECT 1 FROM [FW].[UFN_DATA_SCOPE](@P_USER_ID, @P_PROGRAM_ID) S
                                              WHERE S.[COMPANY_CD] = U.[COMPANY_CD] AND (S.[DEPT_ID] IS NULL OR S.[DEPT_ID] = U.[DEPT_ID])
                                                AND (S.[USER_ID] IS NULL OR S.[USER_ID] = U.[USER_ID])));
  IF @COMPANY_TYPE IS NULL
    THROW 50004, 'MSG.NOT_FOUND', 1;
  DECLARE @CALLER_SYS_ADMIN CHAR(1) = IIF(EXISTS (SELECT 1 FROM [FW].[UFN_USER_ROLES](@P_USER_ID) WHERE [ROLE_ID] = 'SYS_ADMIN'), 'Y', 'N');

  SELECT R.[ROLE_ID], R.[ROLE_NM], R.[DESCRIPTION], R.[COMPANY_TYPE_CD],
         IIF(UR.[ROLE_ID] IS NULL, 'N', 'Y') AS [ASSIGNED_YN],
         IIF(E.[ROLE_ID] IS NULL, 'N', 'Y') AS [EFFECTIVE_YN],
         IIF((R.[COMPANY_TYPE_CD] IS NULL OR R.[COMPANY_TYPE_CD] = @COMPANY_TYPE)
             AND (@P_PROGRAM_ID <> 'PTN_USER_MGT' OR R.[COMPANY_TYPE_CD] = 'PARTNER')
             AND (R.[ROLE_ID] <> 'SYS_ADMIN' OR @CALLER_SYS_ADMIN = 'Y'), 'Y', 'N') AS [ASSIGNABLE_YN],
         UR.[END_DT]
    FROM [FW].[TB_ROLE] R
    LEFT JOIN [FW].[TB_USER_ROLE] UR ON UR.[USER_ID] = @P_TARGET_USER_ID AND UR.[ROLE_ID] = R.[ROLE_ID]
    LEFT JOIN [FW].[UFN_USER_ROLES](@P_TARGET_USER_ID) E ON E.[ROLE_ID] = R.[ROLE_ID]
   WHERE (R.[USE_YN] = 'Y' OR UR.[ROLE_ID] IS NOT NULL)
     AND (@P_PROGRAM_ID <> 'PTN_USER_MGT' OR R.[COMPANY_TYPE_CD] = 'PARTNER' OR UR.[ROLE_ID] IS NOT NULL)
   ORDER BY R.[SORT_NO], R.[ROLE_ID];
END
