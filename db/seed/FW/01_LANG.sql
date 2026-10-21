-- 초기 데이터 (초기 데이터 정의서 2장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 언어
MERGE [FW].[TB_LANG] AS T
USING (VALUES
  (N'ko', N'한국어', N'Y', 1),
  (N'en', N'English', N'N', 2),
  (N'zh-CN', N'简体中文', N'N', 3)
) AS S ([LANG_CD], [LANG_NM], [DEFAULT_YN], [SORT_NO])
   ON T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([LANG_CD], [LANG_NM], [DEFAULT_YN], [SORT_NO]) VALUES (S.[LANG_CD], S.[LANG_NM], S.[DEFAULT_YN], S.[SORT_NO]);
GO
