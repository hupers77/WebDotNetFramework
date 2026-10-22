-- 설정값 일괄 저장 (FW.USP_CONFIG_SAVE). 컬럼 순서가 C# DTO 속성 순서와 같아야 한다.
CREATE TYPE [FW].[TVP_CONFIG] AS TABLE (
  [CONFIG_KEY]   VARCHAR(100)   NOT NULL,
  [CONFIG_VAL]   NVARCHAR(2000) NULL,                 -- SECRET은 애플리케이션이 암호화한 값
  [ROW_VER]      BINARY(8)      NOT NULL,             -- 조회 시점의 값 (동시성 확인)
  PRIMARY KEY ([CONFIG_KEY])
);
