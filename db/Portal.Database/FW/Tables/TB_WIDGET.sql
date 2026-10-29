-- 대시보드 위젯 정의. 데이터는 DATA_SP(USP_WIDGET_로 시작하는 SP)가 주고, TYPE_CD가 표시 방법을 정한다.
-- PROGRAM_ID가 있으면 그 프로그램의 읽기(READ) 권한이 있는 사용자만 보고, 비우면 로그인한 모든 사용자가 본다.
CREATE TABLE [FW].[TB_WIDGET] (

  [WIDGET_CD]      VARCHAR(30)    NOT NULL,
  [TYPE_CD]        VARCHAR(10)    NOT NULL,                                                          -- CARD: 숫자 카드, LIST: 목록, CHART: 막대 차트(LABEL·VALUE)
  [DATA_SP]        VARCHAR(100)   NOT NULL,                                                          -- 예: FW.USP_WIDGET_NOTICE_RECENT
  [PROGRAM_ID]     VARCHAR(30)    NULL,
  [LINK_URL]       VARCHAR(200)   NULL,                                                              -- 위젯 제목을 누르면 가는 화면 (사이트 안 경로)
  [ICON_CD]        VARCHAR(30)    NULL,                                                              -- 아이콘 (공통코드 ICON 값, img/icons.svg)
  [SPAN_COLS]      INT            NOT NULL CONSTRAINT [DF_TB_WIDGET_SPAN_COLS] DEFAULT (6),          -- 12칸 그리드에서 차지하는 칸 수
  [CACHE_SEC]      INT            NOT NULL CONSTRAINT [DF_TB_WIDGET_CACHE_SEC] DEFAULT (60),         -- 사용자별 데이터 캐시 시간(초). 0이면 캐시 안 함
  [SORT_NO]        INT            NOT NULL CONSTRAINT [DF_TB_WIDGET_SORT_NO] DEFAULT (0),
  [USE_YN]         CHAR(1)        NOT NULL CONSTRAINT [DF_TB_WIDGET_USE_YN] DEFAULT ('Y'),
  [REG_ID]         VARCHAR(30)    NOT NULL CONSTRAINT [DF_TB_WIDGET_REG_ID] DEFAULT ('SYSTEM'),
  [REG_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_WIDGET_REG_DT] DEFAULT (SYSUTCDATETIME()),
  [UPD_ID]         VARCHAR(30)    NOT NULL CONSTRAINT [DF_TB_WIDGET_UPD_ID] DEFAULT ('SYSTEM'),
  [UPD_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_WIDGET_UPD_DT] DEFAULT (SYSUTCDATETIME()),
  [ROW_VER]        ROWVERSION     NOT NULL,
  CONSTRAINT [PK_TB_WIDGET] PRIMARY KEY CLUSTERED ([WIDGET_CD]),
  CONSTRAINT [CK_TB_WIDGET_TYPE_CD] CHECK ([TYPE_CD] IN ('CARD', 'LIST', 'CHART')),
  CONSTRAINT [CK_TB_WIDGET_DATA_SP] CHECK ([DATA_SP] LIKE '[A-Z][A-Z]%.USP[_]WIDGET[_]%'),
  CONSTRAINT [CK_TB_WIDGET_SPAN_COLS] CHECK ([SPAN_COLS] BETWEEN 1 AND 12),
  CONSTRAINT [CK_TB_WIDGET_CACHE_SEC] CHECK ([CACHE_SEC] BETWEEN 0 AND 86400),
  CONSTRAINT [CK_TB_WIDGET_USE_YN] CHECK ([USE_YN] IN ('Y', 'N'))
);
GO
ALTER TABLE [FW].[TB_WIDGET] ADD CONSTRAINT [FK_TB_WIDGET_TB_PROGRAM] FOREIGN KEY ([PROGRAM_ID]) REFERENCES [FW].[TB_PROGRAM] ([PROGRAM_ID]);
