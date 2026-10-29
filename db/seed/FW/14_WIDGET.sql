-- 초기 대시보드 위젯. 없는 행만 추가하고, 운영자가 화면에서 바꾼 설정은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

MERGE [FW].[TB_WIDGET] AS T
USING (VALUES
  -- 위젯, 유형, 데이터 SP, 권한 프로그램, 바로가기, 아이콘, 칸 수, 캐시(초), 정렬
  -- 첫 줄 숫자 카드 3개, 둘째 줄 최근 공지(8)·게시판 활동 차트(4), 셋째 줄 자료실·문의(6+6)
  (N'NOTI_UNREAD', N'CARD', N'FW.USP_WIDGET_NOTI_UNREAD', N'MY_NOTIFICATION', N'/My/Notification', N'bell', 4, 30, 10),
  (N'NOTICE_NEW', N'CARD', N'FW.USP_WIDGET_NOTICE_NEW', N'BRD_NOTICE', N'/Board/Notice', N'megaphone', 4, 300, 20),
  (N'MY_POST', N'CARD', N'FW.USP_WIDGET_MY_POST', NULL, NULL, N'pencil', 4, 300, 30),
  (N'NOTICE_RECENT', N'LIST', N'FW.USP_WIDGET_NOTICE_RECENT', N'BRD_NOTICE', N'/Board/Notice', N'board', 8, 60, 40),
  (N'BOARD_ACTIVITY', N'CHART', N'FW.USP_WIDGET_BOARD_ACTIVITY', NULL, NULL, N'chart', 4, 300, 50),
  (N'ARCHIVE_RECENT', N'LIST', N'FW.USP_WIDGET_ARCHIVE_RECENT', N'BRD_ARCHIVE', N'/Board/Archive', N'folder', 6, 300, 60),
  (N'QNA_RECENT', N'LIST', N'FW.USP_WIDGET_QNA_RECENT', N'BRD_QNA', N'/Board/Qna', N'comment', 6, 300, 70)
) AS S ([WIDGET_CD], [TYPE_CD], [DATA_SP], [PROGRAM_ID], [LINK_URL], [ICON_CD], [SPAN_COLS], [CACHE_SEC], [SORT_NO])
   ON T.[WIDGET_CD] = S.[WIDGET_CD]
WHEN NOT MATCHED THEN INSERT ([WIDGET_CD], [TYPE_CD], [DATA_SP], [PROGRAM_ID], [LINK_URL], [ICON_CD], [SPAN_COLS], [CACHE_SEC], [SORT_NO])
  VALUES (S.[WIDGET_CD], S.[TYPE_CD], S.[DATA_SP], S.[PROGRAM_ID], S.[LINK_URL], S.[ICON_CD], S.[SPAN_COLS], S.[CACHE_SEC], S.[SORT_NO]);
GO

-- 아이콘은 화면에서 바꾸지 않으므로, 아이콘 열이 생기기 전에 만든 위젯도 비어 있으면 채운다
UPDATE W SET W.[ICON_CD] = S.[ICON_CD]
  FROM [FW].[TB_WIDGET] W
  JOIN (VALUES (N'NOTI_UNREAD', N'bell'), (N'NOTICE_RECENT', N'board')) AS S ([WIDGET_CD], [ICON_CD]) ON S.[WIDGET_CD] = W.[WIDGET_CD]
 WHERE W.[ICON_CD] IS NULL;
GO

MERGE [FW].[TB_WIDGET_LANG] AS T
USING (VALUES
  (N'NOTICE_RECENT', N'ko', N'최근 공지사항'),
  (N'NOTICE_RECENT', N'en', N'Recent notices'),
  (N'NOTICE_RECENT', N'zh-CN', N'最新公告'),
  (N'NOTI_UNREAD', N'ko', N'안 읽은 알림'),
  (N'NOTI_UNREAD', N'en', N'Unread notifications'),
  (N'NOTI_UNREAD', N'zh-CN', N'未读通知'),
  (N'NOTICE_NEW', N'ko', N'이번 주 새 공지'),
  (N'NOTICE_NEW', N'en', N'New notices this week'),
  (N'NOTICE_NEW', N'zh-CN', N'本周新公告'),
  (N'MY_POST', N'ko', N'내가 쓴 글'),
  (N'MY_POST', N'en', N'My posts'),
  (N'MY_POST', N'zh-CN', N'我的帖子'),
  (N'BOARD_ACTIVITY', N'ko', N'최근 30일 게시판 활동'),
  (N'BOARD_ACTIVITY', N'en', N'Board activity (30 days)'),
  (N'BOARD_ACTIVITY', N'zh-CN', N'近30天版块动态'),
  (N'ARCHIVE_RECENT', N'ko', N'자료실 새 자료'),
  (N'ARCHIVE_RECENT', N'en', N'New in archive'),
  (N'ARCHIVE_RECENT', N'zh-CN', N'资料室新资料'),
  (N'QNA_RECENT', N'ko', N'최근 문의'),
  (N'QNA_RECENT', N'en', N'Recent questions'),
  (N'QNA_RECENT', N'zh-CN', N'最新咨询')
) AS S ([WIDGET_CD], [LANG_CD], [WIDGET_NM])
   ON T.[WIDGET_CD] = S.[WIDGET_CD] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([WIDGET_CD], [LANG_CD], [WIDGET_NM]) VALUES (S.[WIDGET_CD], S.[LANG_CD], S.[WIDGET_NM]);
GO
