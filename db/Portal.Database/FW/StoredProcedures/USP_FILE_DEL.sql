-- 첨부 파일 삭제 표시(DEL_YN = 'Y'). 저장소의 내용은 정리 작업이 지운다.
-- 협력사 사용자는 자기 회사 묶음의 파일만 지울 수 있다(공유 묶음이어도 다른 회사 파일은 못 지운다).
-- 프로그램 권한(CREATE/UPDATE)은 화면 서버가 확인한다.
-- 결과: AFFECTED_CNT
CREATE PROCEDURE [FW].[USP_FILE_DEL]
  @P_FILE_ID    BIGINT,
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  UPDATE F
     SET F.[DEL_YN] = 'Y', F.[UPD_ID] = @P_USER_ID, F.[UPD_DT] = SYSUTCDATETIME()
    FROM [FW].[TB_FILE] F
    JOIN [FW].[TB_FILE_GROUP] G ON G.[FILE_GROUP_ID] = F.[FILE_GROUP_ID]
    JOIN [FW].[TB_USER] U ON U.[USER_ID] = @P_USER_ID AND U.[USER_STAT_CD] = 'ACTIVE'
   WHERE F.[FILE_ID] = @P_FILE_ID
     AND F.[DEL_YN] = 'N'
     AND (U.[USER_TYPE_CD] <> 'PARTNER' OR G.[COMPANY_CD] = U.[COMPANY_CD]);

  IF @@ROWCOUNT = 0
    THROW 50004, 'MSG.NOT_FOUND', 1;

  SELECT 1 AS [AFFECTED_CNT];
END
