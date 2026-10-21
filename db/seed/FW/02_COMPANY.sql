-- 초기 데이터 (초기 데이터 정의서 2장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 본사 (이름은 설치 후 회사 관리 화면에서 입력)
MERGE [FW].[TB_COMPANY] AS T
USING (VALUES
  (N'HQ', N'본사', N'HQ', 1)
) AS S ([COMPANY_CD], [COMPANY_NM], [COMPANY_TYPE_CD], [SORT_NO])
   ON T.[COMPANY_CD] = S.[COMPANY_CD]
WHEN NOT MATCHED THEN INSERT ([COMPANY_CD], [COMPANY_NM], [COMPANY_TYPE_CD], [SORT_NO]) VALUES (S.[COMPANY_CD], S.[COMPANY_NM], S.[COMPANY_TYPE_CD], S.[SORT_NO]);
GO
