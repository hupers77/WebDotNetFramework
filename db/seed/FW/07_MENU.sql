-- 초기 데이터 (초기 데이터 정의서 4장). 없는 행만 추가하고, 운영자가 화면에서 바꾼 값은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

-- 메뉴는 MENU_KEY로 식별한다. 부모는 키로 찾으므로 상위 메뉴를 먼저 넣는다.

-- 최상위 메뉴
MERGE [FW].[TB_MENU] AS T
USING (
  SELECT V.[MENU_KEY], P.[MENU_ID] AS [PARENT_MENU_ID], V.[MENU_TYPE_CD], V.[PROGRAM_ID], V.[OPEN_SCOPE_CD], V.[ICON_CD], V.[SORT_NO]
    FROM (VALUES
  (N'HOME', NULL, N'PAGE', N'DSH_HOME', N'ALL', N'home', 10),
  (N'MY', NULL, N'FOLDER', NULL, N'ALL', N'user', 20),
  (N'BRD', NULL, N'FOLDER', NULL, N'ALL', N'board', 30),
  (N'PTN', NULL, N'FOLDER', NULL, N'PARTNER', N'partner', 40),
  (N'SYS', NULL, N'FOLDER', NULL, N'EMP', N'setting', 50),
  (N'INTG', NULL, N'FOLDER', NULL, N'EMP', N'link', 60),
  (N'OPS', NULL, N'FOLDER', NULL, N'EMP', N'ops', 70),
  (N'LOG', NULL, N'FOLDER', NULL, N'EMP', N'log', 80)
    ) AS V ([MENU_KEY], [PARENT_KEY], [MENU_TYPE_CD], [PROGRAM_ID], [OPEN_SCOPE_CD], [ICON_CD], [SORT_NO])
    LEFT JOIN [FW].[TB_MENU] P ON P.[MENU_KEY] = V.[PARENT_KEY]
) AS S
   ON T.[MENU_KEY] = S.[MENU_KEY]
WHEN NOT MATCHED THEN
  INSERT ([MENU_KEY], [MENU_SET_CD], [PARENT_MENU_ID], [MENU_TYPE_CD], [PROGRAM_ID], [OPEN_SCOPE_CD], [ICON_CD], [SORT_NO])
  VALUES (S.[MENU_KEY], 'PORTAL', S.[PARENT_MENU_ID], S.[MENU_TYPE_CD], S.[PROGRAM_ID], S.[OPEN_SCOPE_CD], S.[ICON_CD], S.[SORT_NO]);
GO

