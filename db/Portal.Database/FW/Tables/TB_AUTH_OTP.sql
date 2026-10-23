-- 2단계 인증 이메일 인증번호. 사용자당 한 행: 새 번호를 보내면 덮어쓴다.
-- 번호 원문은 저장하지 않고 해시(SHA-256)만 둔다. 메일 발송 횟수·간격 제한(SEND_CNT, WINDOW_DT, SEND_DT)도 이 행으로 센다.
CREATE TABLE [FW].[TB_AUTH_OTP] (

  [USER_ID]     VARCHAR(30)  NOT NULL,
  [CODE_HASH]   BINARY(32)   NULL,                  -- 쓴 번호는 NULL로 비워 다시 쓰지 못하게 한다
  [EXPIRE_DT]   DATETIME2(0) NULL,                  -- 번호 유효 시각 (UTC)
  [TRY_CNT]     TINYINT      NOT NULL CONSTRAINT [DF_TB_AUTH_OTP_TRY_CNT] DEFAULT (0),     -- 현재 번호로 입력한 횟수
  [SEND_CNT]    SMALLINT     NOT NULL CONSTRAINT [DF_TB_AUTH_OTP_SEND_CNT] DEFAULT (0),    -- 현재 시간 구간(WINDOW_DT부터 1시간)에 보낸 수
  [WINDOW_DT]   DATETIME2(0) NOT NULL,
  [SEND_DT]     DATETIME2(0) NOT NULL,              -- 마지막 발송 시각
  CONSTRAINT [PK_TB_AUTH_OTP] PRIMARY KEY CLUSTERED ([USER_ID])
);
GO
ALTER TABLE [FW].[TB_AUTH_OTP] ADD CONSTRAINT [FK_TB_AUTH_OTP_TB_USER] FOREIGN KEY ([USER_ID]) REFERENCES [FW].[TB_USER] ([USER_ID]);
GO
