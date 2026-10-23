-- 글 공개 대상으로 고를 수 있는 협력사 목록 (게시판 글쓰기 화면). 프로그램: 게시판의 프로그램 (UPDATE는 애플리케이션이 확인)
-- 사용 중인 협력사의 코드와 이름만 준다. 데이터 범위는 적용하지 않는다(글을 쓰는 관리자는 모든 협력사에 공개할 수 있다).
-- 결과: COMPANY_CD, COMPANY_NM
CREATE PROCEDURE [FW].[USP_BOARD_TARGET_GET_LIST]
  @P_USER_ID     VARCHAR(30),
  @P_LANG_CD     VARCHAR(5),
  @P_CLIENT_IP   VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  SELECT C.[COMPANY_CD], C.[COMPANY_NM]
    FROM [FW].[TB_COMPANY] C
   WHERE C.[COMPANY_TYPE_CD] = 'PARTNER' AND C.[USE_YN] = 'Y'
   ORDER BY C.[SORT_NO], C.[COMPANY_NM], C.[COMPANY_CD];
END
