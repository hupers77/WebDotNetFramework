-- ID/PW 로그인용 계정 조회. 로그인 전이라 @P_USER_ID는 비어 있고 대상은 @P_LOGIN_USER_ID로 받는다.
-- 비밀번호 비교·정책 판단은 애플리케이션(AuthService)이 한다. 없는 ID면 빈 결과.
-- LOCK_YN은 자동 해제 시간(PWD.LOCK_MINUTES)이 지났으면 'N'으로 돌려준다. 계정 사용 가능 여부는 ACCOUNT_STAT으로 요약한다:
--   OK / LOCKED(관리자 잠금, 실패 잠금) / EXPIRED(승인 대기·퇴사·휴면·계약 종료·회사 미사용)
-- 2단계 인증 판단용으로 등록한 TOTP 비밀키(암호문)·마지막 사용 단계와 관리자 역할 여부(ADMIN_YN)를 함께 돌려준다.
--   HAS_EMAIL: 이메일 인증번호를 받을 주소가 있는지 (Y/N, 주소는 돌려주지 않는다)
--   관리자 역할: SYS_ADMIN, PORTAL_ADMIN, SECURITY_AUDITOR (설정 AUTH.ADMIN_MFA 대상)
CREATE PROCEDURE [FW].[USP_AUTH_GET_USER]
  @P_LOGIN_USER_ID VARCHAR(30),
  @P_USER_ID       VARCHAR(30),
  @P_LANG_CD       VARCHAR(5),
  @P_CLIENT_IP     VARCHAR(45) = NULL
AS
BEGIN
  SET NOCOUNT ON;

  DECLARE @LOCK_MINUTES INT = TRY_CAST((SELECT [CONFIG_VAL] FROM [FW].[TB_CONFIG] WHERE [CONFIG_KEY] = 'PWD.LOCK_MINUTES') AS INT);
  DECLARE @TODAY DATE = CAST(GETDATE() AS DATE);

  SELECT U.[USER_ID], U.[COMPANY_CD], U.[USER_TYPE_CD], U.[LANG_CD],
         U.[PWD_HASH], U.[PWD_SALT], U.[PWD_CHG_DT], U.[PWD_CHG_REQ_YN],
         C.[ALLOW_IP_LIST],
         U.[MFA_SECRET_ENC], U.[MFA_LAST_STEP],
         CASE WHEN U.[EMAIL_ENC] IS NULL THEN 'N' ELSE 'Y' END AS [HAS_EMAIL],
         CASE WHEN EXISTS (SELECT 1 FROM [FW].[UFN_USER_ROLES](U.[USER_ID]) R
                            WHERE R.[ROLE_ID] IN ('SYS_ADMIN', 'PORTAL_ADMIN', 'SECURITY_AUDITOR')) THEN 'Y' ELSE 'N' END AS [ADMIN_YN],
         CASE
           WHEN U.[USER_STAT_CD] = 'LOCKED' THEN 'LOCKED'      -- 관리자 잠금은 자동 해제하지 않는다
           WHEN U.[USER_STAT_CD] <> 'ACTIVE'
             OR C.[USE_YN] <> 'Y'
             OR U.[ACCOUNT_END_DT] < @TODAY
             OR C.[CONTRACT_END_DT] < @TODAY THEN 'EXPIRED'
           WHEN U.[LOCK_YN] = 'Y'
            AND NOT (ISNULL(@LOCK_MINUTES, 0) > 0 AND U.[LOCK_DT] < DATEADD(MINUTE, -@LOCK_MINUTES, SYSUTCDATETIME())) THEN 'LOCKED'
           ELSE 'OK'
         END AS [ACCOUNT_STAT]
    FROM [FW].[TB_USER] U
    JOIN [FW].[TB_COMPANY] C ON C.[COMPANY_CD] = U.[COMPANY_CD]
   WHERE U.[USER_ID] = @P_LOGIN_USER_ID;
END
