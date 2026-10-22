-- 메일 발송 대기열. 업무 SP가 USP_MSG_QUEUE_SAVE로 쌓고, Portal.Worker의 메일 발송 작업(JOB_MAIL_SEND)이 꺼내 보낸다.
-- 본문은 저장하지 않고 템플릿 ID와 치환값만 둔다(보낼 때 받는 사람 언어로 만든다).
-- 받는 주소는 애플리케이션에서 암호화한 값만 두고, 발송을 마치면(성공·최종 실패) 주소와 치환값을 지운다(개인정보 최소 보관).
-- 개발계는 MAIL.ENABLED = N이라 발송하지 않고 쌓이기만 한다.
CREATE TABLE [FW].[TB_MSG_QUEUE] (

  [MSG_ID]         BIGINT         IDENTITY (1, 1) NOT NULL,
  [TEMPLATE_ID]    VARCHAR(30)    NOT NULL,
  [CHANNEL_CD]     VARCHAR(10)    NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_CHANNEL_CD] DEFAULT ('MAIL'), -- 공통코드 MSG_CHANNEL
  [TO_USER_ID]     VARCHAR(30)    NULL,                 -- 포털 사용자면 ID (반려된 가입 신청자처럼 계정이 없으면 NULL)
  [TO_EMAIL_ENC]   VARBINARY(512) NULL,                 -- 받는 주소 (애플리케이션 암호화). 발송을 마치면 NULL
  [LANG_CD]        VARCHAR(5)     NOT NULL,             -- 받는 사람 언어
  [PARAMS_JSON]    NVARCHAR(4000) NULL,                 -- 치환값 {"USER_NM":"..."}. 비밀번호·인증 번호는 넣지 않는다. 발송을 마치면 NULL
  [STAT_CD]        VARCHAR(10)    NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_STAT_CD] DEFAULT ('READY'),   -- READY/SENDING/SENT/FAILED
  [TRY_CNT]        TINYINT        NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_TRY_CNT] DEFAULT (0),
  [NEXT_TRY_DT]    DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_NEXT_TRY_DT] DEFAULT (SYSUTCDATETIME()),
  [LOCK_DT]        DATETIME2(0)   NULL,                 -- 발송 작업이 가져간 시각 (작업이 멈추면 일정 시간 뒤 다시 가져간다)
  [SENT_DT]        DATETIME2(0)   NULL,
  [ERROR_MSG]      NVARCHAR(500)  NULL,                 -- 마지막 실패 사유 (주소는 넣지 않는다)
  [REG_ID]         VARCHAR(30)    NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_REG_ID] DEFAULT ('SYSTEM'),
  [REG_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_REG_DT] DEFAULT (SYSUTCDATETIME()),
  [UPD_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_MSG_QUEUE_UPD_DT] DEFAULT (SYSUTCDATETIME()),
  CONSTRAINT [PK_TB_MSG_QUEUE] PRIMARY KEY CLUSTERED ([MSG_ID]),
  CONSTRAINT [FK_TB_MSG_QUEUE_TEMPLATE] FOREIGN KEY ([TEMPLATE_ID]) REFERENCES [FW].[TB_MSG_TEMPLATE] ([TEMPLATE_ID]),
  CONSTRAINT [CK_TB_MSG_QUEUE_STAT] CHECK ([STAT_CD] IN ('READY', 'SENDING', 'SENT', 'FAILED')),
  CONSTRAINT [CK_TB_MSG_QUEUE_CHANNEL] CHECK ([CHANNEL_CD] IN ('MAIL')),
  CONSTRAINT [CK_TB_MSG_QUEUE_PARAMS] CHECK ([PARAMS_JSON] IS NULL OR ISJSON([PARAMS_JSON]) = 1)
);
GO
-- 발송 작업이 보낼 차례인 메시지를 찾는다
CREATE INDEX [IX_TB_MSG_QUEUE_STAT_NEXT] ON [FW].[TB_MSG_QUEUE] ([STAT_CD], [NEXT_TRY_DT]) INCLUDE ([CHANNEL_CD], [LOCK_DT]);
GO
