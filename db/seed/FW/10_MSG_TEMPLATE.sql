-- 초기 데이터 (초기 데이터 정의서 7장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 치환 변수 {{USER_NM}} 등은 보낼 때 HTML 인코딩해 넣는다. {{SITE_NM}}(사이트명), {{LINK}}(로그인 주소)는 발송 작업이 채운다.
-- 포털 알림은 같은 템플릿의 제목을 알림 제목으로, 본문을 글자만 남겨 내용으로 보여 준다.

-- 메시지 템플릿 (LINK_PROGRAM_ID: 포털 알림을 누르면 여는 화면)
MERGE [FW].[TB_MSG_TEMPLATE] AS T
USING (VALUES
  (N'TPL_PARTNER_APPROVED', N'협력사 계정 승인 안내', N'Y', N'N', NULL),
  (N'TPL_PARTNER_REJECTED', N'협력사 가입 반려 안내', N'Y', N'N', NULL),
  (N'TPL_PARTNER_SIGNUP_REQ', N'협력사 가입 신청 알림 (승인 담당자에게)', N'Y', N'Y', N'SYS_PARTNER_APPROVE'),
  (N'TPL_JOB_FAILED', N'배치 작업 실패 알림', N'Y', N'Y', N'SYS_JOB_MGT')
) AS S ([TEMPLATE_ID], [TEMPLATE_NM], [MAIL_YN], [PORTAL_YN], [LINK_PROGRAM_ID])
   ON T.[TEMPLATE_ID] = S.[TEMPLATE_ID]
WHEN MATCHED AND T.[LINK_PROGRAM_ID] IS NULL AND S.[LINK_PROGRAM_ID] IS NOT NULL THEN UPDATE SET T.[LINK_PROGRAM_ID] = S.[LINK_PROGRAM_ID]
WHEN NOT MATCHED THEN INSERT ([TEMPLATE_ID], [TEMPLATE_NM], [MAIL_YN], [PORTAL_YN], [LINK_PROGRAM_ID]) VALUES (S.[TEMPLATE_ID], S.[TEMPLATE_NM], S.[MAIL_YN], S.[PORTAL_YN], S.[LINK_PROGRAM_ID]);
GO

-- 템플릿 제목·본문 (3개 언어)
MERGE [FW].[TB_MSG_TEMPLATE_LANG] AS T
USING (VALUES
  (N'TPL_PARTNER_APPROVED', N'ko', N'[{{SITE_NM}}] 협력사 계정이 승인되었습니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>신청하신 협력사 계정 <strong>{{USER_ID}}</strong>이(가) 승인되었습니다. 신청할 때 정한 비밀번호로 로그인하세요.</p><p>처음 로그인하면 휴대폰 인증 앱(2단계 인증)을 등록합니다.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_APPROVED', N'en', N'[{{SITE_NM}}] Your partner account has been approved', N'<p>Hello {{USER_NM}},</p><p>Your partner account <strong>{{USER_ID}}</strong> has been approved. Sign in with the password you chose when you applied.</p><p>At your first sign-in you will register an authenticator app on your phone (two-step verification).</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_APPROVED', N'zh-CN', N'[{{SITE_NM}}] 您的合作公司账户已获批准', N'<p>{{USER_NM}}，您好：</p><p>您申请的合作公司账户 <strong>{{USER_ID}}</strong> 已获批准。请使用申请时设置的密码登录。</p><p>首次登录时需要在手机上注册身份验证器应用（两步验证）。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_REJECTED', N'ko', N'[{{SITE_NM}}] 협력사 계정 신청이 반려되었습니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>신청하신 협력사 계정 <strong>{{USER_ID}}</strong>이(가) 반려되었습니다.</p><p>사유: {{REJECT_REASON}}</p><p>궁금한 점은 본사 담당자에게 문의해 주세요.</p>'),
  (N'TPL_PARTNER_REJECTED', N'en', N'[{{SITE_NM}}] Your partner account request was rejected', N'<p>Hello {{USER_NM}},</p><p>Your request for the partner account <strong>{{USER_ID}}</strong> was rejected.</p><p>Reason: {{REJECT_REASON}}</p><p>Please contact your headquarters representative if you have any questions.</p>'),
  (N'TPL_PARTNER_REJECTED', N'zh-CN', N'[{{SITE_NM}}] 您的合作公司账户申请已被驳回', N'<p>{{USER_NM}}，您好：</p><p>您申请的合作公司账户 <strong>{{USER_ID}}</strong> 已被驳回。</p><p>原因：{{REJECT_REASON}}</p><p>如有疑问，请联系总部负责人。</p>'),
  (N'TPL_PARTNER_SIGNUP_REQ', N'ko', N'[{{SITE_NM}}] 협력사 계정 가입 신청: {{COMPANY_NM}}', N'<p>{{COMPANY_NM}}에서 협력사 계정 <strong>{{USER_ID}}</strong>의 가입을 신청했습니다.</p><p>시스템 관리 > 협력사 계정 승인에서 확인하고 승인하거나 반려하세요.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_SIGNUP_REQ', N'en', N'[{{SITE_NM}}] Partner account request: {{COMPANY_NM}}', N'<p>{{COMPANY_NM}} requested the partner account <strong>{{USER_ID}}</strong>.</p><p>Review it in System > Partner approval and approve or reject it.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_SIGNUP_REQ', N'zh-CN', N'[{{SITE_NM}}] 合作公司账户申请：{{COMPANY_NM}}', N'<p>{{COMPANY_NM}}申请了合作公司账户 <strong>{{USER_ID}}</strong>。</p><p>请在“系统管理 > 合作公司账户审批”中查看并批准或驳回。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'ko', N'[{{SITE_NM}}] 배치 작업 실패: {{JOB_NM}}', N'<p>배치 작업 <strong>{{JOB_NM}}</strong>({{JOB_ID}})이(가) 실패했습니다.</p><p>사유: {{RESULT_MSG}}</p><p>운영 > 스케줄 관리에서 실행 이력을 확인하세요. 같은 작업이 계속 실패해도 다시 성공할 때까지는 이 메일을 다시 보내지 않습니다.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'en', N'[{{SITE_NM}}] Scheduled job failed: {{JOB_NM}}', N'<p>The scheduled job <strong>{{JOB_NM}}</strong> ({{JOB_ID}}) failed.</p><p>Reason: {{RESULT_MSG}}</p><p>Check the run history in Operations > Schedules. If the job keeps failing, this mail is not sent again until it succeeds.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'zh-CN', N'[{{SITE_NM}}] 计划任务失败：{{JOB_NM}}', N'<p>计划任务 <strong>{{JOB_NM}}</strong>（{{JOB_ID}}）运行失败。</p><p>原因：{{RESULT_MSG}}</p><p>请在“运维 > 计划任务”中查看运行记录。在任务再次成功之前，即使继续失败也不会重复发送此邮件。</p><p><a href="{{LINK}}">{{LINK}}</a></p>')
) AS S ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY])
   ON T.[TEMPLATE_ID] = S.[TEMPLATE_ID] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY]) VALUES (S.[TEMPLATE_ID], S.[LANG_CD], S.[SUBJECT], S.[BODY]);
GO
