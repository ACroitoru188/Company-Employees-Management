# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers. A leave
(time-off) management app: Blazor Web App (**.NET 9**, Interactive Server) + MudBlazor UI on top
of a layered backend with EF Core 8 and SQL Server LocalDB.

> The repo was **re-architected in July 2026** (commit `ec1dc44` and around it): the old
> `CompanyEmployees.Data` project, `ApplicationDbContext`, `Employee : IdentityUser<int>` entity
> and custom session/bitmask-auth code are **gone**. `TODO.md` still describes that old world —
> treat it as historical. Leftover `bin`/`obj` folders under `src/CompanyEmployees.Data`,
> `src/CompanyEmployees.BusinessLogic` and `src/CompanyEmployees.Data.UnitTests` are build
> debris of deleted projects, not code.

## Solution layout (`CompanyEmployees.slnx`, 6 projects, all net9.0)

```
src/CompanyEmployees.Domain          # entities, enums, gateway INTERFACES, domain exceptions
src/CompanyEmployees.Persistence     # CompanyEmployeesDbContext, IEntityTypeConfigurations,
                                     # Migrations/ (incl. SeedData/*.sql), DesignTimeDbContextFactory
src/CompanyEmployees.Gateway         # repository IMPLEMENTATIONS (BaseRepository holds the DbContext)
src/CompanyEmployees.Application     # business logic: Contexts/ (BaseContext, EmployeeContext,
                                     # ManagerContext, NotificationContext) + Hubs/NotificationHub
src/CompanyEmployees.Infrastructure  # cross-cutting: GlobalExceptionHandler, ResponseHandling
src/CompanyEmployees.Web             # Blazor Server + MudBlazor + minimal-API login
```

