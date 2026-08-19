#!/usr/bin/env bash
#
# Brings the local database up to the latest migration.
#
# Running the app already does this — Program.cs calls Database.Migrate() in Development.
# Use this when you want the schema updated without launching, or to check that your
# database matches everyone else's after a pull.
#
# Defaults to the SQL Server container configured in compose.yaml. Pass another connection
# string as the first argument or set ConnectionStrings__Default to override it.
#
#   ./scripts/db-update.sh "Server=db.example.test;Database=CompanyEmployees;User Id=company_app;Password=...;TrustServerCertificate=True"

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
persistence="src/Backend/CompanyEmployees.Persistence"

cd "$repo_root"

# Testing that it runs, not just that it exists: a partial package install can leave a
# /usr/bin/dotnet that resolves but fails with "[/usr/lib/dotnet/host/fxr] does not exist",
# shadowing a working SDK under $HOME.
dotnet_works() { command -v dotnet >/dev/null 2>&1 && dotnet --version >/dev/null 2>&1; }

if ! dotnet_works && [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi

if ! dotnet_works; then
    echo "No working dotnet SDK on PATH." >&2
    exit 1
fi

if [ $# -ge 1 ]; then
    export ConnectionStrings__Default="$1"
fi

echo "Restoring dotnet-ef..."
dotnet tool restore

echo
echo "Applying migrations..."
dotnet dotnet-ef database update --project "$persistence"

echo
echo "Migrations now in the project:"
dotnet dotnet-ef migrations list --project "$persistence"

echo
echo "Done. Your schema matches the repository."
