# Company Employees Management

Team of 3, developing a portal platform for managing employees — a leave (time-off) management app.

## Stack

Blazor Web App (.NET 9, Interactive Server) + MudBlazor, EF Core 8, SQL Server LocalDB, ASP.NET Core Identity, SignalR.

## Run

```sh
dotnet tool restore
dotnet dotnet-ef database update --project src/Backend/CompanyEmployees.Persistence
dotnet run --project src/Frontend/CompanyEmployees.Web
```

App runs at http://localhost:5269. Demo accounts and passwords are in `CLAUDE.md`.

## PostgreSQL fallback (Docker)

SQL Server remains the primary database. At process startup the application probes SQL Server;
if it is unavailable, it connects to the PostgreSQL standby and shows a non-dismissible warning
to signed-in administrators with the configured support contact.

```powershell
docker compose up -d postgres
dotnet run --project src/Frontend/CompanyEmployees.Web
```

Development defaults are in `appsettings.Development.json`. PostgreSQL data persists in the
`company-employees-postgres-data` Docker volume. While SQL Server is active and both databases
are reachable, the application refreshes PostgreSQL every 60 seconds with a complete backend
snapshot: users and password hashes, regions, departments, contracts, leave data, notifications,
delegations, audit data, and ASP.NET Identity tables. It also performs a final refresh immediately
before an administrator manually switches while SQL Server is reachable. If SQL Server fails, the
administrator switches to the latest completed PostgreSQL snapshot and uses the same login details.
Only when no snapshot has ever been created does startup add the emergency account
(`itadmin@siemens.com` / `User123!`, Romania).
Override secrets outside development with environment variables such as
`ConnectionStrings__PostgreSql` and `DatabaseFailover__SupportContact`.

To test fallback without stopping SQL Server, set
`DatabaseFailover__ForceProvider=PostgreSql` for that app process. Remove it to restore automatic
startup selection. If SQL Server is already down at startup, the app opens on PostgreSQL. While the
app is already running on SQL Server, a background health check also
warns administrators within about five seconds if SQL Server goes down. The warning lets an admin
select PostgreSQL without restarting the process; the browser reloads and asks them to sign in to
the fallback database. When SQL Server recovers, the PostgreSQL banner offers the reverse switch.
The built-in synchronization is intended for this application/demo deployment. Production use
still requires transactional replication, monitored backups, and a tested recovery plan.

LocalDB automatically starts itself when it is probed, so stopping `MSSQLLocalDB` is not a stable
outage simulation. While the app is running, create the development marker below, wait up to five
seconds, and use the admin banner's **Switch to PostgreSQL** action:

```powershell
New-Item .tmp/simulate-sqlserver-down -ItemType File -Force
```

Remove the marker to simulate SQL Server recovery and reveal **Switch back to SQL Server**:

```powershell
Remove-Item .tmp/simulate-sqlserver-down
```

## Tests

Run all unit tests from the repository root:

```sh
dotnet test CompanyEmployees.slnx
```

Collect Coverlet coverage reports:

```sh
dotnet test CompanyEmployees.slnx --collect:"XPlat Code Coverage"
```

The tests are split into `CompanyEmployees.Domain.Tests` for pure business rules and
`CompanyEmployees.Application.Tests` for workflows exercised with mocked gateways.
