-- 초기 대시보드 위젯. 없는 행만 추가하고, 운영자가 화면에서 바꾼 설정은 덮어쓰지 않는다.
-- 여러 번 실행해도 결과가 같다. 이 파일을 바꾸면 docs/initial-data.md도 같은 변경에서 고친다.

MERGE [FW].[TB_WIDGET] AS T
USING (VALUES
  -- 위젯, 유형, 데이터 SP, 권한 프로그램, 바로가기, 칸 수, 캐시(초), 정렬
  (N'NOTICE_RECENT', N'LIST', N'FW.USP_WIDGET_NOTICE_RECENT', N'BRD_NOTICE', N'/Board/Notice', 8, 60, 10),
  (N'NOTI_UNREAD', N'CARD', N'FW.USP_WIDGET_NOTI_UNREAD', N'MY_NOTIFICATION', N'/My/Notification', 4, 30, 20)
) AS S ([WIDGET_CD], [TYPE_CD], [DATA_SP], [PROGRAM_ID], [LINK_URL], [SPAN_COLS], [CACHE_SEC], [SORT_NO])
   ON T.[WIDGET_CD] = S.[WIDGET_CD]
WHEN NOT MATCHED THEN INSERT ([WIDGET_CD], [TYPE_CD], [DATA_SP], [PROGRAM_ID], [LINK_URL], [SPAN_COLS], [CACHE_SEC], [SORT_NO])
  VALUES (S.[WIDGET_CD], S.[TYPE_CD], S.[DATA_SP], S.[PROGRAM_ID], S.[LINK_URL], S.[SPAN_COLS], S.[CACHE_SEC], S.[SORT_NO]);
GO

MERGE [FW].[TB_WIDGET_LANG] AS T
USING (VALUES
  (N'NOTICE_RECENT', N'ko', N'최근 공지사항'),
  (N'NOTICE_RECENT', N'en', N'Recent notices'),
  (N'NOTICE_RECENT', N'zh-CN', N'最新公告'),
  (N'NOTI_UNREAD', N'ko', N'안 읽은 알림'),
  (N'NOTI_UNREAD', N'en', N'Unread notifications'),
  (N'NOTI_UNREAD', N'zh-CN', N'未读通知')
) AS S ([WIDGET_CD], [LANG_CD], [WIDGET_NM])
   ON T.[WIDGET_CD] = S.[WIDGET_CD] AND T.[LANG_CD] = S.[LANG_CD]
WHEN NOT MATCHED THEN INSERT ([WIDGET_CD], [LANG_CD], [WIDGET_NM]) VALUES (S.[WIDGET_CD], S.[LANG_CD], S.[WIDGET_NM]);
GO
