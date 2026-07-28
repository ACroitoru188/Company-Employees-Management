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
  `ManagerContext.DecideRequestAsync` (manager approve/decline) or `EmployeeContext.
  HrDecideRequestAsync` (HR review, added 2026-07-28) in the same SaveChanges as the request's
  status change — one transaction either way.
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
  change team visibility there only. The Web view-models' `Department`/`RoleLabel` fields now
  combine role and department via `DbTimeOffService.GetCurrentUserAsync`'s
  `RoleAndDepartment(user)` helper, rather than the bare `Role.ToString()`.
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
- `DbTimeOffService` resolves the current user from the **auth state**
  (`AuthenticationStateProvider.GetAuthenticationStateAsync()` → `Identity.Name` → email →
  `GetEmployeeByEmailAsync`), cached per circuit. Every `ITimeOffService` method funnels through
  its `GetDomainUserAsync()`, so Dashboard/Calendar/Team/My Requests all follow whoever is logged
  in. (Ported from `refactorizare-departamente-1` on 2026-07-20; it previously hardcoded
  `employee@siemens.com`, which showed every user Demo Employee's team and requests.)
- **Auth gating goes through `PageGuard`, not `[Authorize]`**: `Components/Routes.razor` uses
  a plain `<Router>`/`<RouteView>`, not `AuthorizeRouteView`, so `[Authorize]` would do nothing
  anyway. Every gated page instead calls `PageGuard.IsAuthenticatedAsync(AuthStateTask, Nav,
  RendererInfo.IsInteractive)` first in `OnInitializedAsync`: it no-ops while still
  prerendering (`isInteractive` false) to avoid a `NavigateTo` throwing `NavigationException`
  into the root `<ErrorBoundary>` as a raw error screen, then redirects unauthenticated users
  to `/?returnUrl=…` once interactive. `/employee/*` pages need nothing more; `/manager/team`,
  `/hr/dashboard` and `/admin/departments` layer their own role/department claim check on top
  of it. `Web/Security/HomeRouteResolver` still maps `(UserRole? role, string? department)` →
  home route for the post-login redirect; `POST /api/auth/login` and `Login.razor` both now
  honor a survived `returnUrl` first.
- **Demo accounts come from migrations**, not from app code — see "Demo data" below for the
  full roster and passwords.

## Demo data (`SeedDemoData` + `ResetAccountsAndLeaveData` migrations)

All demo accounts and their leave data live in `Persistence/Migrations/SeedData/*.sql` —
**embedded resources** (registered in `CompanyEmployees.Persistence.csproj`) run by their
matching migration. **No account is hardcoded in C#**: to add or change demo users, edit a
`.sql` file (as a new migration — never edit an applied one) rather than any C# file. Teammates
get the identical dataset by pulling and running the app (or `dotnet ef database update`).

Two migrations layer on top of each other — read both, in order, to know what's actually in the
DB:

1. **`20260720073720_SeedDemoData`** — original 37 users / 7 departments / 148 allocations / 63
   leave requests / 32 approvals, built around a `itadmin` → `linemanager` → `projectmanager` →
   `employee`/`colleague` chain (passwords `Passw0rd!`) plus a 32-account expansion (passwords
   `User123!`). This is the migration the rest of this section historically described.
2. **`20260728105509_ResetAccountsAndLeaveData`** (2026-07-28) — **wipes every `LeaveRequest`/
   `LeaveApproval` in the DB** (seeded or UI-created) and **deletes the 5 original demo
   accounts** (`itadmin@`/`linemanager@`/`projectmanager@`/`employee@`/`colleague@siemens.com`
   no longer exist, and `Passw0rd!` no longer unlocks anything), then adds **68 new Employee
   accounts** (272 matching `LeaveAllocation` rows, 4 per user) round-robin distributed across
   the 5 active departments, each reporting to that department's LineManager. No leave requests
   or approvals are seeded by this migration — the table starts empty; only what teammates
   create through the UI exists after this.

Net effect on a freshly-migrated DB: **100 users, all on password `User123!`**, `Design`
department now empty (its only members were 3 of the deleted 5), and **zero pre-seeded leave
requests/approvals** — that data is now purely whatever's been created through the UI since
2026-07-28, so don't assume any fixed count; query `LeaveRequests`/`LeaveApprovals` or check the
HR Dashboard's live counters if you need current numbers.

- Seed rows use fixed GUID prefixes per table (`1111…` users, `2222…` departments, `3333…`
  allocations) so a migration's `Down()` can remove exactly what it added and leave anything
  created through the UI alone.
- Dates are emitted as `DATEADD(day, N, CAST(SYSUTCDATETIME() AS date))`, so the demo is dated
  relative to **when the migration runs** and never goes stale.
- Password hashes are pre-computed PBKDF2 values baked into the SQL (Identity's
  `PasswordHasher` format) — that is why passwords can't be changed by editing a string; a new
  hash must be generated (Identity's default policy requires upper + lower + digit +
  non-alphanumeric, min 6).
- Email convention: `admin.<first>@siemens.com`, `lm.<first>@siemens.com` (LineManagers),
  `hr.<first>@siemens.com` (HR department staff), `first.last@siemens.com` (everyone else).
- A leave request's `LeaveApproval` comes from either the requester's manager
  (`ManagerContext.DecideRequestAsync`) or HR (`EmployeeContext.HrDecideRequestAsync`).

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
  Calendar / Team / My Requests + user footer, plus role/department-conditional sections — "HR"
  for HR-department users, "Department" for LineManager/ProjectManager, "IT ADMIN" for Admin).
  It reads the user's name/role from the auth cookie's claims, never the DB — the layout shares
  the page's scoped DbContext and a second in-flight query on it throws.
