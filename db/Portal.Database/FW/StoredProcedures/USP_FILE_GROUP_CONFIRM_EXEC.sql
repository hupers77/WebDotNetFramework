-- 첨부 묶음 확정. 업무 저장 SP가 업무 데이터에 FILE_GROUP_ID를 저장할 때 같은 트랜잭션에서 호출한다.
-- 확정하지 않은 묶음은 임시로 보고 정리 작업이 지운다. 묶음이 없거나 다른 프로그램의 것이면 오류다.
-- 협력사 사용자는 자기 회사 묶음만 확정할 수 있다.
--   @P_SHARE_YN: 'Y'면 다른 회사 사용자도 받을 수 있다(공지 첨부 등). NULL이면 그대로 둔다.
-- 결과: AFFECTED_CNT
CREATE PROCEDURE [FW].[USP_FILE_GROUP_CONFIRM_EXEC]
  @P_FILE_GROUP_ID  BIGINT,
  @P_PROGRAM_ID     VARCHAR(30),
  @P_SHARE_YN       CHAR(1)     = NULL,
  @P_USER_ID        VARCHAR(30),
  @P_LANG_CD        VARCHAR(5),
  @P_CLIENT_IP      VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  IF @P_SHARE_YN IS NOT NULL AND @P_SHARE_YN NOT IN ('Y', 'N')
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  UPDATE G
     SET G.[CONFIRM_YN] = 'Y', G.[SHARE_YN] = ISNULL(@P_SHARE_YN, G.[SHARE_YN]), G.[UPD_ID] = @P_USER_ID, G.[UPD_DT] = SYSUTCDATETIME()
    FROM [FW].[TB_FILE_GROUP] G
    JOIN [FW].[TB_USER] U ON U.[USER_ID] = @P_USER_ID AND U.[USER_STAT_CD] = 'ACTIVE'
   WHERE G.[FILE_GROUP_ID] = @P_FILE_GROUP_ID AND G.[PROGRAM_ID] = @P_PROGRAM_ID
     AND (U.[USER_TYPE_CD] <> 'PARTNER' OR G.[COMPANY_CD] = U.[COMPANY_CD]);

  IF @@ROWCOUNT = 0
    THROW 50004, 'MSG.NOT_FOUND', 1;

  SELECT 1 AS [AFFECTED_CNT];
END
