# Company Employees Management

Team of 3, developing a portal platform for managing employees — a leave (time-off) management app.

## Stack

Blazor Web App (.NET 9, Interactive Server) + MudBlazor, EF Core 8, SQL Server LocalDB, ASP.NET Core Identity, SignalR.

## Run

```sh
dotnet tool restore
dotnet dotnet-ef database update --project src/CompanyEmployees.Persistence
dotnet run --project src/CompanyEmployees.Web
```

App runs at http://localhost:5269. Demo accounts and passwords are in `CLAUDE.md`.