- **MudBlazor 9.x** (`9.7.0` pinned in `CompanyEmployees.Web.csproj`); `AddMudServices()` in
  `Program.cs`; CSS/JS linked in `App.razor` via plain
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
- `AdminDepartments.razor` (`/admin/departments`) and `AdminUsers.razor` (`/admin/users`) are
  the two admin-only CRUD pages (both under the drawer's "IT ADMIN" section, rendered only when
  the role claim says Admin). Both inject `EmployeeContext` directly (admin CRUD isn't
  time-off, so they skip `ITimeOffService`) and gate on the role claim, redirecting non-admins
  to the dashboard. `AdminDepartments` edits name/manager per department and creates/deletes
  departments; `AdminUsers` reassigns a user's department (`AssignUserToDepartmentAsync`) — the
  Users table used to live inside `AdminDepartments` but was split into its own page on
  2026-07-28. Both list tables are `MudDataGrid` (sortable/filterable/paged via a `QuickFilter`
  toolbar search), not the plain `MudTable` used elsewhere.
- Date formatting: always `CultureInfo.InvariantCulture` (server culture may not be English).

## Notifications (SignalR)

- `Application/Hubs/NotificationHub` is mapped at `/notificationHub` in `Program.cs`
  (`AddSignalR` + `MapHub`). `NotificationContext` (Application) persists notifications via
  `INotificationGateway`/`NotificationGateway` and pushes `"ReceiveNotification"` to the user.
- `Web/Components/NotificationBell.razor` (in the `EmployeeLayout` AppBar) opens its own
  `HubConnection` to the hub, shows unread items and marks them read. It resolves the user id
  from the auth state and uses `IServiceScopeFactory` to query outside the layout's scope.
- `ManagerContext.DecideRequestAsync` is the manager approval flow (approve/decline a pending
  request + notification to the requester); `EmployeeContext.HrDecideRequestAsync` (added
  2026-07-28) is the equivalent for HR's review from `/hr/dashboard` — same shape (status
  change + `LeaveApproval` + best-effort notification, decision still saved even if the
  notification send fails).

## Other UI in the tree (know before styling)

Four design systems coexist; only the MudBlazor one above is on the live path from login.

1. **Retro/vintage-desktop Dashboard** — `Components/Pages/Dashboard.razor` (`/dashboard`,
   direct URL only, orphaned from the login flow) composed from ~20 components under
   `Components/*.razor` (`Sidebar`, `TopBar`, `Card`, `RetroButton`, `ManagerOverview`,
   `TeamCalendarView`, …). UI mockup with in-memory state only, not backed by `ITimeOffService`.
   - **CSS Isolation** (this trips people up project-wide, not just here): each `Foo.razor` has
     a co-located `Foo.razor.css`. A rule only applies to markup *authored directly* in that
     component's own markup — RenderFragment content (e.g. `Card`'s `ChildContent`) is scoped to
     the *passing* component, and **a child component's own rendered root is never reachable at
     all** without `::deep` (Blazor never emits a scope attribute onto a child component's
     output — `<MudPaper Class="foo">` means `.foo` alone can't match MudPaper's root div, full
     stop). `::deep .foo` compiles to the descendant selector `[b-scope] .foo`, which then needs
     a literal DOM **ancestor** carrying that scope attribute — a sibling element in the markup
     doesn't count, so where you close a wrapping `<div>` relative to the target matters (see
     `EmployeeCalendar.razor`'s submit-bar fix, 2026-07-28: it hit both traps stacked — needed
     `::deep`, *and* had to move the submit-bar block inside the scoped wrapper `<div>` instead
     of after it, since as a sibling no ancestor ever carried the scope attribute). Misplaced CSS
     silently drops styling (computed `position` quietly falls back to `static`, no error, no
     warning); check `obj/**/scopedcss/**/*.bundle.scp.css` for the expected `[b-xxxxxxxx]` scope
     if a rule doesn't apply, or query `document.styleSheets` in devtools to see which rules
     actually matched.
   - Design tokens in `wwwroot/css/tokens.css` (`--color-*`, `--space-*`, `--font-mono`, …) —
     consume `var(--color-teal)` etc., never hardcoded hex. Flat and static by design: no
     gradients, shadows, or animations.
2. **HR dashboard** — `Components/Pages/HRDashboard.razor` (`/hr/dashboard`, own `.razor.css`).
   Linked from the drawer nav's "HR" section for users in the HR department (`EmployeeLayout`
   checks the `Department` claim); reachable by direct URL for everyone else, but the page
   itself gates on that same claim and redirects non-HR users to the dashboard. Approve/Reject
   call real backend logic (`EmployeeContext.HrDecideRequestAsync`, added 2026-07-28) — they
   used to be no-op stubs.
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
