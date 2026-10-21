-- 시스템 설정 전체. 웹서버가 시작할 때와 캐시 버전(CONFIG)이 바뀔 때 읽어 메모리에 둔다.
-- SECRET 값은 애플리케이션이 암호화해 저장한 그대로 내보낸다(복호화는 IPortalConfig 구현이 한다).
CREATE PROCEDURE [FW].[USP_CONFIG_GET_LIST]
  @P_USER_ID    VARCHAR(30),
  @P_LANG_CD    VARCHAR(5),
  @P_CLIENT_IP  VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  SELECT [CONFIG_KEY], [CONFIG_VAL], [VAL_TYPE_CD], [ENC_YN]
    FROM [FW].[TB_CONFIG]
   ORDER BY [CONFIG_KEY];
END
