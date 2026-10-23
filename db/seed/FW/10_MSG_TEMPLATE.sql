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
  (N'TPL_DORMANT_NOTICE', N'휴면 전환 사전 안내', N'Y', N'N', NULL),
  (N'TPL_PWD_EXPIRE', N'비밀번호 만료 사전 안내', N'Y', N'Y', N'MY_PWD_CHG'),
  (N'TPL_ACCOUNT_EXPIRE', N'협력사 계정 만료 사전 안내', N'Y', N'N', NULL),
  (N'TPL_JOB_FAILED', N'배치 작업 실패 알림', N'Y', N'Y', N'SYS_JOB_MGT'),
  (N'TPL_REPORT_DONE', N'레포트 백그라운드 생성 완료', N'N', N'Y', N'RPT_RUNS'),
  (N'TPL_REPORT_FAILED', N'레포트 백그라운드 생성 실패', N'N', N'Y', N'RPT_RUNS'),
  (N'TPL_BOARD_REPLY', N'내 글에 답글', N'N', N'Y', NULL),
  (N'TPL_BOARD_COMMENT', N'내 글에 댓글', N'N', N'Y', NULL)
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
  (N'TPL_DORMANT_NOTICE', N'ko', N'[{{SITE_NM}}] {{NOTICE_DAYS}}일 뒤 계정이 휴면으로 바뀝니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>계정 <strong>{{USER_ID}}</strong>으로 오랫동안 로그인하지 않으셨습니다. {{NOTICE_DAYS}}일 안에 로그인하지 않으면 계정이 휴면으로 바뀌고, 그 뒤에는 관리자가 해제해야 로그인할 수 있습니다.</p><p>계속 사용하시려면 아래 주소에서 로그인해 주세요.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_DORMANT_NOTICE', N'en', N'[{{SITE_NM}}] Your account will become dormant in {{NOTICE_DAYS}} days', N'<p>Hello {{USER_NM}},</p><p>You have not signed in to the account <strong>{{USER_ID}}</strong> for a long time. If you do not sign in within {{NOTICE_DAYS}} days, the account becomes dormant and an administrator must reactivate it before you can sign in.</p><p>To keep using it, sign in at the address below.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_DORMANT_NOTICE', N'zh-CN', N'[{{SITE_NM}}] 您的账户将在{{NOTICE_DAYS}}天后转为休眠', N'<p>{{USER_NM}}，您好：</p><p>您已长时间未使用账户 <strong>{{USER_ID}}</strong> 登录。如果{{NOTICE_DAYS}}天内仍未登录，该账户将转为休眠，之后需由管理员解除才能登录。</p><p>如需继续使用，请通过以下地址登录。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PWD_EXPIRE', N'ko', N'[{{SITE_NM}}] 비밀번호를 {{EXPIRE_DT}}까지 바꿔 주세요', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>계정 <strong>{{USER_ID}}</strong>의 비밀번호 사용 기간이 <strong>{{EXPIRE_DT}}</strong>({{DAYS_LEFT}}일 남음)에 끝납니다. 그 뒤에는 로그인할 때 비밀번호를 바꿔야 합니다.</p><p>포털에 로그인한 뒤 내 정보 > 비밀번호 변경에서 미리 바꿀 수 있습니다.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PWD_EXPIRE', N'en', N'[{{SITE_NM}}] Please change your password by {{EXPIRE_DT}}', N'<p>Hello {{USER_NM}},</p><p>The password of the account <strong>{{USER_ID}}</strong> expires on <strong>{{EXPIRE_DT}}</strong> ({{DAYS_LEFT}} days left). After that you must change it when you sign in.</p><p>You can change it now in My page > Change password after signing in.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PWD_EXPIRE', N'zh-CN', N'[{{SITE_NM}}] 请在{{EXPIRE_DT}}前修改密码', N'<p>{{USER_NM}}，您好：</p><p>账户 <strong>{{USER_ID}}</strong> 的密码将于 <strong>{{EXPIRE_DT}}</strong>（剩余{{DAYS_LEFT}}天）到期。到期后登录时必须修改密码。</p><p>登录后可在“我的信息 > 修改密码”中提前修改。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_ACCOUNT_EXPIRE', N'ko', N'[{{SITE_NM}}] 계정 사용 기한이 {{EXPIRE_DT}}에 끝납니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>계정 <strong>{{USER_ID}}</strong>의 사용 기한이 <strong>{{EXPIRE_DT}}</strong>({{DAYS_LEFT}}일 남음)에 끝납니다. 기한이 지나면 계정이 만료되어 로그인할 수 없습니다.</p><p>계약이 연장되었거나 계속 사용해야 하면 본사 담당자에게 기한 연장을 요청해 주세요.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_ACCOUNT_EXPIRE', N'en', N'[{{SITE_NM}}] Your account expires on {{EXPIRE_DT}}', N'<p>Hello {{USER_NM}},</p><p>The account <strong>{{USER_ID}}</strong> can be used until <strong>{{EXPIRE_DT}}</strong> ({{DAYS_LEFT}} days left). After that date the account expires and you cannot sign in.</p><p>If your contract was extended or you still need access, ask your contact at headquarters to extend the account.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_ACCOUNT_EXPIRE', N'zh-CN', N'[{{SITE_NM}}] 您的账户将于{{EXPIRE_DT}}到期', N'<p>{{USER_NM}}，您好：</p><p>账户 <strong>{{USER_ID}}</strong> 的使用期限将于 <strong>{{EXPIRE_DT}}</strong>（剩余{{DAYS_LEFT}}天）结束。到期后账户将失效，无法登录。</p><p>如合同已续签或仍需使用，请联系总部负责人延长使用期限。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'ko', N'[{{SITE_NM}}] 배치 작업 실패: {{JOB_NM}}', N'<p>배치 작업 <strong>{{JOB_NM}}</strong>({{JOB_ID}})이(가) 실패했습니다.</p><p>사유: {{RESULT_MSG}}</p><p>운영 > 스케줄 관리에서 실행 이력을 확인하세요. 같은 작업이 계속 실패해도 다시 성공할 때까지는 이 메일을 다시 보내지 않습니다.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'en', N'[{{SITE_NM}}] Scheduled job failed: {{JOB_NM}}', N'<p>The scheduled job <strong>{{JOB_NM}}</strong> ({{JOB_ID}}) failed.</p><p>Reason: {{RESULT_MSG}}</p><p>Check the run history in Operations > Schedules. If the job keeps failing, this mail is not sent again until it succeeds.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_JOB_FAILED', N'zh-CN', N'[{{SITE_NM}}] 计划任务失败：{{JOB_NM}}', N'<p>计划任务 <strong>{{JOB_NM}}</strong>（{{JOB_ID}}）运行失败。</p><p>原因：{{RESULT_MSG}}</p><p>请在“运维 > 计划任务”中查看运行记录。在任务再次成功之前，即使继续失败也不会重复发送此邮件。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_REPORT_DONE', N'ko', N'[{{SITE_NM}}] 레포트를 만들었습니다', N'<p>요청하신 레포트 <strong>{{REPORT_ID}}</strong>({{FILE_NM}})를 만들었습니다. 내 정보 > 내 레포트에서 내려받으세요.</p>'),
  (N'TPL_REPORT_DONE', N'en', N'[{{SITE_NM}}] Your report is ready', N'<p>The report <strong>{{REPORT_ID}}</strong> ({{FILE_NM}}) you requested is ready. Download it in My page > My reports.</p>'),
  (N'TPL_REPORT_DONE', N'zh-CN', N'[{{SITE_NM}}] 报表已生成', N'<p>您请求的报表 <strong>{{REPORT_ID}}</strong>（{{FILE_NM}}）已生成。请在“我的信息 > 我的报表”中下载。</p>'),
  (N'TPL_REPORT_FAILED', N'ko', N'[{{SITE_NM}}] 레포트를 만들지 못했습니다', N'<p>요청하신 레포트 <strong>{{REPORT_ID}}</strong>를 만들지 못했습니다. 내 정보 > 내 레포트에서 사유를 확인하고 조건을 바꿔 다시 요청하세요.</p>'),
  (N'TPL_REPORT_FAILED', N'en', N'[{{SITE_NM}}] Your report could not be built', N'<p>The report <strong>{{REPORT_ID}}</strong> you requested could not be built. Check the reason in My page > My reports, change the conditions and request again.</p>'),
  (N'TPL_REPORT_FAILED', N'zh-CN', N'[{{SITE_NM}}] 报表生成失败', N'<p>您请求的报表 <strong>{{REPORT_ID}}</strong> 生成失败。请在“我的信息 > 我的报表”中查看原因，调整条件后重新提交。</p>'),
  (N'TPL_BOARD_REPLY', N'ko', N'[{{SITE_NM}}] 내 글에 답글이 달렸습니다', N'<p>작성하신 글 <strong>{{POST_TITLE}}</strong>에 새 답글이 달렸습니다. 게시판에서 확인하세요.</p>'),
  (N'TPL_BOARD_REPLY', N'en', N'[{{SITE_NM}}] Someone replied to your post', N'<p>Your post <strong>{{POST_TITLE}}</strong> has a new reply. Check it on the board.</p>'),
  (N'TPL_BOARD_REPLY', N'zh-CN', N'[{{SITE_NM}}] 您的帖子有了新回复', N'<p>您发布的帖子 <strong>{{POST_TITLE}}</strong> 有了新回复。请在公告板中查看。</p>'),
  (N'TPL_BOARD_COMMENT', N'ko', N'[{{SITE_NM}}] 내 글에 댓글이 달렸습니다', N'<p>작성하신 글 <strong>{{POST_TITLE}}</strong>에 새 댓글이 달렸습니다. 게시판에서 확인하세요.</p>'),
  (N'TPL_BOARD_COMMENT', N'en', N'[{{SITE_NM}}] Someone commented on your post', N'<p>Your post <strong>{{POST_TITLE}}</strong> has a new comment. Check it on the board.</p>'),
  (N'TPL_BOARD_COMMENT', N'zh-CN', N'[{{SITE_NM}}] 您的帖子有了新评论', N'<p>您发布的帖子 <strong>{{POST_TITLE}}</strong> 有了新评论。请在公告板中查看。</p>')
) AS S ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY])
   ON T.[TEMPLATE_ID] = S.[TEMPLATE_ID] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY]) VALUES (S.[TEMPLATE_ID], S.[LANG_CD], S.[SUBJECT], S.[BODY]);
GO
