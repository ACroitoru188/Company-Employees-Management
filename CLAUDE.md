# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers.

- Stack: Blazor Web App (.NET 10 / C#), **Interactive Server** render mode available.
- Single project so far: `src/CompanyEmployees.Web`. No database, API, or auth yet. The
  login + dashboard are a **static UI mockup** (sample data hardcoded in `@code`, marked with
  `ponytail:` comments). Real functionality is layered on later.

## Commands

Run from the repo root.

```sh
dotnet build                                             # build the solution (CompanyEmployees.sln)
dotnet run --project src/CompanyEmployees.Web            # build + run, http://localhost:5269
dotnet run --project src/CompanyEmployees.Web --no-build # run WITHOUT rebuilding (after a build)
dotnet watch --project src/CompanyEmployees.Web         # run with hot reload
```

- Ports (from `launchSettings.json`): `http` profile → http://localhost:5269; `https` profile →
  https://localhost:7248. Pick a profile with `--launch-profile http|https`.
- On Windows a running instance **locks `CompanyEmployees.Web.exe`**, so `dotnet build` fails with
  MSB3027/MSB3026 (file-in-use) rather than a compile error. Stop the running app first
  (`taskkill /F /IM CompanyEmployees.Web.exe`) before rebuilding.
- No test project exists yet. When one is added, wire up `dotnet test` and document it here.

## UI history

The login and dashboard started as a faithful port of a **Claude Design** mockup
("Employee leave management system", cream/indigo, inline styles for fidelity — re-readable via
the **`DesignSync`** tool / `/design-sync` skill if ever needed). In July 2026 the UI was
**redesigned to a corporate, Siemens-authentic look** and the mockup stopped being the source of
truth; the fidelity-driven inline-style exception died with it.

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

`Dashboard.razor` is `@rendermode InteractiveServer` (registered in `Program.cs` via
`AddInteractiveServerComponents` / `AddInteractiveServerRenderMode`). It holds all app state in
`@code` — role (employee/manager) switch, an in-shell view switch (`_view`: dashboard /
my requests / calendar — views, not routes, so submitted requests and role survive navigation),
the request slide-over with a working range-select calendar, a month calendar page fed by
`_events` + dated `_myRequests`, cancel/approve/decline, and a toast. `Login.razor` is static
SSR: the form is a `GET` to `/dashboard`
(no auth). Add `@rendermode InteractiveServer` per-component when a page needs interactivity.

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

## Working conventions

- Team project (4 devs) — avoid unrequested refactors of others' code; keep changes scoped.
- Icons are inline SVGs (no icon package).
- Keep this file current as real structure lands: add `dotnet test` when a test project exists, and
  document architectural patterns (layering, EF Core, auth) as they're established.
