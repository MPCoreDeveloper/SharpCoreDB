param(
    [string]$Host = "localhost",
    [int]$Port = 5433,
    [string]$Database = "master",
    [string]$Username = "admin",
    [string]$Password = "admin123",
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'psql-smoke.sql'
if (-not (Test-Path $scriptPath)) {
    throw "Smoke SQL file not found: $scriptPath"
}

$env:PGPASSWORD = $Password
try {
    & $PsqlPath "host=$Host port=$Port dbname=$Database user=$Username sslmode=require" -v ON_ERROR_STOP=1 -f $scriptPath
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
