-- 레포트 백그라운드 생성 요청 (설계서 11장 '레포트'). 행이 많은 레포트(TB_REPORT.MAX_ROWS가 화면에서 바로 만드는 한도보다 큼)를
-- 요청자가 대기열에 넣으면 웹 서버의 백그라운드 서비스가 하나씩 꺼내 요청자의 권한·데이터 범위로 만들고, 끝나면 포털 알림을 보낸다.
-- 결과 파일은 첨부 저장소(rpt/yyyy/MM/<guid>)에 두고 끝난 지 7일이 지나면(EXPIRE_DT) 정리가 파일과 행을 함께 지운다.
-- 조건 값(PARAMS_JSON)과 사유(REASON)에는 개인정보가 섞일 수 있으므로 행과 함께 지운다. 개인정보 접속기록은 만들 때 남긴다.
CREATE TABLE [FW].[TB_REPORT_RUN] (
  [RUN_ID]          BIGINT         IDENTITY (1, 1) NOT NULL,
  [REPORT_ID]       VARCHAR(30)    NOT NULL,
  [FORMAT_CD]       VARCHAR(5)     NOT NULL,             -- PDF / EXCEL
  [PARAMS_JSON]     NVARCHAR(2000) NOT NULL,             -- {"P_MONTH":"2026-09"} 조건 이름 → 값
  [REASON]          NVARCHAR(200)  NULL,                 -- 개인정보 레포트의 다운로드 사유
  [REQ_USER_ID]     VARCHAR(30)    NOT NULL,             -- 요청자. 만들 때 이 사용자의 권한·데이터 범위·언어로 실행한다
  [REQ_COMPANY_CD]  VARCHAR(10)    NOT NULL,
  [REQ_USER_TYPE_CD] VARCHAR(10)   NOT NULL,
  [REQ_LANG_CD]     VARCHAR(5)     NOT NULL,
  [STATUS_CD]       VARCHAR(10)    NOT NULL CONSTRAINT [DF_TB_REPORT_RUN_STATUS_CD] DEFAULT ('QUEUED'),  -- QUEUED / RUNNING / DONE / FAILED
  [ROW_CNT]         INT            NULL,
  [FILE_NM]         NVARCHAR(200)  NULL,                 -- 내려받을 파일 이름
  [FILE_SIZE]       BIGINT         NULL,
  [STORED_PATH]     VARCHAR(100)   NULL,                 -- 저장소 기준 상대 경로
  [ERR_KEY]         VARCHAR(60)    NULL,                 -- 실패 사유 문구 키 (RPT.TOO_MANY 등)
  [ERR_ARG]         NVARCHAR(100)  NULL,                 -- 문구의 {0}
  [REG_DT]          DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_REPORT_RUN_REG_DT] DEFAULT (SYSUTCDATETIME()),
  [START_DT]        DATETIME2(0)   NULL,
  [END_DT]          DATETIME2(0)   NULL,
  [EXPIRE_DT]       DATETIME2(0)   NULL,                 -- 이 시각이 지나면 목록에서 빼고 정리가 지운다 (끝날 때 정함, 내가 지우면 지금)
  CONSTRAINT [PK_TB_REPORT_RUN] PRIMARY KEY CLUSTERED ([RUN_ID]),
  CONSTRAINT [FK_TB_REPORT_RUN_TB_REPORT] FOREIGN KEY ([REPORT_ID]) REFERENCES [FW].[TB_REPORT] ([REPORT_ID]),
  CONSTRAINT [FK_TB_REPORT_RUN_TB_USER] FOREIGN KEY ([REQ_USER_ID]) REFERENCES [FW].[TB_USER] ([USER_ID]),
  CONSTRAINT [CK_TB_REPORT_RUN_FORMAT_CD] CHECK ([FORMAT_CD] IN ('PDF', 'EXCEL')),
  CONSTRAINT [CK_TB_REPORT_RUN_STATUS_CD] CHECK ([STATUS_CD] IN ('QUEUED', 'RUNNING', 'DONE', 'FAILED')),
  CONSTRAINT [CK_TB_REPORT_RUN_PARAMS_JSON] CHECK (ISJSON([PARAMS_JSON]) = 1)
);
GO
-- 내 요청 목록 (최근 것부터)
CREATE INDEX [IX_TB_REPORT_RUN_USER] ON [FW].[TB_REPORT_RUN] ([REQ_USER_ID], [RUN_ID] DESC);
GO
-- 다음 요청 꺼내기, 정리 대상 찾기
CREATE INDEX [IX_TB_REPORT_RUN_STATUS] ON [FW].[TB_REPORT_RUN] ([STATUS_CD], [RUN_ID]) INCLUDE ([EXPIRE_DT], [START_DT]);
