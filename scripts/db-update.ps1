<#
.SYNOPSIS
    Brings the local database up to the latest migration.

.DESCRIPTION
    Running the app already does this — Program.cs calls Database.Migrate() in Development.
    Use this when you want the schema updated without launching, or to check that your
    database matches everyone else's after a pull.

    Defaults to the LocalDB instance in appsettings.Development.json. Pass a connection
    string to point somewhere else; it is exported as ConnectionStrings__Default, which is
    what DesignTimeDbContextFactory reads.

.EXAMPLE
    .\scripts\db-update.ps1

.EXAMPLE
    .\scripts\db-update.ps1 -ConnectionString "Server=.\SQLEXPRESS;Database=CompanyEmployees;Trusted_Connection=True;TrustServerCertificate=True"
#>
param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$persistence = 'src/Backend/CompanyEmployees.Persistence'

Push-Location $repoRoot
try {
    if ($ConnectionString) {
        $env:ConnectionStrings__Default = $ConnectionString
        Write-Host "Using the supplied connection string." -ForegroundColor Cyan
    }
    else {
        Write-Host "Using the LocalDB default from DesignTimeDbContextFactory." -ForegroundColor Cyan
    }

    Write-Host "`nRestoring dotnet-ef..." -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }

    Write-Host "`nApplying migrations..." -ForegroundColor Cyan
    dotnet dotnet-ef database update --project $persistence
    if ($LASTEXITCODE -ne 0) { throw "database update failed." }

    Write-Host "`nMigrations now in the project:" -ForegroundColor Cyan
    dotnet dotnet-ef migrations list --project $persistence

    Write-Host "`nDone. Your schema matches the repository." -ForegroundColor Green
}
finally {
    Pop-Location
}
