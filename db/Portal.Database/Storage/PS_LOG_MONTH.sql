-- 로그 파티션 스킴. 모든 파티션을 PRIMARY에 둔다(읽기 전용 파일그룹으로 옮기지 않는다, 2026-10-23 결정).
CREATE PARTITION SCHEME [PS_LOG_MONTH]
AS PARTITION [PF_LOG_MONTH] ALL TO ([PRIMARY]);
