-- 배치 작업 (설계서 13장 스케줄러). Portal.Worker가 주기(Cron)마다, 또는 스케줄 관리 화면의 수동 실행 요청으로 실행한다.
-- JOB_TYPE_CD = CLASS면 JOB_TARGET은 IScheduledJob 구현의 이름, SP면 실행할 SP 이름(FW.USP_JOB_..._EXEC 형식만 허용)이다.
-- 화면 저장의 동시성은 EDIT_VER로 확인한다(Worker가 실행 상태 컬럼을 수시로 바꾸므로 ROW_VER로는 확인할 수 없다).
-- 실행 중(RUNNING_YN = 'Y')인 작업은 다른 Worker가 가져가지 않는다. 잠근 지 TIMEOUT_MIN이 지나면 멈춘 것으로 보고 다시 실행한다.
CREATE TABLE [FW].[TB_JOB] (
  [JOB_ID]         VARCHAR(30)    NOT NULL,
  [JOB_NM]         NVARCHAR(100)  NOT NULL,
  [JOB_TYPE_CD]    VARCHAR(10)    NOT NULL,             -- CLASS / SP
  [JOB_TARGET]     VARCHAR(128)   NOT NULL,
  [CRON_EXPR]      VARCHAR(100)   NULL,                 -- 분 시 일 월 요일 (Worker 시간대 기준). NULL이면 수동 실행만
  [TIMEOUT_MIN]    INT            NOT NULL CONSTRAINT [DF_TB_JOB_TIMEOUT_MIN] DEFAULT (60),
  [DESCRIPTION]    NVARCHAR(500)  NULL,
  [USE_YN]         CHAR(1)        NOT NULL CONSTRAINT [DF_TB_JOB_USE_YN] DEFAULT ('Y'),
  [NEXT_RUN_DT]    DATETIME2(0)   NULL,                 -- 다음 예약 실행 (UTC). NULL이면 Worker가 주기로 계산해 채운다
  [RUN_REQ_YN]     CHAR(1)        NOT NULL CONSTRAINT [DF_TB_JOB_RUN_REQ_YN] DEFAULT ('N'),  -- 수동 실행 요청
  [RUN_REQ_ID]     VARCHAR(30)    NULL,
  [RUNNING_YN]     CHAR(1)        NOT NULL CONSTRAINT [DF_TB_JOB_RUNNING_YN] DEFAULT ('N'),
  [LOCK_DT]        DATETIME2(0)   NULL,
  [LAST_HIST_ID]   BIGINT         NULL,
  [LAST_RUN_DT]    DATETIME2(0)   NULL,
  [LAST_RESULT_CD] VARCHAR(10)    NULL,                 -- 공통코드 JOB_STAT
  [EDIT_VER]       INT            NOT NULL CONSTRAINT [DF_TB_JOB_EDIT_VER] DEFAULT (1),  -- 스케줄 관리 화면에서 저장할 때마다 1 증가
  [REG_ID]         VARCHAR(30)    NOT NULL CONSTRAINT [DF_TB_JOB_REG_ID] DEFAULT ('SYSTEM'),
  [REG_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_JOB_REG_DT] DEFAULT (SYSUTCDATETIME()),
  [UPD_ID]         VARCHAR(30)    NOT NULL CONSTRAINT [DF_TB_JOB_UPD_ID] DEFAULT ('SYSTEM'),
  [UPD_DT]         DATETIME2(0)   NOT NULL CONSTRAINT [DF_TB_JOB_UPD_DT] DEFAULT (SYSUTCDATETIME()),
  [ROW_VER]        ROWVERSION     NOT NULL,
  CONSTRAINT [PK_TB_JOB] PRIMARY KEY CLUSTERED ([JOB_ID]),
  CONSTRAINT [CK_TB_JOB_TYPE] CHECK ([JOB_TYPE_CD] IN ('CLASS', 'SP')),
  CONSTRAINT [CK_TB_JOB_TIMEOUT] CHECK ([TIMEOUT_MIN] BETWEEN 1 AND 1440),
  CONSTRAINT [CK_TB_JOB_USE_YN] CHECK ([USE_YN] IN ('Y', 'N')),
  CONSTRAINT [CK_TB_JOB_RUN_REQ_YN] CHECK ([RUN_REQ_YN] IN ('Y', 'N')),
  CONSTRAINT [CK_TB_JOB_RUNNING_YN] CHECK ([RUNNING_YN] IN ('Y', 'N'))
);
