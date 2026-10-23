-- 초기 데이터 (초기 데이터 정의서 8장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- REPORT_ID는 06_PROGRAM.sql의 프로그램 ID와 같다(먼저 넣는다). 운영자가 바꾼 조건·최대 행 수는 덮어쓰지 않는다.

-- 기본 레포트
MERGE [FW].[TB_REPORT] AS T
USING (VALUES
  (N'RPT_LOG_MONTHLY', N'LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST', N'AccessMonthly', N'[{"name":"P_MONTH","type":"MONTH","required":true,"labelKey":"LBL.MONTH","default":"PREV_MONTH"}]', N'L', N'Y', N'Y', 40)
) AS S ([REPORT_ID], [DATA_SP], [TEMPLATE_VIEW], [PARAMS_JSON], [ORIENT_CD], [APPROVAL_YN], [TOTAL_YN], [MAX_ROWS])
   ON T.[REPORT_ID] = S.[REPORT_ID]
WHEN NOT MATCHED THEN INSERT ([REPORT_ID], [DATA_SP], [TEMPLATE_VIEW], [PARAMS_JSON], [ORIENT_CD], [APPROVAL_YN], [TOTAL_YN], [MAX_ROWS]) VALUES (S.[REPORT_ID], S.[DATA_SP], S.[TEMPLATE_VIEW], S.[PARAMS_JSON], S.[ORIENT_CD], S.[APPROVAL_YN], S.[TOTAL_YN], S.[MAX_ROWS]);
GO
