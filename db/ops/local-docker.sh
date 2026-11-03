#!/usr/bin/env bash
# 맥/리눅스의 docker SQL Server에 로컬 개발 DB(PORTAL_LOCAL)를 만들고 배포·실행한다.
# 같은 서버의 다른 DB(예: CMMS)는 건드리지 않는다. 새 DB 하나(PORTAL_LOCAL)만 만든다.
#
# 사용법 (저장소 루트에서):
#   export SA_PASSWORD='컨테이너 SA 비밀번호'
#   export MSSQL_CONTAINER=컨테이너이름        # docker ps 로 확인. 생략하면 1433 포트를 쓰는 컨테이너를 찾는다
#   ./db/ops/local-docker.sh setup             # DB 생성 + 스키마/초기 데이터 배포 (여러 번 실행해도 됨)
#   ./db/ops/local-docker.sh admin             # 관리자 sysadmin 생성(또는 비밀번호 재설정), 임시 비밀번호 출력
#   ./db/ops/local-docker.sh nomfa             # 로컬 확인용: 관리자 2단계 인증(AUTH.ADMIN_MFA)을 끈다
#   ./db/ops/local-docker.sh run               # https://localhost:5001 로 실행
#   ./db/ops/local-docker.sh test              # 통합 테스트(DB 포함)를 PORTAL_LOCAL 로 실행
#   ./db/ops/local-docker.sh all               # setup + admin + nomfa 후 run
#
# 환경변수: SA_PASSWORD(필수), MSSQL_CONTAINER, MSSQL_HOST(기본 localhost), MSSQL_PORT(기본 1433), PORTAL_DB(기본 PORTAL_LOCAL)
set -euo pipefail

cd "$(dirname "$0")/../.."
HOST="${MSSQL_HOST:-localhost}"
PORT="${MSSQL_PORT:-1433}"
DB="${PORTAL_DB:-PORTAL_LOCAL}"
: "${SA_PASSWORD:?SA_PASSWORD 환경변수를 지정하세요}"
CONN="Server=${HOST},${PORT};Database=${DB};User Id=sa;Password=${SA_PASSWORD};Encrypt=True;TrustServerCertificate=True"

container() {
  if [[ -n "${MSSQL_CONTAINER:-}" ]]; then echo "$MSSQL_CONTAINER"; return; fi
  docker ps --filter "publish=${PORT}" --format '{{.Names}}' | head -n1
}

# 컨테이너 안의 sqlcmd로 쿼리를 실행한다 (도구 경로가 이미지마다 달라 둘 다 찾는다)
sql() {
  local c; c="$(container)"
  [[ -n "$c" ]] || { echo "SQL Server 컨테이너를 찾지 못했습니다. MSSQL_CONTAINER 를 지정하세요." >&2; exit 1; }
  local tool
  tool="$(docker exec "$c" sh -c 'ls /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd 2>/dev/null | head -n1')"
  [[ -n "$tool" ]] || { echo "컨테이너에서 sqlcmd 를 찾지 못했습니다." >&2; exit 1; }
  docker exec -e SQLCMDPASSWORD="$SA_PASSWORD" "$c" "$tool" -S localhost -U sa -C -b -d "${2:-master}" -Q "$1"
}

setup() {
  echo "1/4 DB 만들기: ${DB} (이미 있으면 그대로 둠)"
  sql "IF DB_ID(N'${DB}') IS NULL CREATE DATABASE [${DB}] COLLATE Korean_Wansung_CI_AS;"
  echo "2/4 도구 복원"; dotnet tool restore
  echo "3/4 DB 프로젝트 빌드"; dotnet build db/Portal.Database/Portal.Database.sqlproj -c Debug
  echo "4/4 배포 (스키마 + 초기 데이터)"
  dotnet sqlpackage /Action:Publish /SourceFile:db/Portal.Database/bin/Debug/Portal.Database.dacpac \
    /Profile:db/Portal.Database/Portal.Database.publish.xml /TargetConnectionString:"$CONN"
  echo "완료: ${DB}"
}

app() { ConnectionStrings__Portal="$CONN" ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Portal.Web -- "$@"; }

case "${1:-}" in
  setup) setup ;;
  admin) app init-admin ;;
  nomfa) sql "UPDATE FW.TB_CONFIG SET CONFIG_VAL = N'N' WHERE CONFIG_KEY = N'AUTH.ADMIN_MFA';" "$DB"; echo "관리자 2단계 인증을 껐습니다(로컬 확인용)." ;;
  run)   ASPNETCORE_URLS="https://localhost:5001" app ;;
  test)  PORTAL_TEST_DB="$CONN" dotnet test tests/Portal.IntegrationTests -c Debug ;;
  all)   setup; app init-admin; "$0" nomfa; ASPNETCORE_URLS="https://localhost:5001" app ;;
  *) sed -n '2,16p' "$0"; exit 1 ;;
esac
