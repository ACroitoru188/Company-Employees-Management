# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers. A leave
(time-off) management app: Blazor Web App (**.NET 9**, Interactive Server) + **Microsoft Fluent UI
Blazor** on top of a layered backend with EF Core 8 and SQL Server LocalDB.

> The UI was **migrated from MudBlazor to Fluent UI Blazor** in commit `a25e53d`. MudBlazor is
> gone from the source entirely — the only remaining hits are build debris under the deleted
> `src/CompanyEmployees.Web/bin|obj` (note the path: the live project is
> `src/Frontend/CompanyEmployees.Web`). Anything below describing a `Mud*` component is history.

> The repo was **re-architected in July 2026** (commit `ec1dc44` and around it): the old
> `CompanyEmployees.Data` project, `ApplicationDbContext`, `Employee : IdentityUser<int>` entity
> and custom session/bitmask-auth code are **gone**. `TODO.md` still describes that old world —
> treat it as historical. Leftover `bin`/`obj` folders under `src/CompanyEmployees.Data`,
> `src/CompanyEmployees.BusinessLogic` and `src/CompanyEmployees.Data.UnitTests` are build
> debris of deleted projects, not code.

## Solution layout (`CompanyEmployees.slnx`, 8 projects, all net9.0)

```
src/Backend/CompanyEmployees.Domain          # entities, enums, gateway INTERFACES, domain exceptions
src/Backend/CompanyEmployees.Persistence     # CompanyEmployeesDbContext, IEntityTypeConfigurations,
                                             # Migrations/ (incl. SeedData/*.sql), DesignTimeDbContextFactory
src/Backend/CompanyEmployees.Gateway         # repository IMPLEMENTATIONS (BaseRepository holds the DbContext)
src/Backend/CompanyEmployees.Application     # business logic: Contexts/ (BaseContext, EmployeeContext,
                                             # ManagerContext, NotificationContext, ImpersonationContext)
src/Backend/CompanyEmployees.Infrastructure  # cross-cutting: GlobalExceptionHandler, ResponseHandling
src/Frontend/CompanyEmployees.Web            # Blazor Server + Fluent UI + minimal-API login
tests/CompanyEmployees.Domain.Tests          # xunit: LeaveAllocationPolicy, LeaveApprovalPolicy
tests/CompanyEmployees.Application.Tests     # xunit: ManagerContext, NotificationContext
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
dotnet test                                              # 19 tests, all green
dotnet run --project src/Frontend/CompanyEmployees.Web   # run, http://localhost:5269
dotnet watch --project src/Frontend/CompanyEmployees.Web # hot reload
dotnet dotnet-ef migrations add <Name> --project src/Backend/CompanyEmployees.Persistence
dotnet dotnet-ef database update --project src/Backend/CompanyEmployees.Persistence
```

- Ports (`launchSettings.json`): `http` → http://localhost:5269; `https` → https://localhost:7248.
  `UseHttpsRedirection` is commented out in `Program.cs` so plain HTTP testing works.
- A running instance used to **lock `CompanyEmployees.Web.exe`** and fail the build with
  MSB3027/MSB3026. The `KillZombieProcesses` target in `CompanyEmployees.Web.csproj` now runs
  `taskkill /F /IM CompanyEmployees.Web.exe /T` before every build on Windows, so the build
  succeeds — but **it silently stops whatever instance is running**, including one started from
  an IDE. Expect to restart the app after any build.
- Connection string: `ConnectionStrings:Default` in `Web/appsettings.Development.json`
  (LocalDB `CompanyEmployees`). `Persistence/DesignTimeDbContextFactory.cs` keeps its own copy
  for `dotnet ef` tooling, overridable with the `ConnectionStrings__Default` **environment
  variable** — LocalDB is Windows-only, so on Linux/macOS that variable is required or every
  `dotnet ef` command dies with "LocalDB is not supported on this platform".
