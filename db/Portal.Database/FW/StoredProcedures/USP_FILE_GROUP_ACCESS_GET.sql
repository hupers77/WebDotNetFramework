-- 첨부 묶음의 기록 단위 권한. FileController가 묶음 프로그램 권한(READ·CREATE·UPDATE)을 확인한 뒤 부른다.
-- 프로그램 권한만 보면 같은 프로그램의 모든 묶음을 다루게 되므로(파일 ID는 차례로 늘어난다), 묶음이 붙은 기록의 규칙을 한 번 더 본다.
--   VIEW_YN : 볼 수 있는지.
--             - 확정 전 임시 묶음은 올린 사람만 본다.
--             - 게시글의 첨부·본문 사진 묶음은 그 글을 볼 수 있어야 한다(삭제·게시기간·공개 대상 회사·비밀글, UFN_POST_VISIBLE).
--             - 그 밖의 확정된 묶음은 프로그램 권한과 회사 격리(파일 SP)로 판단한다('Y').
--   OWNER_YN: 묶음을 만든 사람인지. 남의 묶음에 파일을 더하거나 남이 올린 파일을 지우려면 화면 서버가 UPDATE 권한을 요구한다.
-- @P_MANAGER_YN: 묶음 프로그램의 수정(UPDATE) 권한이 있는지 (애플리케이션이 평가해 넘긴다. 게시판에서는 게시판 관리자).
-- 결과: FILE_GROUP_ID, OWNER_YN, VIEW_YN. 없는 묶음이면 0행.
CREATE PROCEDURE [FW].[USP_FILE_GROUP_ACCESS_GET]
  @P_FILE_GROUP_ID  BIGINT,
  @P_MANAGER_YN     CHAR(1)     = 'N',
  @P_USER_ID        VARCHAR(30),
  @P_LANG_CD        VARCHAR(5),
  @P_CLIENT_IP      VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  IF @P_MANAGER_YN NOT IN ('Y', 'N')
    THROW 50005, 'MSG.INVALID_INPUT', 1;

  SELECT G.[FILE_GROUP_ID],
         IIF(G.[REG_ID] = @P_USER_ID, 'Y', 'N') AS [OWNER_YN],
         CASE
           WHEN G.[CONFIRM_YN] = 'N' THEN IIF(G.[REG_ID] = @P_USER_ID, 'Y', 'N')
           WHEN EXISTS (SELECT 1 FROM [FW].[TB_POST] P WHERE P.[FILE_GROUP_ID] = G.[FILE_GROUP_ID])
             OR EXISTS (SELECT 1 FROM [FW].[TB_POST] P WHERE P.[IMAGE_GROUP_ID] = G.[FILE_GROUP_ID])
             THEN IIF(EXISTS (SELECT 1
                                FROM [FW].[TB_POST] P
                                JOIN [FW].[UFN_POST_VISIBLE](@P_USER_ID, @P_MANAGER_YN) V ON V.[POST_ID] = P.[POST_ID] AND V.[SECRET_OK_YN] = 'Y'
                               WHERE P.[FILE_GROUP_ID] = G.[FILE_GROUP_ID] OR P.[IMAGE_GROUP_ID] = G.[FILE_GROUP_ID]), 'Y', 'N')
           ELSE 'Y'
         END AS [VIEW_YN]
    FROM [FW].[TB_FILE_GROUP] G
   WHERE G.[FILE_GROUP_ID] = @P_FILE_GROUP_ID;
END
