-- 배포 후 스크립트: 초기 데이터를 순서대로 넣는다 (SqlPackage 배포마다 실행, 여러 번 실행해도 결과가 같다).
-- 새 시드 파일은 db/seed/<스키마>/에 두고 여기에 :r 한 줄을 추가한다.
SET NOCOUNT ON;
GO
:r ../../seed/FW/01_LANG.sql
:r ../../seed/FW/02_COMPANY.sql
:r ../../seed/FW/03_ROLE.sql
:r ../../seed/FW/04_CODE.sql
:r ../../seed/FW/05_CONFIG.sql
:r ../../seed/FW/06_PROGRAM.sql
:r ../../seed/FW/07_MENU.sql
:r ../../seed/FW/08_LANG_RESOURCE.sql
:r ../../seed/FW/09_CACHE_LOG.sql
