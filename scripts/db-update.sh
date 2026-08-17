#!/usr/bin/env bash
#
# Brings the local database up to the latest migration.
#
# Running the app already does this — Program.cs calls Database.Migrate() in Development.
# Use this when you want the schema updated without launching, or to check that your
# database matches everyone else's after a pull.
#
# LocalDB is Windows-only, so on Linux and macOS a connection string is required. Pass it
# as the first argument or set ConnectionStrings__Default, which is what
# DesignTimeDbContextFactory reads.
#
#   ./scripts/db-update.sh "Server=localhost,1433;Database=CompanyEmployees;User Id=sa;Password=...;TrustServerCertificate=True"
#
# With the mssql Docker image running as a container named "sql1", the password can come
# from the container instead of your shell history:
#
#   SA=$(docker inspect sql1 --format '{{range .Config.Env}}{{println .}}{{end}}' | grep '^MSSQL_SA_PASSWORD=' | cut -d= -f2-)
#   ./scripts/db-update.sh "Server=localhost,1433;Database=CompanyEmployees;User Id=sa;Password=${SA};TrustServerCertificate=True"

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

if [ -z "${ConnectionStrings__Default:-}" ]; then
    cat >&2 <<'MSG'
No connection string.

DesignTimeDbContextFactory falls back to LocalDB, which does not exist on this platform —
`dotnet ef` would fail with "LocalDB is not supported on this platform". Pass a connection
string as the first argument, or export ConnectionStrings__Default. See the header of this
script for the Docker one-liner.
MSG
    exit 1
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
