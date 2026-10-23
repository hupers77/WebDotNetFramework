-- 게시글을 볼 수 있는 사람인지 (목록·단건·댓글 SP가 조인한다). 게시판 읽기 권한(READ)은 애플리케이션이 확인하고,
-- 여기서는 글 단위 규칙만 본다. @P_MANAGER_YN은 게시판 프로그램의 수정(UPDATE) 권한이 있는지(애플리케이션이 평가해 넘긴다).
--   - 삭제된 글, 사용 안 하는 게시판의 글은 작성자와 관리자에게도 보이지 않는다.
--   - 게시기간이 지났거나 아직 안 된 글은 작성자와 관리자만 본다.
--   - 공개 대상 회사가 지정된 글은 본사 사용자, 지정된 회사 사용자, 작성자, 관리자만 본다.
-- 결과: POST_ID, MINE_YN(내가 쓴 글), SECRET_OK_YN(비밀글 내용을 볼 수 있다: 작성자, 관리자, 내 글의 답글 원본 작성자 포함)
CREATE FUNCTION [FW].[UFN_POST_VISIBLE] (@P_USER_ID VARCHAR(30), @P_MANAGER_YN CHAR(1))
RETURNS TABLE
AS
RETURN
  SELECT P.[POST_ID],
         IIF(P.[WRITER_ID] = U.[USER_ID], 'Y', 'N') AS [MINE_YN],
         IIF(P.[SECRET_YN] = 'N' OR P.[WRITER_ID] = U.[USER_ID] OR @P_MANAGER_YN = 'Y'
             OR EXISTS (SELECT 1 FROM [FW].[TB_POST] R WHERE R.[POST_ID] = P.[ROOT_POST_ID] AND R.[WRITER_ID] = U.[USER_ID]), 'Y', 'N') AS [SECRET_OK_YN]
    FROM [FW].[TB_POST] P
    JOIN [FW].[TB_BOARD] B ON B.[BOARD_CD] = P.[BOARD_CD] AND B.[USE_YN] = 'Y'
    JOIN [FW].[TB_USER] U ON U.[USER_ID] = @P_USER_ID AND U.[USER_STAT_CD] = 'ACTIVE'
    JOIN [FW].[TB_COMPANY] C ON C.[COMPANY_CD] = U.[COMPANY_CD]
   WHERE P.[DEL_YN] = 'N'
     AND (P.[WRITER_ID] = U.[USER_ID] OR @P_MANAGER_YN = 'Y'
          OR ((B.[PERIOD_YN] = 'N'
               OR ((P.[START_DT] IS NULL OR P.[START_DT] <= CAST(SYSUTCDATETIME() AS DATE))
                   AND (P.[END_DT] IS NULL OR P.[END_DT] >= CAST(SYSUTCDATETIME() AS DATE))))
              AND (C.[COMPANY_TYPE_CD] = 'HQ'
                   OR NOT EXISTS (SELECT 1 FROM [FW].[TB_POST_TARGET] T WHERE T.[POST_ID] = P.[ROOT_POST_ID])
                   OR EXISTS (SELECT 1 FROM [FW].[TB_POST_TARGET] T WHERE T.[POST_ID] = P.[ROOT_POST_ID] AND T.[COMPANY_CD] = U.[COMPANY_CD]))));
