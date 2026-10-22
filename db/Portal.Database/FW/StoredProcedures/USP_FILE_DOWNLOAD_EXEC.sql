-- 다운로드 횟수 증가. 화면 서버가 USP_FILE_GET과 READ 권한을 확인한 뒤, 내용을 보내기 전에 호출한다.
-- 누가 언제 받았는지는 접속기록(요청 경로 /File/Download/{id})에 남는다.
-- 결과: AFFECTED_CNT
CREATE PROCEDURE [FW].[USP_FILE_DOWNLOAD_EXEC]
  @P_FILE_ID    BIGINT,
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  UPDATE [FW].[TB_FILE]
     SET [DOWN_CNT] += 1
   WHERE [FILE_ID] = @P_FILE_ID AND [DEL_YN] = 'N';

  SELECT @@ROWCOUNT AS [AFFECTED_CNT];
END
