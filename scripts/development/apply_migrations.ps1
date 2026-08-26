<#
.SYNOPSIS
    Applies the .sql migrations in a folder to a MySQL database running in a Docker container.

.DESCRIPTION
    Files are applied in version order (V01__, V02__, ... V10__), one at a time.
    If a migration fails the run stops immediately, because later migrations
    normally assume the earlier ones succeeded.

    This script does not track which migrations have already run - every file is
    re-applied on every run, so every migration must be safe to re-run.

    On MySQL 8, CREATE TABLE IF NOT EXISTS is the tool that gives you this.
    Declare indexes inline in the CREATE TABLE body (PRIMARY KEY / UNIQUE KEY /
    KEY) so they are covered by the same IF NOT EXISTS - MySQL 8 has no
    CREATE INDEX IF NOT EXISTS, and a bare CREATE INDEX fails on the second run.

    Adding a column to an existing table has no safe-to-re-run form on MySQL 8
    (ALTER TABLE ... ADD COLUMN IF NOT EXISTS is MariaDB syntax and is a syntax
    error here). Until this script tracks applied migrations, column additions
    have to be applied by hand, once.

    TODO: track applied migrations in a schema_migrations table - create it if
    absent, SELECT the applied filenames before the loop, skip files already
    listed, INSERT each filename after it succeeds. That removes the
    re-runnability constraint above entirely.

.EXAMPLE
    $env:MYSQL_PASSWORD = 'CustomerLocalDev!23'
    .\apply_migrations.ps1 -Database customerdb -User customer_svc

.EXAMPLE
    .\apply_migrations.ps1 -Database jobdb -User job_svc -Password 'JobLocalDev!23'
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Database,

    [Parameter(Mandatory = $true)]
    [string]$User,

    [string]$Password = $env:MYSQL_PASSWORD,

    [string]$MigrationsPath = (Join-Path $PSScriptRoot '..\..\database\migrations'),

    [string]$Container = 'assms-mysql'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Password)) {
    Write-Host "No password supplied. Pass -Password or set the MYSQL_PASSWORD environment variable." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $MigrationsPath)) {
    Write-Host "Migrations folder not found: $MigrationsPath" -ForegroundColor Red
    exit 1
}

# Sort on the number extracted from the V<n>__ prefix, so V2 runs before V10
# regardless of whether the version numbers are zero-padded.
$files = @(
    Get-ChildItem -Path $MigrationsPath -Filter *.sql |
        Sort-Object @{ Expression = { if ($_.Name -match '^V(\d+)__') { [int]$Matches[1] } else { [int]::MaxValue } } }, Name
)

if ($files.Count -eq 0) {
    Write-Host "No .sql files found in $MigrationsPath" -ForegroundColor Yellow
    exit 0
}

Write-Host "Applying $($files.Count) migration(s) to '$Database' in container '$Container'..." -ForegroundColor Cyan
Write-Host ""

$applied = @()
$failedFile = $null
$failedCode = 0

foreach ($file in $files) {
    Write-Host "  -> $($file.Name)"

    Get-Content $file.FullName | docker exec -i -e "MYSQL_PWD=$Password" $Container mysql -u $User $Database

    if ($LASTEXITCODE -ne 0) {
        $failedFile = $file.Name
        $failedCode = $LASTEXITCODE
        break
    }

    $applied += $file.Name
}

Write-Host ""
Write-Host "----- Summary -----"

if ($applied.Count -gt 0) {
    Write-Host "Applied ($($applied.Count)):" -ForegroundColor Green
    foreach ($name in $applied) {
        Write-Host "  $name" -ForegroundColor Green
    }
} else {
    Write-Host "Applied: none"
}

if ($failedFile) {
    Write-Host "FAILED on: $failedFile (mysql exit code $failedCode)" -ForegroundColor Red

    $skipped = $files.Count - $applied.Count - 1
    if ($skipped -gt 0) {
        Write-Host "Stopped early - $skipped later migration(s) not run." -ForegroundColor Yellow
    }

    exit 1
}

Write-Host "All migrations applied successfully." -ForegroundColor Green
exit 0
