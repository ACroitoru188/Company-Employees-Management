# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Company Employees Management — an internship project built by a team of 4 developers.

- Stack: Blazor Web App (.NET 10 / C#), **Interactive Server** render mode.
- Single project so far: `src/CompanyEmployees.Web`. No database, API, or auth yet — the
  login and dashboard are a static UI mockup (sample data hardcoded, marked with `ponytail:`
  comments). Real functionality is added on top later.

## Commands

Run from the repo root.

```sh
dotnet build                              # build the solution (CompanyEmployees.sln)
dotnet run --project src/CompanyEmployees.Web            # build + run, http://localhost:5269
dotnet run --project src/CompanyEmployees.Web --no-build # run WITHOUT rebuilding (after a build)
dotnet watch --project src/CompanyEmployees.Web         # run with hot reload
```

- Ports (from `launchSettings.json`): `http` profile → http://localhost:5269; `https` profile →
  https://localhost:7248. Pick the profile with `--launch-profile https` if needed.
- No test project exists yet. When one is added, wire up `dotnet test` and document it here.

## Project layout

```
CompanyEmployees.sln
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
```

## Conventions & architecture

- **Styling:** one global stylesheet, `wwwroot/app.css` — CSS custom properties for tokens, neutral
  slate base + a single blue accent (`--accent`), system font stack (no web-font dependency). Bootstrap
  was removed from the template. Keep new UI on these tokens; don't reintroduce a CSS framework without
  team agreement.
- **Icons** are small inline SVGs (no icon package). If a component library is later added, swap them.
- Pages are currently static SSR (no `@rendermode`). Add `@rendermode InteractiveServer` per-component
  when a page needs interactivity.

## Working conventions

- This is a team project (4 devs) — avoid unrequested refactors or restructuring of code written by
  others; keep changes scoped to what's asked.
- Keep this file current as real structure lands: add `dotnet test` when a test project exists, and
  document architectural patterns (layering, EF Core, auth) as they're established.