-- 하위 메뉴
MERGE [FW].[TB_MENU] AS T
USING (
  SELECT V.[MENU_KEY], P.[MENU_ID] AS [PARENT_MENU_ID], V.[MENU_TYPE_CD], V.[PROGRAM_ID], V.[OPEN_SCOPE_CD], V.[ICON_CD], V.[SORT_NO]
    FROM (VALUES
  (N'MY_PROFILE', N'MY', N'PAGE', N'MY_PROFILE', N'ALL', NULL, 10),
  (N'MY_PWD_CHG', N'MY', N'PAGE', N'MY_PWD_CHG', N'ALL', NULL, 20),
  (N'MY_NOTIFICATION', N'MY', N'PAGE', N'MY_NOTIFICATION', N'ALL', NULL, 30),
  (N'RPT_RUNS', N'MY', N'PAGE', N'RPT_RUNS', N'ALL', NULL, 40),
  (N'BRD_NOTICE', N'BRD', N'PAGE', N'BRD_NOTICE', N'ALL', NULL, 10),
  (N'BRD_ARCHIVE', N'BRD', N'PAGE', N'BRD_ARCHIVE', N'ALL', NULL, 20),
  (N'BRD_QNA', N'BRD', N'PAGE', N'BRD_QNA', N'ALL', NULL, 30),
  (N'BRD_EMP_FREE', N'BRD', N'PAGE', N'BRD_EMP_FREE', N'EMP', NULL, 40),
  (N'PTN_USER_MGT', N'PTN', N'PAGE', N'PTN_USER_MGT', N'PARTNER', NULL, 10),
  (N'SYS_USER_MGT', N'SYS', N'PAGE', N'SYS_USER_MGT', N'EMP', NULL, 10),
  (N'SYS_PARTNER_APPROVE', N'SYS', N'PAGE', N'SYS_PARTNER_APPROVE', N'EMP', NULL, 20),
  (N'SYS_COMPANY_MGT', N'SYS', N'PAGE', N'SYS_COMPANY_MGT', N'EMP', NULL, 30),
  (N'SYS_DEPT_MGT', N'SYS', N'PAGE', N'SYS_DEPT_MGT', N'EMP', NULL, 40),
  (N'SYS_ROLE_MGT', N'SYS', N'PAGE', N'SYS_ROLE_MGT', N'EMP', NULL, 50),
  (N'SYS_ROLE_AUTH_MGT', N'SYS', N'PAGE', N'SYS_ROLE_AUTH_MGT', N'EMP', NULL, 60),
  (N'SYS_USER_AUTH_MGT', N'SYS', N'PAGE', N'SYS_USER_AUTH_MGT', N'EMP', NULL, 65),
  (N'SYS_PROGRAM_MGT', N'SYS', N'PAGE', N'SYS_PROGRAM_MGT', N'EMP', NULL, 70),
  (N'SYS_MENU_MGT', N'SYS', N'PAGE', N'SYS_MENU_MGT', N'EMP', NULL, 80),
  (N'SYS_CODE_MGT', N'SYS', N'PAGE', N'SYS_CODE_MGT', N'EMP', NULL, 90),
  (N'SYS_LANG_MGT', N'SYS', N'PAGE', N'SYS_LANG_MGT', N'EMP', NULL, 100),
  (N'SYS_CONFIG_MGT', N'SYS', N'PAGE', N'SYS_CONFIG_MGT', N'EMP', NULL, 110),
  (N'SYS_HOLIDAY_MGT', N'SYS', N'PAGE', N'SYS_HOLIDAY_MGT', N'EMP', NULL, 120),
  (N'BRD_BOARD_MGT', N'SYS', N'PAGE', N'BRD_BOARD_MGT', N'EMP', NULL, 130),
  (N'SYS_POPUP_MGT', N'SYS', N'PAGE', N'SYS_POPUP_MGT', N'EMP', NULL, 140),
  (N'SYS_MSG_TEMPLATE_MGT', N'SYS', N'PAGE', N'SYS_MSG_TEMPLATE_MGT', N'EMP', NULL, 150),
  (N'DSH_WIDGET_MGT', N'SYS', N'PAGE', N'DSH_WIDGET_MGT', N'EMP', NULL, 160),
  (N'RPT_REPORT_MGT', N'SYS', N'PAGE', N'RPT_REPORT_MGT', N'EMP', NULL, 170),
  (N'SYS_API_CLIENT_MGT', N'INTG', N'PAGE', N'SYS_API_CLIENT_MGT', N'EMP', NULL, 10),
  (N'SYS_API_OUTBOUND_MGT', N'INTG', N'PAGE', N'SYS_API_OUTBOUND_MGT', N'EMP', NULL, 20),
  (N'SYS_JOB_MGT', N'OPS', N'PAGE', N'SYS_JOB_MGT', N'EMP', NULL, 10),
  (N'SYS_MONITOR_INQ', N'OPS', N'PAGE', N'SYS_MONITOR_INQ', N'EMP', NULL, 20),
  (N'LOG_ACCESS_INQ', N'LOG', N'PAGE', N'LOG_ACCESS_INQ', N'EMP', NULL, 10),
  (N'LOG_AUDIT_INQ', N'LOG', N'PAGE', N'LOG_AUDIT_INQ', N'EMP', NULL, 20),
  (N'LOG_PII_INQ', N'LOG', N'PAGE', N'LOG_PII_INQ', N'EMP', NULL, 30),
  (N'RPT_LOG_MONTHLY', N'LOG', N'PAGE', N'RPT_LOG_MONTHLY', N'EMP', NULL, 40)
    ) AS V ([MENU_KEY], [PARENT_KEY], [MENU_TYPE_CD], [PROGRAM_ID], [OPEN_SCOPE_CD], [ICON_CD], [SORT_NO])
    LEFT JOIN [FW].[TB_MENU] P ON P.[MENU_KEY] = V.[PARENT_KEY]
) AS S
   ON T.[MENU_KEY] = S.[MENU_KEY]
