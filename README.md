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
