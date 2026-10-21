-- 공통코드 그리드 일괄 저장 (FW.USP_CODE_SAVE). 컬럼 순서가 C# DTO 속성 순서와 같아야 한다.
CREATE TYPE [FW].[TVP_CODE] AS TABLE (
  [ROW_STAT]     CHAR(1)        NOT NULL,             -- I/U/D (그리드 행 상태)
  [CODE]         VARCHAR(30)    NOT NULL,
  [CODE_NM]      NVARCHAR(200)  NULL,                 -- 현재 사용자 언어의 명칭
  [PARENT_CODE]  VARCHAR(30)    NULL,
  [SORT_NO]      INT            NULL,
  [ATTR1]        NVARCHAR(200)  NULL,
  [ATTR2]        NVARCHAR(200)  NULL,
  [ATTR3]        NVARCHAR(200)  NULL,
  [ATTR4]        NVARCHAR(200)  NULL,
  [ATTR5]        NVARCHAR(200)  NULL,
  [START_DT]     DATE           NULL,
  [END_DT]       DATE           NULL,
  [USE_YN]       CHAR(1)        NULL,
  [ROW_VER]      BINARY(8)      NULL,
  PRIMARY KEY ([CODE])
);