WHEN NOT MATCHED THEN
  INSERT ([MENU_KEY], [MENU_SET_CD], [PARENT_MENU_ID], [MENU_TYPE_CD], [PROGRAM_ID], [OPEN_SCOPE_CD], [ICON_CD], [SORT_NO])
  VALUES (S.[MENU_KEY], 'PORTAL', S.[PARENT_MENU_ID], S.[MENU_TYPE_CD], S.[PROGRAM_ID], S.[OPEN_SCOPE_CD], S.[ICON_CD], S.[SORT_NO]);
GO

-- 메뉴명
MERGE [FW].[TB_MENU_LANG] AS T
USING (
  SELECT M.[MENU_ID], V.[LANG_CD], V.[MENU_NM]
    FROM (VALUES
  (N'HOME', N'ko', N'홈'),
  (N'HOME', N'en', N'Home'),
  (N'HOME', N'zh-CN', N'首页'),
  (N'MY', N'ko', N'내 정보'),
  (N'MY', N'en', N'My page'),
  (N'MY', N'zh-CN', N'我的信息'),
  (N'MY_PROFILE', N'ko', N'내 정보'),
  (N'MY_PROFILE', N'en', N'My profile'),
  (N'MY_PROFILE', N'zh-CN', N'个人信息'),
  (N'MY_PWD_CHG', N'ko', N'비밀번호 변경'),
  (N'MY_PWD_CHG', N'en', N'Change password'),
  (N'MY_PWD_CHG', N'zh-CN', N'修改密码'),
  (N'MY_NOTIFICATION', N'ko', N'알림함'),
  (N'MY_NOTIFICATION', N'en', N'Notifications'),
  (N'MY_NOTIFICATION', N'zh-CN', N'通知'),
  (N'RPT_RUNS', N'ko', N'내 레포트'),
  (N'RPT_RUNS', N'en', N'My reports'),
  (N'RPT_RUNS', N'zh-CN', N'我的报表'),
  (N'BRD', N'ko', N'게시판'),
  (N'BRD', N'en', N'Boards'),
  (N'BRD', N'zh-CN', N'公告板'),
  (N'BRD_NOTICE', N'ko', N'공지사항'),
  (N'BRD_NOTICE', N'en', N'Notices'),
  (N'BRD_NOTICE', N'zh-CN', N'公告'),
  (N'BRD_ARCHIVE', N'ko', N'자료실'),
  (N'BRD_ARCHIVE', N'en', N'Archive'),
  (N'BRD_ARCHIVE', N'zh-CN', N'资料室'),
  (N'BRD_QNA', N'ko', N'FAQ·문의'),
  (N'BRD_QNA', N'en', N'FAQ & Q&A'),
  (N'BRD_QNA', N'zh-CN', N'常见问题与咨询'),
  (N'BRD_EMP_FREE', N'ko', N'사내 게시판'),
  (N'BRD_EMP_FREE', N'en', N'Internal board'),
  (N'BRD_EMP_FREE', N'zh-CN', N'内部公告板'),
  (N'PTN', N'ko', N'협력사'),
  (N'PTN', N'en', N'Partner'),
  (N'PTN', N'zh-CN', N'合作公司'),
  (N'PTN_USER_MGT', N'ko', N'협력사 사용자 관리'),
  (N'PTN_USER_MGT', N'en', N'Partner users'),
  (N'PTN_USER_MGT', N'zh-CN', N'合作公司用户管理'),
  (N'SYS', N'ko', N'시스템 관리'),
  (N'SYS', N'en', N'System'),
  (N'SYS', N'zh-CN', N'系统管理'),
  (N'SYS_USER_MGT', N'ko', N'사용자 관리'),
  (N'SYS_USER_MGT', N'en', N'Users'),
  (N'SYS_USER_MGT', N'zh-CN', N'用户管理'),
  (N'SYS_PARTNER_APPROVE', N'ko', N'협력사 계정 승인'),
  (N'SYS_PARTNER_APPROVE', N'en', N'Partner approval'),
  (N'SYS_PARTNER_APPROVE', N'zh-CN', N'合作公司账户审批'),
  (N'SYS_COMPANY_MGT', N'ko', N'회사 관리'),
  (N'SYS_COMPANY_MGT', N'en', N'Companies'),
  (N'SYS_COMPANY_MGT', N'zh-CN', N'公司管理'),
  (N'SYS_DEPT_MGT', N'ko', N'부서 관리'),
  (N'SYS_DEPT_MGT', N'en', N'Departments'),
  (N'SYS_DEPT_MGT', N'zh-CN', N'部门管理'),
  (N'SYS_ROLE_MGT', N'ko', N'역할 관리'),
  (N'SYS_ROLE_MGT', N'en', N'Roles'),
  (N'SYS_ROLE_MGT', N'zh-CN', N'角色管理'),
  (N'SYS_ROLE_AUTH_MGT', N'ko', N'역할별 권한'),
  (N'SYS_ROLE_AUTH_MGT', N'en', N'Role permissions'),
  (N'SYS_ROLE_AUTH_MGT', N'zh-CN', N'角色权限'),
  (N'SYS_USER_AUTH_MGT', N'ko', N'사용자별 예외 권한'),
  (N'SYS_USER_AUTH_MGT', N'en', N'User permission exceptions'),
  (N'SYS_USER_AUTH_MGT', N'zh-CN', N'用户例外权限'),
  (N'SYS_PROGRAM_MGT', N'ko', N'프로그램 관리'),
  (N'SYS_PROGRAM_MGT', N'en', N'Programs'),
  (N'SYS_PROGRAM_MGT', N'zh-CN', N'程序管理'),
  (N'SYS_MENU_MGT', N'ko', N'메뉴 관리'),
  (N'SYS_MENU_MGT', N'en', N'Menus'),
  (N'SYS_MENU_MGT', N'zh-CN', N'菜单管理'),
  (N'SYS_CODE_MGT', N'ko', N'공통코드'),
  (N'SYS_CODE_MGT', N'en', N'Common codes'),
  (N'SYS_CODE_MGT', N'zh-CN', N'公共代码'),
  (N'SYS_LANG_MGT', N'ko', N'다국어 관리'),
  (N'SYS_LANG_MGT', N'en', N'Translations'),
  (N'SYS_LANG_MGT', N'zh-CN', N'多语言管理'),
  (N'SYS_CONFIG_MGT', N'ko', N'설정 관리'),
  (N'SYS_CONFIG_MGT', N'en', N'Settings'),
  (N'SYS_CONFIG_MGT', N'zh-CN', N'设置管理'),
  (N'SYS_HOLIDAY_MGT', N'ko', N'휴일·달력'),
  (N'SYS_HOLIDAY_MGT', N'en', N'Holidays'),
  (N'SYS_HOLIDAY_MGT', N'zh-CN', N'节假日'),
  (N'BRD_BOARD_MGT', N'ko', N'게시판 관리'),
  (N'BRD_BOARD_MGT', N'en', N'Board settings'),
  (N'BRD_BOARD_MGT', N'zh-CN', N'公告板管理'),
  (N'SYS_POPUP_MGT', N'ko', N'공지 팝업'),
  (N'SYS_POPUP_MGT', N'en', N'Notice popups'),
  (N'SYS_POPUP_MGT', N'zh-CN', N'公告弹窗'),
  (N'SYS_MSG_TEMPLATE_MGT', N'ko', N'메시지 템플릿'),
  (N'SYS_MSG_TEMPLATE_MGT', N'en', N'Message templates'),
  (N'SYS_MSG_TEMPLATE_MGT', N'zh-CN', N'消息模板'),
  (N'DSH_WIDGET_MGT', N'ko', N'위젯 관리'),
  (N'DSH_WIDGET_MGT', N'en', N'Widgets'),
  (N'DSH_WIDGET_MGT', N'zh-CN', N'小组件管理'),
  (N'RPT_REPORT_MGT', N'ko', N'레포트 관리'),
  (N'RPT_REPORT_MGT', N'en', N'Reports'),
  (N'RPT_REPORT_MGT', N'zh-CN', N'报表管理'),
  (N'INTG', N'ko', N'연계 관리'),
  (N'INTG', N'en', N'Integration'),
  (N'INTG', N'zh-CN', N'集成管理'),
  (N'SYS_API_CLIENT_MGT', N'ko', N'API 클라이언트'),
  (N'SYS_API_CLIENT_MGT', N'en', N'API clients'),
  (N'SYS_API_CLIENT_MGT', N'zh-CN', N'API客户端'),
  (N'SYS_API_OUTBOUND_MGT', N'ko', N'송신 대상'),
  (N'SYS_API_OUTBOUND_MGT', N'en', N'Outbound targets'),
  (N'SYS_API_OUTBOUND_MGT', N'zh-CN', N'外发目标'),
  (N'OPS', N'ko', N'운영'),
  (N'OPS', N'en', N'Operations'),
  (N'OPS', N'zh-CN', N'运维'),
  (N'SYS_JOB_MGT', N'ko', N'스케줄 관리'),
  (N'SYS_JOB_MGT', N'en', N'Schedules'),
  (N'SYS_JOB_MGT', N'zh-CN', N'计划任务'),
  (N'SYS_MONITOR_INQ', N'ko', N'운영 모니터링'),
  (N'SYS_MONITOR_INQ', N'en', N'Monitoring'),
  (N'SYS_MONITOR_INQ', N'zh-CN', N'运行监控'),
  (N'LOG', N'ko', N'로그'),
  (N'LOG', N'en', N'Logs'),
  (N'LOG', N'zh-CN', N'日志'),
  (N'LOG_ACCESS_INQ', N'ko', N'로그인·접근 기록'),
  (N'LOG_ACCESS_INQ', N'en', N'Login & access log'),
  (N'LOG_ACCESS_INQ', N'zh-CN', N'登录与访问记录'),
  (N'LOG_AUDIT_INQ', N'ko', N'데이터 변경 기록'),
  (N'LOG_AUDIT_INQ', N'en', N'Data change log'),
  (N'LOG_AUDIT_INQ', N'zh-CN', N'数据变更记录'),
  (N'LOG_PII_INQ', N'ko', N'개인정보 접속기록'),
  (N'LOG_PII_INQ', N'en', N'Personal data access log'),
  (N'LOG_PII_INQ', N'zh-CN', N'个人信息访问记录'),
  (N'RPT_LOG_MONTHLY', N'ko', N'월별 접속기록 점검'),
  (N'RPT_LOG_MONTHLY', N'en', N'Monthly access log review'),
  (N'RPT_LOG_MONTHLY', N'zh-CN', N'月度访问记录检查')
    ) AS V ([MENU_KEY], [LANG_CD], [MENU_NM])
    JOIN [FW].[TB_MENU] M ON M.[MENU_KEY] = V.[MENU_KEY]
) AS S
   ON T.[MENU_ID] = S.[MENU_ID] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([MENU_ID], [LANG_CD], [MENU_NM]) VALUES (S.[MENU_ID], S.[LANG_CD], S.[MENU_NM]);
GO
