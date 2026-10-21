<#
.SYNOPSIS
  개발 PC에 로컬 DB를 만들고 스키마와 초기 데이터를 배포한다 (개발 표준 가이드 2장).

.EXAMPLE
  ./db/ops/local-setup.ps1 -Server "localhost" -Database "PORTAL_LOCAL"

.NOTES
  - Windows 통합 인증으로 접속한다. SQL 인증이 필요하면 -ConnectionString으로 전체 연결 문자열을 준다.
  - 여러 번 실행해도 된다. 바뀐 스키마만 반영하고 초기 데이터는 없는 행만 추가한다.
  - 통합 테스트도 이 DB를 쓴다: $env:PORTAL_TEST_DB 를 지정하지 않으면 PORTAL_LOCAL에 접속한다.
#>
param(
  [string]$Server = "localhost",
  [string]$Database = "PORTAL_LOCAL",
  [string]$ConnectionString = ""
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$project = Join-Path $root "db/Portal.Database/Portal.Database.sqlproj"
$profile = Join-Path $root "db/Portal.Database/Portal.Database.publish.xml"
$dacpac = Join-Path $root "db/Portal.Database/bin/Debug/Portal.Database.dacpac"

if (-not $ConnectionString) {
  $ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
}

Write-Host "1/3 도구 복원 (SqlPackage)"
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore 실패" }

Write-Host "2/3 DB 프로젝트 빌드 → DACPAC"
dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { throw "DB 프로젝트 빌드 실패" }

Write-Host "3/3 배포 (스키마 + 초기 데이터)"
dotnet sqlpackage /Action:Publish /SourceFile:$dacpac /Profile:$profile /TargetConnectionString:$ConnectionString
if ($LASTEXITCODE -ne 0) { throw "배포 실패" }

Write-Host "완료: $Database"
