# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers.

- `src/CompanyEmployees.Web` — Blazor Web App (.NET 10 / C#), **Interactive Server** render mode.
  References `CompanyEmployees.Data` and registers `ApplicationDbContext` via DI in `Program.cs`
  (`ConnectionStrings:Default` in `appsettings.Development.json`, LocalDB). `Login.razor` is the
  first page wired to a real flow: it verifies email/password against `Employee.PasswordHash`
  directly from its `@code` block (no separate API layer — Blazor Server already runs this
  server-side). `Dashboard.razor` is still a **static UI mockup** (sample data hardcoded,
  `ponytail:`-marked). No cookie/session auth yet — a successful login just navigates to
  `/dashboard`.
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

Two design systems coexist — know which one a page uses before styling it:

1. **Leave Management design system** (the live UI: `Pages/Login.razor`, `Pages/Dashboard.razor`).
   - **Class-based**: all styling lives in the `.lm` section at the bottom of `wwwroot/app.css`
     (design tokens as `--l-*` CSS vars on `.lm`; system font stack, no web-font dependency).
     Inline `style="…"` is reserved for **data-driven values only** (meter widths, month-bar
     heights). Every root element carries `class="lm"`.
   - Palette (Siemens-authentic): page bg `#F4F5F7`, white surfaces, hairline borders
     `#E4E7EC`/`#EDEFF2`, ink `#000028`, petrol `#007993` as the **single accent** (hover
     `#00646E`, tint `#E5F3F5`). Muted status colors: green `#0F7B3D`, red `#C4314B`, amber
     `#B26205`. Leave types are tone keys — `TypeTone()` in `Dashboard.razor` maps a type to
     `accent|amber|purple|gray`, consumed as `.tone-*` (tint bg + ink), `.dot-*` and `.fill-*`
     classes. The other `@code` helpers (`Pill`, `CalCells`) likewise return class strings, not
     hexes.
   - **Shell: sticky top nav** (`.topnav` — no sidebar) with brand, nav links (+ pending badge),
     role segmented control and the request CTA; content in a centered `.container`
     (max-width 1240px). KPIs are one hairline-divided card (`.kpis`/`.kpi`); lists are flat
     divided rows (`.rows`/`.row`). The request form is a **slide-over panel** (`.sheet`), not a
     centered modal.
   - **Both pages override the default layout with `@layout AuthLayout`** (a bare
     `@Body` shell). `Dashboard.razor` renders its *own* top nav rather than using
     `MainLayout`, because that chrome is coupled to page state (role switch, pending badge,
     request trigger); splitting it into a layout would mean plumbing state across components.

2. **Old design system** (legacy: `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, and the
   bulk of `wwwroot/app.css` — a light-teal→indigo ramp now, but the token names still say
   celadon/teal; `.stat`/`.panel`/`.auth`/`.sidebar` classes).
   - `Routes.razor` still sets `DefaultLayout = MainLayout`, so this shell now only backs the
     pages that *don't* override it: `Error.razor` and `NotFound.razor`. `MainLayout`/`NavMenu`
     and most `app.css` classes are otherwise unused by the current UI.

### Interactivity

Both `Login.razor` and `Dashboard.razor` are `@rendermode InteractiveServer` (registered in
`Program.cs` via `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode`).
`Dashboard.razor` holds all app state in `@code` — role (employee/manager) switch, an in-shell
view switch (`_view`: dashboard / my requests / calendar — views, not routes, so submitted
requests and role survive navigation), the request slide-over with a working range-select
calendar, a month calendar page fed by `_events` + dated `_myRequests`, cancel/approve/decline,
and a toast — none of it persisted yet. `Login.razor` queries `ApplicationDbContext` directly and
verifies the password with `PasswordHasher<Employee>` (same hasher ASP.NET Core Identity uses
internally); there's no cookie/session yet, so a successful check just navigates to `/dashboard`
with no actual signed-in state. Add `@rendermode InteractiveServer` per-component when a page
needs interactivity.

### Animation layer (GSAP)

GSAP + ScrollTrigger are vendored in `wwwroot/lib/` (no CDN) and driven by `wwwroot/js/anim.js`.
The contract is declarative data-attributes on markup — no C#/JS interop:
`data-reveal` (scroll entrance, rise + blur-in), `data-reveal="stagger"` (container's children
cascade in), `data-grow` / `data-grow="height"` (bars grow to their inline size), `data-count`
(count-up numbers, decimals/suffix preserved), `data-donut="36"` (sweeps the `--p` var read by
the donut's conic-gradient), `data-draw` (line draws with scrubbed scroll — the activity rail),
`data-intro` (login entrance timeline; headings get a clip-path mask reveal), `data-drift`
(scrubbed greeting parallax). Re-init after Blazor re-renders (role switch, enhanced nav) is
automatic via a MutationObserver in anim.js. A sync `<head>` script in `App.razor` adds the
`anim` class that pre-hides targets (see `.lm` CSS) — reduced-motion/no-JS users see everything
statically, matching `pointer.js`.

### Dismiss-with-animation pattern

The request slide-over animates *out* by keeping the element mounted during the exit:
`CloseRequest` sets a `_requestClosing` flag (adding a `.closing` class that swaps the CSS
animation to `sheetOut`/`fadeOut`), awaits a short `Task.Delay`, then unmounts. Reuse this
pattern for other dismiss-with-animation cases.

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
