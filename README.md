# Company Employees Management

Team of 3, developing a portal platform for managing employees — a leave (time-off) management app.

## Stack

Blazor Web App (.NET 9, Interactive Server) + MudBlazor, EF Core 8, SQL Server 2022, ASP.NET Core Identity, SignalR. SQL Server and the PostgreSQL standby run in Docker for development.

## Run

```sh
docker compose up -d --wait
dotnet tool restore
dotnet dotnet-ef database update --project src/Backend/CompanyEmployees.Persistence
dotnet run --project src/Frontend/CompanyEmployees.Web
```

App runs at http://localhost:5269. Demo accounts and passwords are in `CLAUDE.md`.

## Database containers and PostgreSQL fallback

SQL Server remains the primary database. At process startup the application probes SQL Server;
if it is unavailable, it connects to the PostgreSQL standby and shows a non-dismissible warning
to signed-in administrators with the configured support contact.

Development defaults are in `appsettings.Development.json`. SQL Server listens on `localhost:1433`
and PostgreSQL on `localhost:5432`. Their data persists in the
`company-employees-sqlserver-data` and `company-employees-postgres-data` Docker volumes. The
passwords in `compose.yaml` are development-only defaults; override `MSSQL_SA_PASSWORD`,
`POSTGRES_PASSWORD`, `ConnectionStrings__Default`, and `ConnectionStrings__PostgreSql` outside
local development.

While SQL Server is active, the application first
creates a complete PostgreSQL baseline: users and password hashes, regions, departments, contracts,
leave data, notifications, delegations, audit data, and ASP.NET Identity tables. After that baseline,
every EF Core business transaction records a change envelope in a durable outbox in the same
transaction. A worker applies those envelopes to the standby every two seconds.

Replication is bidirectional. Writes made while PostgreSQL is active queue there and are applied
back to SQL Server after it recovers. Failback is blocked until PostgreSQL has zero pending changes.
During a provider change, a process-wide write gate waits for in-flight saves, prevents a save from
crossing the switch boundary, and forces every signed-in Blazor circuit to sign in again with a new
DbContext. The admin status bar reports the active provider, standby health, last successful sync,
pending change count, and replication errors.

If SQL Server fails, the administrator switches to the latest completed PostgreSQL state and uses
the same login details. Changes already queued but not replicated before an abrupt primary failure
cannot be recovered from the unavailable primary; the status bar exposes that replication window.
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
The outbox table is initialized with provider-specific idempotent DDL because the existing SQL
Server migrations contain T-SQL and cannot be replayed on PostgreSQL. Production deployment still
requires monitored backups, provider-specific schema migration automation, retention/cleanup for
processed outbox rows, alerting, and a tested disaster-recovery procedure.

To test a real primary outage while the app is running, stop only SQL Server, wait up to five
seconds, and use the admin banner's **Switch to PostgreSQL** action:

```powershell
docker compose stop sqlserver
```

Start it again to reveal **Switch back to SQL Server** after the health check succeeds:

```powershell
docker compose start sqlserver
```

The `.tmp/simulate-sqlserver-down` marker remains available when stopping a container is
inconvenient. LocalDB is still supported by overriding `ConnectionStrings__Default`, but it is no
longer the development default.

Both databases share one Docker host in this setup, which is convenient for development but is not
a production high-availability design. Deploy the primary and standby to independent, monitored
infrastructure in production.

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