**Data flow (follow it, don't bypass it):**
Razor page → `ITimeOffService` (`Web/Services/DbTimeOffService`) → `EmployeeContext`
(Application) → `I*Gateway` (Domain/GatewayInterfaces) → `*Repository` (Gateway) →
`CompanyEmployeesDbContext` → LocalDB. Application never references Persistence — the gateway
interfaces live in Domain precisely so the dependency points inward. Web components never touch
the DbContext directly (only Identity's `UserManager`/`SignInManager` do, via DI).

Each layer registers itself via its own `ServiceCollectionExtensions`
(`AddPersistenceLayer(config)` / `AddGatewayLayer()` / `AddApplicationLayer()` /
`AddInfrastructureLayer()`), all called from `Web/Program.cs`.

## Commands

Run from the repo root. `dotnet-ef` 8.x is a local tool (`dotnet-tools.json` at the repo root) —
after a fresh clone run `dotnet tool restore` once.

```sh
dotnet build                                             # build the solution
dotnet run --project src/CompanyEmployees.Web            # run, http://localhost:5269
dotnet watch --project src/CompanyEmployees.Web          # hot reload
dotnet dotnet-ef migrations add <Name> --project src/CompanyEmployees.Persistence
dotnet dotnet-ef database update --project src/CompanyEmployees.Persistence
```

- Ports (`launchSettings.json`): `http` → http://localhost:5269; `https` → https://localhost:7248.
  `UseHttpsRedirection` is commented out in `Program.cs` so plain HTTP testing works.
- On Windows a running instance **locks `CompanyEmployees.Web.exe`** → `dotnet build` fails with
  MSB3027/MSB3026 (file-in-use), not a compile error. `taskkill /F /IM CompanyEmployees.Web.exe`
  first.
- Connection string: `ConnectionStrings:Default` in `Web/appsettings.Development.json`
  (LocalDB `CompanyEmployees`). `Persistence/DesignTimeDbContextFactory.cs` hardcodes its own copy
  for `dotnet ef` tooling.
- **Dev startup applies migrations**: `Program.cs` calls `db.Database.Migrate()` in Development,
  creating the DB if absent and applying anything pending. **Data persists between runs** —
  nothing is dropped, so records added through the UI survive a restart. Because the schema now
  comes from migrations rather than `EnsureCreated()`, an entity change without a matching
  migration will *not* show up in the DB: add the migration.
  - The old `DatabaseSeeder` (EnsureDeleted + EnsureCreated + hardcoded demo users) was removed
    on 2026-07-20 — demo data now arrives through the `SeedDemoData` migration (below).
  - A database created by the *old* `EnsureCreated()` path has no `__EFMigrationsHistory` table,
    so `database update` fails against it. Drop it once
    (`dotnet dotnet-ef database drop --force --project src/CompanyEmployees.Persistence`), then
    `database update`.
- No test project. When one lands, wire up `dotnet test` and document it here.

## Domain model (`Domain/Entities`, `Domain/Enums`)

- **`User : IdentityUser<Guid>`** — `Name`, `UserRole Role` (plain **enum column**:
  Guest/Employee/ProjectManager/LineManager/Admin — *not* Identity roles), `UserStatus Status`,
  `Guid? ManagerId` + `Manager`/`DirectReports` (self-reference, `DeleteBehavior.NoAction`),
  `CreatedAt`/`UpdatedAt`.
- **`LeaveRequest`** — `UserId`, `DateOnly StartDate/EndDate`, `Reason`, `LeaveStatus`,
  `LeaveType`, `Approvals`.
- **`LeaveAllocation`** — per user/type/year day quota.
- **`LeaveApproval`** — approval chain (`ApproverId`, `Step`, `Status`, `ReviewedAt`). Written by
  `ManagerContext.DecideRequestAsync` (approve/decline) in the same SaveChanges as the request's
  status change — one transaction.
- **`Notification`** — per-user message + optional `ActionUrl`, pushed live over SignalR (see
  Notifications below).
- `RoleAssignment`, `ImpersonationSession` — audit-ish entities, not wired to any UI.
- Fluent config lives in `Persistence/Configurations/*Configuration.cs` (one class per entity,
  auto-applied from the assembly). "Deleting" a user = soft delete via `Status = Inactive`
  (`UserRepository.DeleteUserAsync`).
- **`Department`** — `Name`, `Guid? ManagerId` (a LineManager; separate from `User.ManagerId`),
  `Members`. FKs: `User.DepartmentId` → `SetNull` on department delete;
  `Department.ManagerId` → `NoAction` (avoids an FK cascade cycle — detach a manager before
  deleting them). Admin CRUD lives in `EmployeeContext` (`GetDepartmentsAsync`,
  `Create/Update/DeleteDepartmentAsync`, `AssignUserToDepartmentAsync`) behind
  `IDepartmentGateway`/`DepartmentRepository`. Seeded: "Design" (managed by the line manager,
  containing LM + the two employees) and empty "Production"; admin + PM have no department.
- **Departments are org data, not team visibility** (deliberate): **Team** = the user's manager
  **plus** the active users sharing the same `ManagerId` (excluding the user).
  `EmployeeContext.GetTeamMembersAsync` / `GetTeamRequestsAsync` are the single source of that
  definition — calendar, dashboard "Team time off" and the Team page all route through them;
  change team visibility there only. The Web view-models' `Department`/`RoleLabel` fields still
  display `Role.ToString()`, not the real department — wiring `user.Department?.Name` into
  `DbTimeOffService` is a pending follow-up.
- Domain defines its own `InvalidOperationException` in `Domain/Exceptions` —
  `EmployeeContext` uses a `using` alias to pick it over System's; keep that in mind when
  catching.

## Auth (ASP.NET Core Identity, cookie-based — fully wired)

- `Program.cs`: `AddIdentity<User, IdentityRole<Guid>>()` + `AddSignInManager()` +
  `AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()` (issues `ClaimTypes.Role` =
  `user.Role.ToString()` and a `FullName` claim — layouts/pages read these instead of querying
  the DB); application cookie `LoginPath="/"`, 8 h sliding expiration;
  `AddCascadingAuthenticationState()` + `Web/Security/IdentityRevalidatingAuthenticationStateProvider`
  (30-min security-stamp revalidation). `<AuthorizeView>` / `[CascadingParameter]
  Task<AuthenticationState>` work.
- **Login flow**: `Login.razor` (route `/`, MudBlazor form) submits a hidden HTML
  `<form method="post" action="/api/auth/login">` via JS interop — the minimal API in
  `Program.cs` calls `SignInManager.PasswordSignInAsync` and redirects to `/employee/dashboard`
  (or `/?error=InvalidCredentials`). The hidden-form hop exists because an interactive circuit
  can't set the auth cookie itself; don't "simplify" it away.
- `UserName == Email` for all users, so `Identity.Name` from the auth state *is* the email.
- **Gaps to know about**: `DbTimeOffService` still **hardcodes the current user**
  (`employee@siemens.com`, marked TODO) instead of reading the auth state, and no `/employee/*`
  page carries `[Authorize]`. `[Authorize(Roles=...)]` *does* have role claims available now
  (via `AppClaimsPrincipalFactory`), but pages don't use it yet.
- **Demo accounts come from the `SeedDemoData` migration**, not from app code — see
  "Demo data" below for the full roster and passwords.

## Demo data (the `SeedDemoData` migration)

All demo accounts and their leave data live in
`Persistence/Migrations/SeedData/SeedDemoData.sql` — an **embedded resource** (registered in
`CompanyEmployees.Persistence.csproj`) that the `20260720073720_SeedDemoData` migration executes.
**No account is hardcoded in C#**: to add or change demo users, edit the `.sql` file (as a new
migration — never edit an applied one) rather than any C# file. Teammates get the identical
dataset by pulling and running the app (or `dotnet ef database update`).

- **37 users / 7 departments / 148 allocations / 63 leave requests / 32 approvals.**
- Seed rows use fixed GUID prefixes per table (`1111…` users, `2222…` departments, `3333…`
  allocations, `4444…` requests, `5555…` approvals) so the migration's `Down()` can remove
  exactly the seed data and leave anything created through the UI alone.
- Dates are emitted as `DATEADD(day, N, CAST(SYSUTCDATETIME() AS date))`, so the demo is dated
  relative to **when the migration runs** and never goes stale.
- Password hashes are pre-computed PBKDF2 values baked into the SQL (Identity's
  `PasswordHasher` format) — that is why the passwords below cannot be changed by editing a
  string; a new hash must be generated.
- **Passwords**: the 5 original accounts keep **`Passw0rd!`**; all 32 accounts added in the
  2026-07-20 expansion use **`User123!`** (Identity's default policy requires upper + lower +
  digit + non-alphanumeric, min 6 — a simple password like `user` is rejected).
- Email convention: `admin.<first>@siemens.com`, `lm.<first>@siemens.com` (LineManagers),
  `hr.<first>@siemens.com` (HR department staff), `first.last@siemens.com` (everyone else).
- Org shape: 3 admins; five departments each led by a LineManager who reports to an admin —
  **HR** (LM + 4 staff), **Engineering** and **Sales** (LM → ProjectManager → 4 employees),
  **Support** and **Marketing** (LM → 5 employees, no PM layer) — plus the original
  `itadmin` → `linemanager` → `projectmanager` → `employee`/`colleague` chain and the
  `Design`/`Production` departments.
- Requests span every `LeaveType` and every `LeaveStatus`; every Approved/Rejected one has a
  matching `LeaveApproval` from the requester's manager.

## The live Employee UI (MudBlazor)

Pages in `Web/Components/Employee/Pages/` — `EmployeeDashboard` (`/employee/dashboard`),
`EmployeeCalendar` (`/employee/calendar`), `EmployeeTeam` (`/employee/team`), `MyRequests`
(`/employee/my-requests`); all `@layout EmployeeLayout`.

- **Render mode is global**: `App.razor` wraps `<Routes @rendermode="InteractiveServer" />`, so
  layouts render interactively too. That is why `EmployeeProviders`
  (`MudPopoverProvider`/`MudDialogProvider`/`MudSnackbarProvider` + shared `MudTheme`) currently
  lives in `EmployeeLayout` and the `NotificationBell` in the AppBar works. **If per-page render
  modes ever return, the providers must move back into each page** — a static layout's providers
  never register with the circuit and every tooltip/dialog/snackbar crashes it
  ("Missing <MudPopoverProvider />").
- `Components/Layout/EmployeeLayout.razor` (MudAppBar + "WORKSPACE" drawer nav: Dashboard /
  Calendar / Team / My Requests + user footer). It reads the user's name/role from the auth
  cookie's claims, never the DB — the layout shares the page's scoped DbContext and a second
  in-flight query on it throws.
- **MudBlazor 8.x**; `AddMudServices()` in `Program.cs`; CSS/JS linked in `App.razor` via plain
  `_content/MudBlazor/…` hrefs (RCL assets don't go through `@Assets[]`).
- Leave-type color map: `Components/Employee/LeaveTypePalette.cs`, applied via inline `Style`
  (the colors aren't theme `Color` enum members). `LeaveBalanceSummary.razor` is the shared
  header + balance cards (Dashboard passes a CTA through its `Actions` RenderFragment).
- **View-models vs domain**: `Web/Models/` (`TeamMember`, `TimeOffRequest`, `LeaveBalance`) and
  the Web-side `LeaveType`/`RequestStatus` enums are **separate from the domain enums** and
  explicitly mapped in `DbTimeOffService` (numeric orders differ — never cast between them).
- **`ITimeOffService`** is the page-facing seam. `DbTimeOffService` (registered Scoped, real DB
  path) is live; `InMemoryTimeOffService` is an unregistered mock kept as a fallback — when the
  interface grows, both must implement the new member.
- `EmployeeTeam.razor` lists the team roster (`GetTeamRosterAsync` → `TeamRosterEntry`):
  manager first with a "Manager" chip, then teammates, each with their current-or-next approved
  leave period or "Available".
- `AdminDepartments.razor` (`/admin/departments`, admin-only) is the department CRUD page: edit
  name/manager per department, create/delete, assign users. It injects `EmployeeContext`
  directly (admin CRUD isn't time-off, so it skips `ITimeOffService`) and gates on the role
  claim — non-admins are redirected to the dashboard; the drawer link renders only when the
  claim says Admin.
- Date formatting: always `CultureInfo.InvariantCulture` (server culture may not be English).

## Notifications (SignalR)

- `Application/Hubs/NotificationHub` is mapped at `/notificationHub` in `Program.cs`
  (`AddSignalR` + `MapHub`). `NotificationContext` (Application) persists notifications via
  `INotificationGateway`/`NotificationGateway` and pushes `"ReceiveNotification"` to the user.
- `Web/Components/NotificationBell.razor` (in the `EmployeeLayout` AppBar) opens its own
  `HubConnection` to the hub, shows unread items and marks them read. It resolves the user id
  from the auth state and uses `IServiceScopeFactory` to query outside the layout's scope.
- `ManagerContext.DecideRequestAsync` is the manager approval flow (approve/decline a pending
  request + notification to the requester).

## Other UI in the tree (know before styling)

Four design systems coexist; only the MudBlazor one above is on the live path from login.

1. **Retro/vintage-desktop Dashboard** — `Components/Pages/Dashboard.razor` (`/dashboard`,
   direct URL only, orphaned from the login flow) composed from ~20 components under
   `Components/*.razor` (`Sidebar`, `TopBar`, `Card`, `RetroButton`, `ManagerOverview`,
   `TeamCalendarView`, …). UI mockup with in-memory state only, not backed by `ITimeOffService`.
   - **CSS Isolation**: each `Foo.razor` has a co-located `Foo.razor.css`. A rule only applies to
     markup *authored by* the declaring component — RenderFragment content (e.g. `Card`'s
     `ChildContent`) is scoped to the *passing* component, and `::deep` only pierces into a real
     child component. Misplaced CSS silently drops styling; check
     `obj/**/scopedcss/**/*.bundle.scp.css` for the expected `[b-xxxxxxxx]` scope if a rule
     doesn't apply.
   - Design tokens in `wwwroot/css/tokens.css` (`--color-*`, `--space-*`, `--font-mono`, …) —
     consume `var(--color-teal)` etc., never hardcoded hex. Flat and static by design: no
     gradients, shadows, or animations.
2. **HR dashboard** — `Components/Pages/HRDashboard.razor` (`/hr/dashboard`, own `.razor.css`),
   not linked from the drawer nav; reachable by direct URL.
3. **Corporate Siemens system** — `Pages/ForgotPassword.razor` only; class-based styling in the
   `.lm` section of `wwwroot/app.css` (`--l-*` tokens). Don't re-add Dashboard rules there.
4. **Old default shell** — `Layout/MainLayout.razor` + `NavMenu.razor`; still the
   `DefaultLayout` in `Routes.razor`, backing only `NotFound.razor` and `Error.razor`. Not dead
   code.

Icons are inline SVGs (no icon package). GSAP + ScrollTrigger are loaded **from the cdnjs CDN**
in `App.razor` (plus `/pointer.js` and `/anim.js`) but are **dormant** — the MudBlazor Login has
no `data-*` anim attributes; the declarative contract (`data-reveal`, `data-intro`, …) in
`wwwroot/js/anim.js` still works if markup opts in. `Dashboard.razor` and `Login.razor` use
`@layout AuthLayout` (bare `@Body`).

## Working conventions

- Team project (4 devs) — avoid unrequested refactors of others' code; keep changes scoped.
- Follow the layer flow for new features: gateway interface in Domain, implementation in
  Gateway, logic in an Application context, thin mapping in a Web service. Don't add a new
  context class for a handful of pass-throughs — extend `EmployeeContext`.
- `TODO.md` is stale (pre-rearchitecture).
- Keep this file current as structure lands: `dotnet test` when tests exist, auth hardening as
  it happens.
