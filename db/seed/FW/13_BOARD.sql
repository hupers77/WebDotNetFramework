-- 초기 게시판 4개 (초기 데이터 정의서 5장 메뉴와 같은 프로그램). 없는 행만 추가하고, 운영자가 화면에서 바꾼 설정은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.
-- BOARD_CD는 화면 주소의 마지막 마디(/Board/Notice)와 같다.

MERGE [FW].[TB_BOARD] AS T
USING (VALUES
  -- 게시판, 프로그램, 유형, 답글, 댓글, 첨부, 익명, 비밀글, 게시기간, 상단고정, 조회자목록, 정렬
  (N'Notice', N'BRD_NOTICE', N'NOTICE', N'N', N'N', N'Y', N'N', N'N', N'Y', N'Y', N'Y', 10),
  (N'Archive', N'BRD_ARCHIVE', N'ARCHIVE', N'N', N'N', N'Y', N'N', N'N', N'N', N'N', N'N', 20),
  (N'Qna', N'BRD_QNA', N'QNA', N'Y', N'Y', N'Y', N'N', N'Y', N'N', N'Y', N'N', 30),
  (N'EmpFree', N'BRD_EMP_FREE', N'NORMAL', N'N', N'Y', N'Y', N'N', N'N', N'N', N'N', N'N', 40)
) AS S ([BOARD_CD], [PROGRAM_ID], [BOARD_TYPE_CD], [REPLY_YN], [COMMENT_YN], [ATTACH_YN], [ANON_YN], [SECRET_YN], [PERIOD_YN], [PIN_YN], [READER_YN], [SORT_NO])
   ON T.[BOARD_CD] = S.[BOARD_CD]
WHEN NOT MATCHED THEN INSERT ([BOARD_CD], [PROGRAM_ID], [BOARD_TYPE_CD], [REPLY_YN], [COMMENT_YN], [ATTACH_YN], [ANON_YN], [SECRET_YN], [PERIOD_YN], [PIN_YN], [READER_YN], [SORT_NO])
  VALUES (S.[BOARD_CD], S.[PROGRAM_ID], S.[BOARD_TYPE_CD], S.[REPLY_YN], S.[COMMENT_YN], S.[ATTACH_YN], S.[ANON_YN], S.[SECRET_YN], S.[PERIOD_YN], S.[PIN_YN], S.[READER_YN], S.[SORT_NO]);
GO

MERGE [FW].[TB_BOARD_LANG] AS T
USING (VALUES
  (N'Notice', N'ko', N'공지사항'),
  (N'Notice', N'en', N'Notices'),
  (N'Notice', N'zh-CN', N'公告'),
  (N'Archive', N'ko', N'자료실'),
  (N'Archive', N'en', N'Archive'),
  (N'Archive', N'zh-CN', N'资料室'),
  (N'Qna', N'ko', N'FAQ·문의'),
  (N'Qna', N'en', N'FAQ & Q&A'),
  (N'Qna', N'zh-CN', N'常见问题与咨询'),
  (N'EmpFree', N'ko', N'사내 게시판'),
  (N'EmpFree', N'en', N'Internal board'),
  (N'EmpFree', N'zh-CN', N'内部公告板')
) AS S ([BOARD_CD], [LANG_CD], [BOARD_NM])
   ON T.[BOARD_CD] = S.[BOARD_CD] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([BOARD_CD], [LANG_CD], [BOARD_NM]) VALUES (S.[BOARD_CD], S.[LANG_CD], S.[BOARD_NM]);
GO