- **Everyone ends up on the same schema by just running the app** — `Database.Migrate()` on
  startup applies whatever is pending. `scripts/db-update.ps1` (Windows/LocalDB) and
  `scripts/db-update.sh` (Linux/macOS, connection string required) do the same without
  launching, and print the migration list so you can confirm you are in sync after a pull.
  `scripts/sql/reset-delegation-test-data.sql` empties the delegation tables — testing leftovers
  only, a no-op on a machine that has not run the feature.
- **Dev startup applies migrations**: `Program.cs` calls `db.Database.Migrate()` in Development,
  creating the DB if absent and applying anything pending. **Data persists between runs** —
  nothing is dropped, so records added through the UI survive a restart. Because the schema now
  comes from migrations rather than `EnsureCreated()`, an entity change without a matching
  migration will *not* show up in the DB: add the migration.
  - The old `DatabaseSeeder` (EnsureDeleted + EnsureCreated + hardcoded demo users) was removed
    on 2026-07-20 — demo data now arrives through the `SeedDemoData` migration (below).
  - A database created by the *old* `EnsureCreated()` path has no `__EFMigrationsHistory` table,
    so `database update` fails against it. Drop it once
    (`dotnet dotnet-ef database drop --force --project src/Backend/CompanyEmployees.Persistence`), then
    `database update`.
- **Tests exist** (they did not when this file was first written): xunit, two projects under
  `tests/`, 19 tests, `dotnet test` green. They cover domain policies and two Application
  contexts; there is no Web/component test project, so Razor pages are still only verified by
  running the app.

## Domain model (`Domain/Entities`, `Domain/Enums`)

