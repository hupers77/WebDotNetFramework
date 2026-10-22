-- 공통코드 언어별 명칭 (FW.USP_CODE_SAVE). 컬럼 순서가 C# DTO 속성 순서와 같아야 한다.
CREATE TYPE [FW].[TVP_CODE_LANG] AS TABLE (
  [CODE]         VARCHAR(30)    NOT NULL,
  [LANG_CD]      VARCHAR(5)     NOT NULL,
  [CODE_NM]      NVARCHAR(200)  NULL,                 -- 비우면 그 언어의 명칭을 지운다(기본 언어는 비울 수 없음)
  PRIMARY KEY ([CODE], [LANG_CD])
);
