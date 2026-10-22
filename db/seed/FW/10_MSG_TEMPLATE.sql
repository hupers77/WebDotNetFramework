-- 초기 데이터 (초기 데이터 정의서 7장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 치환 변수 {{USER_NM}} 등은 보낼 때 HTML 인코딩해 넣는다. {{SITE_NM}}(사이트명), {{LINK}}(로그인 주소)는 발송 작업이 채운다.

-- 메시지 템플릿
MERGE [FW].[TB_MSG_TEMPLATE] AS T
USING (VALUES
  (N'TPL_PARTNER_APPROVED', N'협력사 계정 승인 안내', N'Y', N'N'),
  (N'TPL_PARTNER_REJECTED', N'협력사 가입 반려 안내', N'Y', N'N')
) AS S ([TEMPLATE_ID], [TEMPLATE_NM], [MAIL_YN], [PORTAL_YN])
   ON T.[TEMPLATE_ID] = S.[TEMPLATE_ID]
WHEN NOT MATCHED THEN INSERT ([TEMPLATE_ID], [TEMPLATE_NM], [MAIL_YN], [PORTAL_YN]) VALUES (S.[TEMPLATE_ID], S.[TEMPLATE_NM], S.[MAIL_YN], S.[PORTAL_YN]);
GO

-- 템플릿 제목·본문 (3개 언어)
MERGE [FW].[TB_MSG_TEMPLATE_LANG] AS T
USING (VALUES
  (N'TPL_PARTNER_APPROVED', N'ko', N'[{{SITE_NM}}] 협력사 계정이 승인되었습니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>신청하신 협력사 계정 <strong>{{USER_ID}}</strong>이(가) 승인되었습니다. 신청할 때 정한 비밀번호로 로그인하세요.</p><p>처음 로그인하면 휴대폰 인증 앱(2단계 인증)을 등록합니다.</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_APPROVED', N'en', N'[{{SITE_NM}}] Your partner account has been approved', N'<p>Hello {{USER_NM}},</p><p>Your partner account <strong>{{USER_ID}}</strong> has been approved. Sign in with the password you chose when you applied.</p><p>At your first sign-in you will register an authenticator app on your phone (two-step verification).</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_APPROVED', N'zh-CN', N'[{{SITE_NM}}] 您的合作公司账户已获批准', N'<p>{{USER_NM}}，您好：</p><p>您申请的合作公司账户 <strong>{{USER_ID}}</strong> 已获批准。请使用申请时设置的密码登录。</p><p>首次登录时需要在手机上注册身份验证器应用（两步验证）。</p><p><a href="{{LINK}}">{{LINK}}</a></p>'),
  (N'TPL_PARTNER_REJECTED', N'ko', N'[{{SITE_NM}}] 협력사 계정 신청이 반려되었습니다', N'<p>{{USER_NM}}님, 안녕하세요.</p><p>신청하신 협력사 계정 <strong>{{USER_ID}}</strong>이(가) 반려되었습니다.</p><p>사유: {{REJECT_REASON}}</p><p>궁금한 점은 본사 담당자에게 문의해 주세요.</p>'),
  (N'TPL_PARTNER_REJECTED', N'en', N'[{{SITE_NM}}] Your partner account request was rejected', N'<p>Hello {{USER_NM}},</p><p>Your request for the partner account <strong>{{USER_ID}}</strong> was rejected.</p><p>Reason: {{REJECT_REASON}}</p><p>Please contact your headquarters representative if you have any questions.</p>'),
  (N'TPL_PARTNER_REJECTED', N'zh-CN', N'[{{SITE_NM}}] 您的合作公司账户申请已被驳回', N'<p>{{USER_NM}}，您好：</p><p>您申请的合作公司账户 <strong>{{USER_ID}}</strong> 已被驳回。</p><p>原因：{{REJECT_REASON}}</p><p>如有疑问，请联系总部负责人。</p>')
) AS S ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY])
   ON T.[TEMPLATE_ID] = S.[TEMPLATE_ID] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([TEMPLATE_ID], [LANG_CD], [SUBJECT], [BODY]) VALUES (S.[TEMPLATE_ID], S.[LANG_CD], S.[SUBJECT], S.[BODY]);
GO
