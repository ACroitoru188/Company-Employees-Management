# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers.

- `src/CompanyEmployees.Web` — Blazor Web App (**.NET 9** / C#), **Interactive Server** render
  mode. References `CompanyEmployees.Data` and registers `ApplicationDbContext` via DI in
  `Program.cs` (`ConnectionStrings:Default` in `appsettings.Development.json`, LocalDB).
  `Login.razor` (route `/`) is a **MudBlazor mock sign-in** since July 2026: any well-formed
  credentials navigate to `/employee/dashboard` — the earlier DB-backed check
  (`PasswordHasher<Employee>` against `Employee.PasswordHash`) was removed with the redesign and
  should return via Identity/SignInManager when real auth lands. The **live Employee UI** is the
  MudBlazor page set under `/employee/*` (see Architecture, system 4). `Dashboard.razor`
  (route `/dashboard`, retro shell) is a **UI mockup** — role switch, in-shell view switch,
  request flow, approve/decline — composed from ~20 reusable components under
  `Components/*.razor`; it is no longer reachable from the login flow (direct URL only).
- `src/CompanyEmployees.Data` — data layer (.NET 8 / C#), Entity Framework Core 8 (Code-First),
  SQL Server LocalDB.

## Commands

Run from the repo root. `dotnet-ef` is a local tool (see `dotnet-tools.json`) — after a fresh
clone run `dotnet tool restore` once.

```sh
dotnet build                                             # build the solution (CompanyEmployees.slnx)
dotnet run --project src/CompanyEmployees.Web            # build + run, http://localhost:5269
dotnet run --project src/CompanyEmployees.Web --no-build # run WITHOUT rebuilding (after a build)
dotnet watch --project src/CompanyEmployees.Web          # run with hot reload
dotnet dotnet-ef migrations add <Name> --project src/CompanyEmployees.Data
dotnet dotnet-ef database update --project src/CompanyEmployees.Data  # requires LocalDB
```

- Ports (from `launchSettings.json`): `http` profile → http://localhost:5269; `https` profile →
  https://localhost:7248. Pick a profile with `--launch-profile http|https`.
- On Windows a running instance **locks `CompanyEmployees.Web.exe`**, so `dotnet build` fails with
  MSB3027/MSB3026 (file-in-use) rather than a compile error. Stop the running app first
  (`taskkill /F /IM CompanyEmployees.Web.exe`) before rebuilding.
- In Development, `Program.cs` seeds a demo login (`demo@siemens.com` / `Passw0rd!`) into the
  `Employees` table on startup if it doesn't already exist — that's how you sign in locally today.
- No test project exists yet. When one is added, wire up `dotnet test` and document it here.

## UI history

The login and dashboard started as a faithful port of a **Claude Design** mockup
("Employee leave management system", cream/indigo, inline styles for fidelity — re-readable via
the **`DesignSync`** tool / `/design-sync` skill if ever needed). In July 2026 the UI was
**redesigned to a corporate, Siemens-authentic look** and the mockup stopped being the source of
truth; the fidelity-driven inline-style exception died with it.

Later in July 2026, the Dashboard shell (not Login) was restyled again to a **retro/vintage-desktop
look** (cream/teal/navy/coral, thick solid-black borders, no gradients/shadows/animations, monospace
font) and, at the same time, refactored from one large `Dashboard.razor` file into ~20 reusable
components under `Components/*.razor` with **CSS Isolation** (`Foo.razor.css` co-located per
component) — see Architecture below. `Login.razor` was deliberately left untouched by both changes
and kept the corporate Siemens palette and the shared `app.css`.

Also in July 2026, a **MudBlazor (Material Design) Employee UI** was added per the spec in
`CLAUDE-instructions-for-frontend/` — three new pages under `/employee/*` plus a rewritten
`Login.razor` (design system 4 below). It deliberately does **not** replace the retro Dashboard
shell; the two coexist on separate routes.

## Project layout (Data)

```
src/CompanyEmployees.Data/
  Entities/              # Employee (: IdentityUser<int>), Department, Role (: IdentityRole<int>,
                         # keeps Color/Position/Permissions) and the Permission [Flags] enum
                         # (Discord-style bitmask stored as long)
  ApplicationDbContext.cs      # IdentityDbContext<Employee, Role, int>; all Fluent API config in
                               # OnModelCreating + HasData seed of the 3 default roles (Admin,
                               # Department Manager, Employee)
  Services/PermissionService.cs # HasPermission(employee, permission): ORs permissions across all
                                # of the employee's roles; Administrator flag overrides everything
  DesignTimeDbContextFactory.cs # hardcoded LocalDB connection string, used only by dotnet ef
                                # design-time tooling (separate from the Web app's own DI-registered
                                # connection string in appsettings.Development.json)
  Migrations/            # EF Core migrations (InitialCreate)
```

## Architecture

Four design systems coexist — know which one a page uses before styling it:

1. **Retro/vintage-desktop system** (the live Dashboard shell: everything under
   `Components/*.razor` that `Pages/Dashboard.razor` composes — `Sidebar`, `SidebarLink`,
   `TopBar`, `Card`, `RetroButton`, the per-view components like `EmployeeOverview` /
   `ManagerOverview` / `MyRequestsView` / `TeamCalendarView`, etc.).
   - **CSS Isolation, not global classes**: each `Foo.razor` has a co-located `Foo.razor.css`
     scoped by Blazor to that component only. A rule only applies to markup *authored by* the
     component that declares it — markup passed to another component as a `RenderFragment`
     (e.g. `Card`'s `ChildContent`/`HeaderAction`) is scoped to the *passing* component, not the
     one rendering it, and `::deep` only pierces into a real child `<Component/>`, never into a
     RenderFragment's own markup. Getting this wrong (CSS left in the wrong file after moving
     markup between components) silently drops styling with no compiler error — verify visually
     after any such move, and check the compiled `obj/**/scopedcss/**/*.bundle.scp.css` for the
     expected `[b-xxxxxxxx]` scope if something doesn't apply.
   - **Design tokens**: `wwwroot/css/tokens.css` (linked in `App.razor` alongside `app.css`) is
     the single source of truth for this system's colors/spacing/radius/font as CSS custom
     properties (`--color-*`, `--space-*`, `--radius-*`, `--border-width*`, `--font-mono`).
     Components consume `var(--color-teal)` etc., never hardcoded hex. `Login.razor` is
     deliberately **not** on this system and keeps its own `--l-*` tokens in `app.css`.
   - Palette: cream `#EDE1C7` background, white surfaces, ink `#17181C` for text/borders, teal
     `#7CC6BD` as the primary accent, navy `#1A2340` for the sidebar/topnav chrome, coral
     `#E8A084` as the secondary accent. Thick solid borders (`--border-width` 2.5px) everywhere,
     moderate radii, **no gradients, shadows, or transitions/animations** — flat and static by
     design. Leave-type tone keys (`accent|amber|purple|gray|green`) map to `LeaveTone.cs` and
     `.tone-*`/`.dot-*`/`.fill-*`-style classes per component, same pattern as system 2 below.
   - **Shell**: `Sidebar` (fixed-width, left, vertical nav — `Dashboard`/`My requests`/
     `Calendar`/`Team`/`Reports`, the last two inert placeholders) + a right column split into a
     thin `TopBar` (role toggle, notifications, request CTA, profile menu) and a single bordered
     `.content-panel` that hosts whichever in-shell view is active, with a decorative
     `.chrome-strip` (2 dots + `ViewTitle`) as its first child. All of this lives inside
     `Dashboard.razor`, which overrides the default layout with `@layout AuthLayout` (a bare
     `@Body` shell) rather than using `MainLayout` — the shell is coupled to page state (active
     view, role) so splitting it into a `LayoutComponentBase` would mean plumbing that state
     across a layout boundary.

2. **Corporate Siemens system** (`Pages/ForgotPassword.razor` only — `Login.razor` left this
   system for MudBlazor in July 2026, but the `.lm` CSS it used remains for ForgotPassword).
   - **Class-based**, not isolated: styling lives in the `.lm` section of `wwwroot/app.css`
     (tokens as `--l-*` CSS vars on `.lm`). Inline `style="…"` is reserved for data-driven values
     only. Every root element carries `class="lm"`.
   - Palette: page bg `#F4F5F7`, white surfaces, hairline borders `#E4E7EC`/`#EDEFF2`, ink
     `#000028`, petrol `#007993` as the single accent (hover `#00646E`, tint `#E5F3F5`).
   - `app.css`'s `.lm` block now holds **only** what Login/ForgotPassword actually use
     (`.microlabel`, `.btn-pri`, `.login-*`) — the rest (`.card`, `.pill`, `.tone-*`, `.topnav`,
     `.kpis`, …) moved to system 1's isolated `.razor.css` files when the Dashboard shell was
     rebuilt; don't re-add Dashboard-only rules here.

3. **Old design system** (legacy: `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, and the
   bulk of `wwwroot/app.css` above the `.lm` section — a light-teal→indigo ramp now, but the
   token names still say celadon/teal; `.stat`/`.panel`/`.auth` classes).
   - `Routes.razor` still sets `DefaultLayout = MainLayout`, so this shell backs the pages that
     *don't* override it: `NotFound.razor` (explicit `@layout MainLayout`) and `Error.razor`
     (implicit, no `@layout` at all). It was itself restyled to the retro palette (own
     `MainLayout.razor.css`/`NavMenu.razor.css`, consuming `tokens.css`) even though it's not
     part of system 1 or reachable from the main app flow — it's still the fallback shell for
     error/not-found, not dead code to delete.

4. **MudBlazor / Material system** (the live Employee flow: `Pages/Login.razor` at `/` and the
   pages under `Components/Employee/Pages/` — `EmployeeDashboard` `/employee/dashboard`,
   `EmployeeCalendar` `/employee/calendar`, `MyRequests` `/employee/my-requests`).
   - **MudBlazor 8.x** (`PackageReference` in the Web csproj; `AddMudServices()` in `Program.cs`;
     `MudBlazor.min.css`/`.js` linked in `App.razor` by plain `_content/MudBlazor/…` href — RCL
     assets don't go through `@Assets[]`). Standard Material theme, `Primary`/`AppbarBackground`
     = `Colors.Blue.Darken1`.
   - **Providers live in `Components/Employee/EmployeeProviders.razor`, included at the top of
     every employee page — NEVER in the layout.** With per-page render modes the layout renders
     statically, so `MudPopoverProvider`/`MudDialogProvider`/`MudSnackbarProvider` placed there
     never register with the circuit and every tooltip/dialog/snackbar crashes it
     ("Missing <MudPopoverProvider />"). `EmployeeProviders` also owns the shared `MudTheme`.
   - Shell: `Components/Layout/EmployeeLayout.razor` (`MudAppBar` + persistent `MudDrawer`
     "WORKSPACE" nav + user footer), used only by the three `/employee/*` pages via `@layout`.
     The layout is static chrome — anything interactive must be in the page.
   - Leave-type color map lives in `Components/Employee/LeaveTypePalette.cs`
     (Annual→Blue, Sick→Orange.Darken1, Parental→DeepPurple, Unpaid→Gray.Darken1 — MudBlazor
     spells it `Colors.Gray`), applied via inline `Style` since these aren't theme `Color` enum
     members. `LeaveBalanceSummary.razor` is the shared header+balance-cards component
     (Dashboard passes the CTA through its `Actions` RenderFragment; Calendar passes nothing).
   - **Mock data layer**: `Models/` (LeaveType, RequestStatus, TimeOffRequest, LeaveBalance,
     TeamMember) + `Services/ITimeOffService` with `InMemoryTimeOffService` registered **Scoped**
     (per circuit: submitted requests survive navigation between employee pages, reset on F5).
     Swap the implementation for a real API client without touching the Razor components. Seed
     dates are relative to the current month so the demo never goes stale; Sick is seeded with 0
     days remaining to demo the disabled type chip on Calendar.
   - Date formatting: always `CultureInfo.InvariantCulture` (server culture may not be English).

### Interactivity

Both `Login.razor` and `Dashboard.razor` are `@rendermode InteractiveServer` (registered in
`Program.cs` via `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode`).
`Dashboard.razor` is the **single owner of all app state** in its `@code` — role
(employee/manager) switch, an in-shell view switch (`_view`: dashboard / requests / calendar —
views, not routes, so submitted requests and role survive navigation), the request slide-over
with a working range-select calendar, a month calendar page fed by `_events` + dated
`_myRequests`, cancel/approve/decline, and a toast — none of it persisted yet. It never renders
raw markup for these itself; it maps its private records to each child component's own record
types (e.g. `_myRequests` → `MyRequestsView.RequestEntry`) and passes them down as `[Parameter]`s,
wiring child `EventCallback`s back to its own methods (`GoDashboard`, `Approve`, `ClickDay`, …).
When adding a new piece of Dashboard state, keep it in `Dashboard.razor` and thread it through
parameters/callbacks rather than letting a child component own its own copy. `Login.razor` is a
**mock** since the MudBlazor redesign — no DB query, no cookie/session; any well-formed
credentials navigate to `/employee/dashboard` (the old `PasswordHasher<Employee>` check is gone,
to be reinstated via Identity/SignInManager). Add `@rendermode InteractiveServer` per-component
when a page needs interactivity — and remember the layout it uses stays static (see system 4's
provider rule).

### Animation layer (GSAP) — dormant since the Login rewrite

GSAP + ScrollTrigger are vendored in `wwwroot/lib/` (no CDN) and driven by `wwwroot/js/anim.js`.
The old corporate Login was its only consumer; the MudBlazor Login has no `data-*` anim
attributes, so the layer currently animates nothing (ForgotPassword may still carry attributes).
The scripts remain loaded in `App.razor` — harmless, but remove them if ForgotPassword also
migrates.
The contract is declarative data-attributes on markup — no C#/JS interop:
`data-reveal` (scroll entrance, rise + blur-in), `data-reveal="stagger"` (container's children
cascade in), `data-intro` (login entrance timeline; headings get a clip-path mask reveal),
`data-drift` (scrubbed greeting parallax), among others. Re-init after Blazor re-renders is
automatic via a MutationObserver in anim.js. A sync `<head>` script in `App.razor` adds the `anim`
class that pre-hides targets — reduced-motion/no-JS users see everything statically. The retro
Dashboard shell (design system 1) is deliberately static per its "no animations" rule: `app.css`
force-overrides `.anim .lm [data-reveal]` etc. to `opacity: 1 !important` so this layer has no
visible effect there even though the `data-*` attributes may still be present in older markup —
don't rely on GSAP-driven motion for anything under `Components/*.razor`.

### Data

- **Auth model is ASP.NET Core Identity** (July 2026, replacing the earlier custom-only bitmask
  system): `Employee : IdentityUser<int>`, `Role : IdentityRole<int>`. The bitmask permission
  layer survives on top of it — `Role.Permissions` (`[Flags] enum`) is OR'd across an employee's
  roles via `PermissionService.HasPermission`, with `Administrator` overriding everything.
  Identity's own tables are remapped to domain names: `Employees`, `Roles`, and `EmployeeRoles`
  (the `IdentityUserRole<int>` join, exposed as `Employee.Roles`/`Role.Employees` skip
  navigations); the rarely-used ones keep their `AspNet*` defaults. Role seeds carry **fixed
  `ConcurrencyStamp` values** — keep them fixed, or every model change regenerates the seed data.
  The Web project's `Login.razor`/`Program.cs` predate this migration (they built `Employee`
  objects and verified `PasswordHash` directly against the old plain-`Employee` shape) and need
  rework to match the new `IdentityUser<int>`-based entity — see the note in that project's own
  `Interactivity` section above once updated.
- Delete behaviors: `Employee.DepartmentId` is `SetNull` (deleting a department keeps its
  employees); `Department.ManagerId` is `NoAction` because SQL Server rejects referential-action
  cycles with the other FK — detach a manager before deleting them. `EmployeeRole` cascades both ways.
- Firing an employee = soft delete via `Employee.IsActive`, not row deletion.
- `Email`, `PhoneNumber` and `PasswordHash` live on `IdentityUser` now (hashing comes from
  Identity's `PasswordHasher` once sign-in is wired up); `Email` keeps a unique index on top of
  Identity's defaults.
- TFM mismatch is deliberate: the Data project targets `net8.0` (per assignment spec) and the Web
  project `net10.0` — referencing the lower-TFM library from Web works fine.

## Working conventions

- Team project (4 devs) — avoid unrequested refactors of others' code; keep changes scoped.
- Icons are inline SVGs (no icon package).
- `TODO.md` at the repo root tracks the production-readiness checklist (DB wiring, auth, security
  hardening, testing, deployment) — check it before starting infra-adjacent work to avoid
  duplicating what's already planned or done.
- Keep this file current as real structure lands: add `dotnet test` when a test project exists, and
  document architectural patterns (layering, auth) as they're established.
