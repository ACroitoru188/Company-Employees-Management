# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers.

- `src/CompanyEmployees.Web` — Blazor Web App (.NET 10 / C#), **Interactive Server** render mode.
  The login and dashboard are a static UI mockup (sample data hardcoded, marked with `ponytail:`
  comments); no API or auth yet. Real functionality is added on top later.
- `src/CompanyEmployees.Data` — data layer (.NET 8 / C#), Entity Framework Core 8 (Code-First),
  SQL Server LocalDB. Not yet referenced by the Web project.

## Commands

Run from the repo root. `dotnet-ef` is a local tool (see `dotnet-tools.json`) — after a fresh
clone run `dotnet tool restore` once.

```sh
dotnet build                              # build the solution (CompanyEmployees.slnx)
dotnet run --project src/CompanyEmployees.Web            # build + run, http://localhost:5269
dotnet run --project src/CompanyEmployees.Web --no-build # run WITHOUT rebuilding (after a build)
dotnet watch --project src/CompanyEmployees.Web          # run with hot reload
dotnet dotnet-ef migrations add <Name> --project src/CompanyEmployees.Data
dotnet dotnet-ef database update --project src/CompanyEmployees.Data  # requires LocalDB
```

- Ports (from `launchSettings.json`): `http` profile → http://localhost:5269; `https` profile →
  https://localhost:7248. Pick the profile with `--launch-profile https` if needed.
- No test project exists yet. When one is added, wire up `dotnet test` and document it here.

## Project layout

```
CompanyEmployees.slnx
src/CompanyEmployees.Web/
  Program.cs                         # app startup (Razor Components + Interactive Server)
  Components/
    App.razor                        # root document (<head>, script tags); loads wwwroot/app.css
    Routes.razor                     # router; DefaultLayout = MainLayout
    Pages/
      Login.razor        (route /)          # uses AuthLayout, no sidebar; sign in → /dashboard
      Dashboard.razor    (route /dashboard) # stat tiles + recent-employees table
      Error.razor, NotFound.razor
    Layout/
      AuthLayout.razor   # bare layout for the login screen
      MainLayout.razor   # app shell: sidebar + topbar + content
      NavMenu.razor      # sidebar navigation
  wwwroot/app.css        # global design system (all styling lives here)
src/CompanyEmployees.Data/
  Entities/              # Employee, Department, Role, EmployeeRole (join, composite key) and the
                         # Permission [Flags] enum (Discord-style bitmask stored as long)
  ApplicationDbContext.cs      # all Fluent API config in OnModelCreating + HasData seed of the
                               # 3 default roles (Admin, Department Manager, Employee)
  Services/PermissionService.cs # HasPermission(employee, permission): ORs permissions across all
                                # of the employee's roles; Administrator flag overrides everything
  DesignTimeDbContextFactory.cs # hardcoded LocalDB connection string, used only by dotnet ef
                                # until the Web project wires up the DbContext via DI
  Migrations/            # EF Core migrations (InitialCreate)
```

## Conventions & architecture

### Web (UI)

- **Styling:** one global stylesheet, `wwwroot/app.css` — CSS custom properties for tokens, neutral
  slate base + a single blue accent (`--accent`), system font stack (no web-font dependency). Bootstrap
  was removed from the template. Keep new UI on these tokens; don't reintroduce a CSS framework without
  team agreement.
- **Icons** are small inline SVGs (no icon package). If a component library is later added, swap them.
- Pages are currently static SSR (no `@rendermode`). Add `@rendermode InteractiveServer` per-component
  when a page needs interactivity.

### Data

- Custom role system, deliberately NOT ASP.NET Core Identity — permissions are a bitmask on `Role`,
  effective permissions are the bitwise OR across an employee's roles.
- Delete behaviors: `Employee.DepartmentId` is `SetNull` (deleting a department keeps its
  employees); `Department.ManagerId` is `NoAction` because SQL Server rejects referential-action
  cycles with the other FK — detach a manager before deleting them. `EmployeeRole` cascades both ways.
- Firing an employee = soft delete via `Employee.IsActive`, not row deletion.
- `PasswordHash` exists in the schema but no hashing logic is implemented yet (intentional).
- TFM mismatch is deliberate: the Data project targets `net8.0` (per assignment spec) and the Web
  project `net10.0` — referencing the lower-TFM library from Web works fine.

## Working conventions

- This is a team project (4 devs) — avoid unrequested refactors or restructuring of code written by
  others; keep changes scoped to what's asked.
- Keep this file current as real structure lands: add `dotnet test` when a test project exists, and
  document architectural patterns (layering, EF Core wiring in the Web app, auth) as they're
  established.