- **`User : IdentityUser<Guid>`** — `Name`, `UserRole Role` (plain **enum column**:
  Guest=0/Employee=1/LineManager=3/Admin=4 — *not* Identity roles; **2 is an intentional gap**,
  `ProjectManager` was removed and folded into `LineManager` on 2026-07-28 — the app never
  treated the two differently anywhere, so don't reintroduce the distinction or reuse value 2),
  `UserStatus Status`, `Guid? ManagerId` + `Manager`/`DirectReports` (self-reference,
  `DeleteBehavior.NoAction`), `CreatedAt`/`UpdatedAt`.
- **`LeaveRequest`** — `UserId`, `DateOnly StartDate/EndDate`, `Reason`, `LeaveStatus`,
  `LeaveType`, `Approvals`.
- **`LeaveAllocation`** — per user/type/year day quota. Missing allocations are created lazily
  by `EmployeeContext.GetMyBalancesAsync` through
  `ILeaveRequestGateway.EnsureDefaultAllocationsAsync`: Annual 21, Sick 10, Parental 10, and
  Unpaid 30 days. This makes new and regionally seeded accounts usable without a separate
  allocation backfill and initializes the next year when it is first viewed.
- **`LeaveApproval`** — approval chain (`ApproverId`, `Step`, `Status`, `ReviewedAt`). Written by
  `ManagerContext.DecideRequestAsync` (manager approve/decline) or `EmployeeContext.
  HrDecideRequestAsync` (HR review, added 2026-07-28) in the same SaveChanges as the request's
  status change — one transaction either way.
- **`Notification`** — per-user message + optional `ActionUrl`, pushed live over SignalR (see
  Notifications below).
- `RoleAssignment` — audit-ish entity, not wired to any UI.
- **`ImpersonationSession`**, **`DelegatedAction`** — the delegation audit trail; see
  "Delegation = borrowed accounts" below. `ImpersonationSession` was reshaped on 2026-08-08
  (it used to be `AdminId`/`TargetUserId` with no table).
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
- **`Region`** is the current employment and security scope (`Name`, unique `Code`, `IsActive`).
  `User.RegionId` is required and separate from department, so one department can contain people
  in several countries. The `AddRegions` migration seeds Romania (`RO`) and Pakistan (`PK`),
  assigns all existing accounts to Romania, and `AddMoreRegions` adds 30 additional international
  regions. Admin account creation requires a region, and the Users grid can relocate an account
  later. Admins may preview employee lists from every region, but foreign-region rows are
  read-only: department, contract, transfer, and account-creation mutations are checked against
  the administrator's own region in both the UI and application/service layer. Relocation changes
  the security stamp and removes manager/direct-report links that would cross the new regional boundary.
  `SeedRegionalDemoAccounts` adds one Admin (`admin.<code>@siemens.com`), Line Manager
  (`lm.<code>@siemens.com`), and HR employee (`hr.<code>@siemens.com`) to every active region,
  all using `User123!`. Regional HR belongs to the global HR department and reports to the Line
  Manager in the same region. This migration is additive-only so rollback cannot erase HR data
  subsequently attached to these accounts.
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
- **Login flow**: `Login.razor` (route `/`, Fluent form) requires a region and submits a hidden
  HTML `<form method="post" action="/api/auth/login">` via JS interop — the minimal API verifies
  the password and that the account belongs to the selected active region, then redirects to
  `/employee/dashboard` (or `/?error=InvalidCredentials`). Selecting a region never grants
  access; it must match `User.RegionId`. The hidden-form hop exists because an interactive circuit
  can't set the auth cookie itself; don't "simplify" it away.
- `UserName == Email` for all users, so `Identity.Name` from the auth state *is* the email.
- The cookie includes `RegionId` and `Region` claims for display and HTTP endpoint scoping, while
  sensitive application queries use the account's current database region. Team rosters,
  manager/HR dashboards and decisions, contract actions, non-admin org charts, and every CSV
  export are region-scoped. Admins may preview the global user list, but foreign rows remain
  read-only and exports always use the admin account's database region, never the preview filter.
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

## Demo data (`SeedDemoData` + `ResetAccountsAndLeaveData` + `RemoveProjectManagerRole` migrations)

All demo accounts and their leave data live in `Persistence/Migrations/SeedData/*.sql` —
**embedded resources** (registered in `CompanyEmployees.Persistence.csproj`) run by their
matching migration. **No account is hardcoded in C#**: to add or change demo users, edit a
`.sql` file (as a new migration — never edit an applied one) rather than any C# file. Teammates
get the identical dataset by pulling and running the app (or `dotnet ef database update`).

Three migrations layer on top of each other — read them in order to know what's actually in the
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
3. **`20260728162532_RemoveProjectManagerRole`** (2026-07-28, same day) — no seed SQL, just
   `UPDATE [AspNetUsers] SET [Role] = 3 WHERE [Role] = 2`: the 2 remaining `ProjectManager`
   accounts (Diana Marinescu/Engineering, Alexandru Stoica/Sales — the 3rd, the original
   `projectmanager@siemens.com`, was already deleted by migration 2) become `LineManager`. They
   keep their existing 4-person teams and keep reporting to their department's actual
   LineManager; nothing about the reporting graph changes, only the role label.

Net effect on a freshly-migrated DB: **100 users, all on password `User123!`**, every manager
role is `LineManager` (`ProjectManager` no longer exists as a concept), `Design` department now
empty (its only members were 3 of the deleted original 5), and **zero pre-seeded leave
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

## The live Employee UI (Fluent UI Blazor)

Pages in `Web/Components/Employee/Pages/` — `EmployeeDashboard` (`/employee/dashboard`),
`EmployeeCalendar` (`/employee/calendar`), `EmployeeTeam` (`/employee/team`), `MyRequests`
(`/employee/my-requests`), `NotificationsHistory` (`/employee/notifications`), plus an org chart
at `/employee/org-chart`; all `@layout EmployeeLayout`.

- **Render mode is global**: `App.razor` wraps `<Routes @rendermode="InteractiveServer" />`, so
  layouts render interactively too. That is why `EmployeeProviders` lives in `EmployeeLayout` and
  the `NotificationBell` in the header works. **If per-page render modes ever return, the
  providers must move back into each page** — a static layout's providers never register with the
  circuit and every tooltip/dialog/toast crashes it.
- `Components/Employee/EmployeeProviders.razor` is the provider bundle: `FluentDesignTheme`
  (must sit **above** the rest — it emits the design tokens every component reads),
  `FluentToastProvider`, `FluentDialogProvider`, `FluentTooltipProvider`, `FluentMenuProvider`,
  and a `FluentMessageBarProvider` pinned to `MessageSections.Page` (an unsectioned one would
  also swallow the notification bell's centre). Include it at the top of every layout.
  `AccentColor` (`#0F6CBD`) must stay in step with the value `app.css` uses for light-DOM
  elements, or the app runs two slightly different blues.
- `Components/Layout/EmployeeLayout.razor` ("WORKSPACE" nav: Dashboard / Calendar / Team / My
  Requests / Org Chart + user footer, plus role/department-conditional sections — "HR" for
  HR-department users, "Department" for LineManager, "IT ADMIN" for Admin).
  It reads the user's name/role from the auth cookie's claims, never the DB — the layout shares
  the page's scoped DbContext and a second in-flight query on it throws.
- **Microsoft.FluentUI.AspNetCore.Components 4.14.4** (pinned, plus the matching `.Icons`
  package); `AddFluentUIComponents()` in `Program.cs`. Icons come from the icon package as
  `new Icons.Regular.Size20.Foo()`, not inline SVG as on the older pages.
- Dialogs and toasts are injected services (`IDialogService`, `IToastService`), not components.

### FluentCombobox raises SelectedOptionChanged more than once — guard it

A single pick fires the callback **at least twice**: first with a **null** option, then with the
option actually chosen ([fluentui-blazor#2077](https://github.com/microsoft/fluentui-blazor/issues/2077)),
and then again on every render for as long as the component's own selection disagrees with the
`SelectedOption` it is handed back. Two rules, both learned the hard way on `/admin/users`:

1. **Ignore a null option.** Clearing a value on purpose arrives as a real "None" entry carrying
   `Guid.Empty`, never as null, so null is always the phantom callback. Acting on it wipes the
   field for as long as the real value takes to arrive — and permanently if that save then fails.
   Do **not** funnel both through a `ToXxxId(option)` helper that maps null and `Guid.Empty` to
   the same thing; that conflation is what hid the bug.
2. **Write the new value to the bound row before the first `await`.** Until the row agrees with
   the component, every render contradicts it and it raises the event again — an endless loop of
   handler calls that never lets the save finish.

Because the circuit has **one scoped `DbContext`**, overlapping callbacks also mean overlapping
EF operations, which EF Core refuses ("a second operation was started on this context instance").
Unhandled, that kills the whole circuit and blanks the page. Handlers doing database work on
these pages therefore also take a `SemaphoreSlim` and wrap the work in `try/catch` →
`Toast.ShowError`. The same collision happens when clicking Delete blurs a focused field, so
delete paths share the lock. `OnUserRegionChangedAsync` had the null guard from the start, which
is exactly why the Region column never showed the bug while Department did.
### Styling a Fluent control from `app.css` — three things that will bite you

Learned while giving the buttons a Fluent 2 palette; all three were measured, not guessed.

1. **An outline button's background cannot be set through `::part`.** Its shadow sheet carries
   `.control { background: transparent !important }`, and for `!important` declarations the
   *inner* tree wins over the outer one — an outer `!important` does not win either. The host
   element is ordinary light DOM, so paint that instead and give it the control's radius.
2. **Appearance-scoped rules outrank the app-bar block.** `fluent-button[appearance="stealth"]…`
   scores (0,3,1) against `header.header fluent-button…`'s (0,2,2), so a page rule silently
   repaints the navy bar — that is where the near-white chip on the header came from, twice.
   Prefixing with `body:has(fluent-design-theme[mode="dark"])` adds another (0,1,1) on top and
   breaks the same overrides again in dark mode only.
3. **Do not scope a per-theme palette by repeating `body:has(…)` on every rule.** The buttons use
   two tiers of custom property instead — `--app-page-btn-*` for the page palette, `--app-btn-*`
   for what the rules read — so a surface with its own treatment (the app bar) repoints them for
   its subtree and a surface nested inside it that is really page content (the notification
   popover, which renders *inside* `<header>`) points them back. Inheritance does the scoping,
   which means no rule has to out-score another and nothing is written twice per theme.

Corollary for the light-DOM tokens at the top of `app.css`: they are fine for text and surfaces
but **not for controls**. FAST recomputes its ramp per component against whatever that component
sits on, so one token holds different values in different places — measured in dark mode,
`--neutral-fill-rest` is `#292929` at document level and `#3D3D3D` inside a card.

- Leave-type color map: `Components/Employee/LeaveTypePalette.cs`, applied via inline `Style`
  (the colors aren't theme `Color` enum members). `LeaveBalanceSummary.razor` is the shared
  header + balance cards (Dashboard passes a CTA through its `Actions` RenderFragment).
- The employee calendar loads public holidays for the signed-in user's current region through
  `IPublicHolidayProvider`. `NagerDatePublicHolidayProvider` uses the stable Nager.Date v3 API,
  caches results by country/year, and has built-in calendars for unsupported PK/IN/AE regions
  (India's full fallback is currently 2026; other India years contain only the three national
  fixed holidays, while UAE/Pakistan fallbacks omit lunar dates). Weekends and regional public
  holidays are displayed but cannot be selected, and they do not consume the leave balance.
  The Application layer repeats those checks on submit/edit so the UI cannot be bypassed.
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
  2026-07-28. `AdminUsers` also owns contracts, region transfer, CSV export and account
  creation. Both list tables are `FluentDataGrid` with `PaginationState` + `FluentPaginator`,
  a toolbar `FluentSearch` and per-column `ColumnOptions` filters that stack with it; both
  edit inline through `FluentCombobox`/`FluentSelect` cells, so read the combobox warning above
  before touching either.
- Date formatting: `CultureInfo.InvariantCulture` on the admin/data paths (server culture may not
  be English); the user-facing contract and calendar strings use `CultureInfo.CurrentCulture`,
  which request localization sets from the signed-in user's language.

## Localization (21 languages, home-grown — not IStringLocalizer)

Undocumented until now, and it touches every page you will edit.

- `Web/Services/AppLocalizer` is a **singleton** that loads `Web/Languages/<culture>.json` from
  `ContentRootPath` at startup. Each file is a flat `{ "english source string": "translation" }`
  map, keyed by the English text itself — there are no symbolic keys. A missing file **throws at
  startup**, so a new entry in `SupportedLanguages.All` needs its JSON added in the same change.
- The files are `Content … CopyToOutputDirectory="PreserveNewest"` in the csproj precisely
  because the localizer reads them from disk rather than from resources.
- Pages `@inject AppLocalizer Text` and write `@Text["Some label"]`, or
  `Text.Format("{0} moved.", name)` when there are placeholders. **Adding UI text means adding
  the same key to all 21 JSON files**, keeping `{0}`/`{1}` intact in every translation.
- `LanguagePreferenceService` + `User.PreferredCulture` persist the choice (`LanguageDialog`
  changes it); `Program.cs` calls `UseRequestLocalization`, which is what makes
  `CultureInfo.CurrentCulture` correct on user-facing pages.
- Arabic and Urdu are in the set, so **do not hard-code left-to-right layout assumptions**.

Other Web services worth knowing before adding a feature: `EmployeeAccountService` (account
creation + invite), `AccountEmailSender`/`SmtpAccountEmailSender` (setup links; falls back to a
dev link when SMTP is unconfigured), `EmployeeCsvExportService` (the region-scoped export behind
`/api/employees/export.csv`), `ThemeState` (dark/light, persisted), `LanguagePreferenceService`.

## Delegation = borrowed accounts (not delegated permissions)

Added 2026-08-08. A delegate **signs in as** the delegator for the delegation window, rather
than gaining rights while staying themselves. Chosen deliberately over permission delegation:
the UX is "I see what they see", and it covers admin duties, which a per-action permission
model did not. **The trade-off is accepted, not overlooked** — while borrowing, the delegate
can read everything that account can (contracts, sick-leave reasons), and reads leave no
trace. The compensating controls below are what make that acceptable; don't remove them.

- **`Web/Security/ActingUser.Resolve(ClaimsPrincipal)` is the only code that reads the
  impersonation claims** (`RealUserId`, `RealUserName`, `DelegationId` in
  `ImpersonationClaims`). Endpoints call it with `HttpContext.User`; components go through
  the scoped `ActingContext`. Do not re-derive "who is really acting" anywhere else.
- **Switching is two minimal APIs**, for the same reason login is one — a circuit cannot
  write the auth cookie: `POST /api/auth/impersonate` (form-posted `delegationId`) and
  `GET /api/auth/impersonate/stop`. Landing page comes from `HomeRouteResolver`, as at login.
- **Every rule lives in `Application/Contexts/ImpersonationContext`.** The endpoints only
  turn its result into a redirect. `ValidateDelegationAsync` is re-run before *every*
  borrowed action (via `ManagerContext.GuardAsync`) because the 5 h cookie outlives the
  delegation; it also checks the *delegate's* own status, which Identity never revalidates
  since the cookie belongs to the borrowed account.
- **Chaining is refused from the cookie** (`acting.IsImpersonating` at the endpoint), not
  from an open `ImpersonationSession` row — a row survives sign-out and cookie expiry, and
  using it as the signal locked people out permanently. Sign-out closes the row.
- `ManagerContext.DecideRequestAsync` / `ExtendContractAsync` / `TerminateContractAsync` /
  `CreateDelegationAsync` all take an optional **`ActingOnBehalf`** (real user + delegation),
  supplied by the page, null when acting as yourself. Explicit rather than ambient. Any new
  delegatable action must take it too, or it escapes the guard and the audit.
- **`DelegatedAction`** is the audit trail: written once per borrowed action, never updated,
  so it stays true when the reporting graph changes. Notifications name both —
  "approved by Line Manager X (delegate: Y)".
- **UI signals**: the app bar turns Material Red 700 with the borrowed name and an exit
  button, the drawer shows that account above yours, and terminating a contract from a
  borrowed account asks first, naming both identities.
- History lives at `/manager/delegation-history` — two personal tabs, plus a region-scoped
  "everyone" tab for Admins that `ManagerContext` refuses for anyone else (hiding the tab is
  convenience, not the control). Nav entry appears only for admins and people who have
  actually delegated or been delegated to.
- **Admins must nominate a stand-in before taking leave**: nobody reviews their requests, so
  `EmployeeContext.SubmitRequestAsync` throws `DelegationRequiredException` unless an active
  delegation overlaps the period. `EmployeeCalendar` catches that one exception, offers the
  dialog pre-filled with the leave dates, and retries the submit **once**.
  - That path is the one place an *employee* page injects `EmployeeContext`/`ManagerContext`
    directly instead of `ITimeOffService`. Deliberate: delegation has no business on that
    interface, and adding it would drag it into `InMemoryTimeOffService` too. Treat it as an
    exception, not precedent.

## Notifications (in-process dispatcher)

- **No SignalR hub any more** (removed 2026-08-04). `Application/Hubs/NotificationHub` was
  mapped at `/notificationHub` with no `[Authorize]` and a `Register(string userId)` that
  trusted its argument, so any client could subscribe to another user's notifications. In
  Blazor Server the bell already runs on the server, so the hub was a loopback: the server
  connected to itself to deliver something it was holding. Don't re-add `MapHub`.
  (`AddSignalR` in `Program.cs` **stays** — its `HubOptions` also configure the Blazor
  circuit, and removing it drops `MaximumReceiveMessageSize` to the 32 KB default.)
- `Application/Notifications/INotificationDispatcher` (**singleton**) replaces it: an
  in-process fan-out keyed by user id. `NotificationContext` publishes; components
  `Subscribe` and dispose the returned handle. Both publish paths hand off **without
  awaiting** the subscribers — a handler ends in a Blazor render, and an approval must not
  wait on the recipient's browser. A delivery that throws is logged and its subscription
  dropped.
- Subscribers receive a `NotificationChange`: `Created` holds the new row, or is null when
  only read state moved (`MarkAsRead`/`MarkAllAsRead` publish this), which subscribers
  answer by re-reading. That is what keeps the bell's badge in step when the history page
  marks something read — the bell sits in the layout and survives that navigation.
- `Web/Components/NotificationBell.razor` (in the `EmployeeLayout` AppBar) shows the newest
  8, read and unread, and `Components/Employee/Pages/NotificationsHistory.razor`
  (`/employee/notifications`) the paged full list. `Components/NotificationRow.razor` is the
  shared row. The history page is reachable **only** from the bell's "See all" entry —
  deliberately not in the drawer nav.
- The bell resolves the user id from the auth state and uses `IServiceScopeFactory`: it
  shares the layout's scoped `DbContext` otherwise, and a second in-flight query throws.
  Its handler does every state mutation **inside `InvokeAsync`** — the dispatcher calls it
  from the publisher's thread, so touching the list outside the circuit races the renderer.
- `NotificationContext.MarkAsReadAsync(userId, notificationId)` is scoped to the owner; an
  id alone must not be enough to flip someone else's row.
- `ManagerContext.DecideRequestAsync` is the manager approval flow (approve/decline a pending
  request + notification to the requester); `EmployeeContext.HrDecideRequestAsync` (added
  2026-07-28) is the equivalent for HR's review from `/hr/dashboard` — same shape (status
  change + `LeaveApproval` + best-effort notification, decision still saved even if the
  notification send fails).

## Other UI in the tree (know before styling)

Several styling worlds coexist; only the Fluent UI one above is on the live path from login.

> The **retro/vintage-desktop Dashboard** (`Components/Pages/Dashboard.razor` at
> `/manager/dashboard`, plus its ~21 components — `Sidebar`, `TopBar`, `Card`, `RetroButton`,
> `ManagerOverview`, `TeamCalendarView`, … — and `LeaveTone.cs`) was **deleted on
> 2026-08-04**. It was an in-memory mockup on a route nothing linked to, and its component
> graph was closed: nothing outside it referenced any of the 22 files. The real manager view
> is `/manager/team` (`ManagerDashboard.razor`). `git show 9c3d804` and earlier still has it if
> anything needs recovering.

1. **CSS Isolation** (not a design system, but it trips people up project-wide): each `Foo.razor` has
     a co-located `Foo.razor.css`. A rule only applies to markup *authored directly* in that
     component's own markup — RenderFragment content (e.g. `Card`'s `ChildContent`) is scoped to
     the *passing* component, and **a child component's own rendered root is never reachable at
     all** without `::deep` (Blazor never emits a scope attribute onto a child component's
     output — `<FluentCard Class="foo">` means `.foo` alone can't match the card's root div, full
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
   - `wwwroot/css/tokens.css` (`--color-*`, `--space-*`, `--font-mono`, …) outlived the retro
     dashboard it was built for: `MainLayout.razor.css` and `NavMenu.razor.css` still consume
     it. Anything else styling those two should use `var(--color-teal)` etc., never hex.
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

On these older pages icons are inline SVGs; the Fluent pages use the icon package instead
(`Icons.Regular.Size20.*`). GSAP + ScrollTrigger are loaded **from the cdnjs CDN** in `App.razor`
(plus `/pointer.js` and `/anim.js`) but are **dormant** — the Login has no `data-*` anim
attributes; the declarative contract (`data-reveal`, `data-intro`, …) in `wwwroot/js/anim.js`
still works if markup opts in. `Login.razor` uses `@layout AuthLayout` (bare `@Body`).

## Working conventions

- Team project (4 devs) — avoid unrequested refactors of others' code; keep changes scoped.
- Follow the layer flow for new features: gateway interface in Domain, implementation in
  Gateway, logic in an Application context, thin mapping in a Web service. Don't add a new
  context class for a handful of pass-throughs — extend `EmployeeContext`.
- `TODO.md` is stale (pre-rearchitecture).
- New user-facing text means a new key in **all** `Web/Languages/*.json`, not just `en.json`.
- Any event handler that touches the database from a Razor page needs a `try/catch` that reports
  through `IToastService`. Blazor Server tears down the circuit on an unhandled exception, which
  looks to the user like the page freezing or silently ignoring them — the department-assignment
  bug hid behind exactly that for weeks.
- Keep this file current as structure lands.
