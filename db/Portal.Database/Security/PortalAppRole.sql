-- 애플리케이션 DB 계정이 속하는 역할. 테이블 권한 없이 SP 실행 권한만 가진다(설계서 8장).
-- 실제 로그인(gMSA 앱 풀 계정)은 환경별로 db/ops 스크립트에서 이 역할에 추가한다.
CREATE ROLE [portal_app] AUTHORIZATION [dbo];
GO
GRANT EXECUTE ON SCHEMA::[FW] TO [portal_app];
GO
GRANT EXECUTE ON SCHEMA::[LOG] TO [portal_app];
GO
-- TVP 형식 사용 권한 (스키마 단위)
GRANT REFERENCES ON SCHEMA::[FW] TO [portal_app];
GO
GRANT REFERENCES ON SCHEMA::[LOG] TO [portal_app];
GO
-- 로그는 위·변조 방지를 위해 애플리케이션 계정의 직접 변경을 막는다 (SP 소유권 체인으로만 INSERT)
DENY UPDATE, DELETE ON SCHEMA::[LOG] TO [portal_app];
GO
