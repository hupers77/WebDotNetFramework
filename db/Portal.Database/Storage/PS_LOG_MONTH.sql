-- 로그 파티션 스킴. 처음에는 모두 PRIMARY에 두고, 운영 스크립트가 오래된 파티션을 읽기 전용 파일그룹으로 옮긴다.
CREATE PARTITION SCHEME [PS_LOG_MONTH]
AS PARTITION [PF_LOG_MONTH] ALL TO ([PRIMARY]);
