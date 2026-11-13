# Runs the backend test suite against the Neon database instead of the local docker Postgres.
#
# Why a script: PostgresFixture reads TEST_DATABASE_ADMIN as a key=value connection string, not a URI, and it
# creates / drops a throwaway database per test class. Pointing it at Neon means every run creates and drops
# ~78 databases on the shared branch, which is fine for a dev database but must never point at production.
#
# Usage:
#   .\tests\run-tests-neon.ps1                          # whole suite
#   .\tests\run-tests-neon.ps1 -Filter Phase0           # one slice
#   .\tests\run-tests-neon.ps1 -NoBuild                 # reuse the last build

param(
    [string]$Filter = "",
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$appsettings = Join-Path $repo 'src\VehicleRental.WebApi\appsettings.json'

if (-not (Test-Path $appsettings)) { throw "Cannot find $appsettings" }
$cfg = Get-Content $appsettings -Raw | ConvertFrom-Json
$url = $cfg.ConnectionStrings.RentalDatabase
if (-not $url) { throw "ConnectionStrings:RentalDatabase is not set in appsettings.json" }

$uri = [uri]$url
$userInfo = $uri.UserInfo -split ':', 2
$database = $uri.AbsolutePath.Trim('/')

$connString = "Host={0};Port=5432;Database={1};Username={2};Password={3};SSL Mode=Require" -f `
    $uri.Host, $database, $userInfo[0], $userInfo[1]

Write-Host "Neon target : $($uri.Host)/$database" -ForegroundColor Cyan
if ($uri.Host -match 'neon\.tech' -and $uri.Host -notmatch 'pooler') {
    Write-Host "Note: using the direct endpoint, not the pooler." -ForegroundColor DarkGray
}
Write-Host "The suite creates and drops one throwaway database per test class on this server." -ForegroundColor Yellow

$env:TEST_DATABASE_ADMIN = $connString

$args = @('test', '--nologo')
if ($Filter) { $args += @('--filter', "FullyQualifiedName~$Filter") }
if ($NoBuild) { $args += '--no-build' }

Push-Location $repo
try {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @args
    $code = $LASTEXITCODE
    $sw.Stop()
    Write-Host ("elapsed: {0:N1}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray
    exit $code
}
finally {
    Pop-Location
    Remove-Item Env:\TEST_DATABASE_ADMIN -ErrorAction SilentlyContinue
}
