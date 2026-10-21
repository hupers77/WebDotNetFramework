-- 초기 데이터 (초기 데이터 정의서 (프레임워크 내부)). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 캐시 버전 키
MERGE [FW].[TB_CACHE_VERSION] AS T
USING (VALUES
  (N'AUTH'),
  (N'CODE'),
  (N'CONFIG'),
  (N'LANG'),
  (N'MENU')
) AS S ([CACHE_KEY])
   ON T.[CACHE_KEY] = S.[CACHE_KEY]
WHEN NOT MATCHED THEN INSERT ([CACHE_KEY]) VALUES (S.[CACHE_KEY]);
GO

-- 로그 해시 체인 머리 (테이블별 1행)
MERGE [LOG].[TB_LOG_CHAIN] AS T
USING (VALUES
  (N'LOG.TB_AUDIT_LOG'), (N'LOG.TB_ACCESS_LOG'), (N'LOG.TB_LOGIN_LOG'), (N'LOG.TB_PII_ACCESS_LOG')
) AS S ([TABLE_NM])
   ON T.[TABLE_NM] = S.[TABLE_NM]
WHEN NOT MATCHED THEN INSERT ([TABLE_NM]) VALUES (S.[TABLE_NM]);
GO
