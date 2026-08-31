# Siemens Employee Management Portal

An enterprise-grade portal platform designed to streamline employee data management, time-off tracking, organizational hierarchy, and workforce administration.

## 📖 Overview

The Employee Management Portal is a comprehensive internal tool that replaces manual HR tracking with an automated, hierarchical system. It features an interactive organizational chart, dynamic calendar views, real-time notifications, and a robust dual-database high-availability architecture.

## 🚀 Key Features

*   **Hierarchical Approval Workflow:** Automated routing of requests based on the organizational structure (Employee -> Line Manager -> Country Manager).
*   **Dynamic Organizational Chart:** Visual representation of the company hierarchy grouped by regions, departments, and managerial lines.
*   **Interactive Calendars:** Personal and departmental views of upcoming leaves, public holidays, and team availability.
*   **Real-time Notifications:** In-app alerts for request submissions, approvals, rejections, and system status changes.
*   **Contract & Quota Management:** Tracking of indefinite and fixed-term contracts, calculating accurate leave quotas for each employee.
*   **High Availability & Dynamic Database Providers:** Active-passive dual database setup (PostgreSQL / SQL Server) with automatic synchronization, health monitoring, and seamless failover.
*   **Containerized Architecture:** Fully dockerized full-stack deployment with Docker Compose.

## 💻 Tech Stack

### Frontend
*   **Framework:** Blazor Web App (.NET 9, Interactive Server)
*   **UI Library:** Microsoft Fluent UI Blazor Components (customized with corporate branding)
*   **Animation & Scripting:** GSAP (GreenSock), custom pointer and animation utilities
*   **Styling:** CSS/SCSS (custom theming, Light/Dark modes)

### Backend & Persistence
*   **Framework:** ASP.NET Core (.NET 9) / SignalR
*   **ORM:** Entity Framework Core 9
*   **Pluggable Database Providers:** Microsoft SQL Server 2022 & PostgreSQL 16
*   **Authentication & Security:** ASP.NET Core Identity & Persistent Data Protection Keyring
*   **Infrastructure:** Docker & Docker Compose

## 👥 User Roles & Permissions

The application implements a strict role-based access control (RBAC) system:

### 1. Employee
The standard user profile for company staff.
*   Can view their own calendar and available holiday quotas.
*   Can submit, edit, or cancel personal time-off requests.
*   Can view the organizational chart and see colleagues within their department.

### 2. Line Manager
Leads a specific department or team.
*   Inherits all Employee privileges.
*   Receives and processes (Approve/Reject) requests from their direct reports.
*   Can view the team's aggregated calendar to ensure adequate department coverage.

### 3. Country Manager
Oversees an entire region or country's operations.
*   Inherits Line Manager privileges.
*   Approves/Rejects requests submitted by Line Managers.
*   Has a broader view of the region's organizational chart and statistics.

### 4. Admin
System administrator responsible for platform maintenance and HR data.
*   Full access to user management (Create, Edit, Deactivate employees).
*   Manages employment contracts (determinate/indeterminate), job titles, and region assignments.
*   Monitors system health, database replication status, and can trigger manual database failover.

---

## 🐳 Running the Full Stack with Docker (Recommended)

The entire solution — including the web application and both database engines — is containerized and orchestrated via Docker Compose.

### 1. Start all containers
Run the following command from the repository root:

```bash
# First time or after code changes (recompiles image):
docker compose up --build -d

# Regular daily starts (instant, no rebuild):
docker compose up -d
```

> **Quick Commands:** Pause with `docker compose stop`, resume instantly with `docker compose start`.

This starts three services:
*   `company-employees-app` — The .NET 9 Blazor Web Application (listening on port `8080`)
*   `company-employees-sqlserver` — Microsoft SQL Server 2022 (listening on port `1433`)
*   `company-employees-postgres` — PostgreSQL 16 Alpine (listening on port `5432`)

### 2. Open the application
Navigate to **[http://localhost:8080](http://localhost:8080)** in your browser.

*   **First-time Setup Wizard:** On the initial run, the app will display the **Database Setup** page.
    *   Select your **Primary Database** (e.g., *PostgreSQL* with host `postgres` or *Microsoft SQL Server* with host `sqlserver,1433`).
    *   (Optional) Select your **Secondary / Standby Database**.
    *   Click **Test Connection** for each provider.
    *   Click **Save & Complete Setup**. The application will automatically initialize schemas, perform baseline sync, and restart smoothly into the **Login screen**.
*   **Subsequent Runs:** The setup state is persisted across container restarts, and the application opens directly to the Login page.

### 3. Demo Accounts
Default accounts for testing the system:
*   **Admin:** `itadmin@siemens.com` / `User123!`
*   **Country Manager:** `countrymanager.ro@siemens.com` / `User123!`
*   **Line Manager:** `manager.it.ro@siemens.com` / `User123!`
*   **Employee:** `employee.it.ro@siemens.com` / `User123!`

### 4. Stopping and Resetting State
*   **Stop containers (preserving data):**
    ```bash
    docker compose down
    ```
*   **Stop containers and wipe all data (Full Reset):**
    ```bash
    docker compose down -v
    ```

---

## 💻 Local Development Workflow (Hybrid Mode)

For daily UI or backend feature development with Hot Reload:

### 1. Start only the databases in Docker
```bash
docker compose up -d sqlserver postgres
```

### 2. Run the application locally with `dotnet watch`
```bash
dotnet watch --project src/Frontend/CompanyEmployees.Web
```

The app will run locally at **`http://localhost:5269`** (or `https://localhost:7082`), connecting to `localhost:1433` (SQL Server) and `localhost:5432` (PostgreSQL) with instant code hot-reloading on file save.

---

## 🔄 Dual-Database High Availability & Failover

The platform implements an active-passive dual-database architecture with real-time replication and live failover:

```
[ Active Database (e.g. PostgreSQL) ] ────(Outbox Replication)────► [ Standby Database (e.g. SQL Server) ]
                 │                                                                     │
                 └───────────────────────────(Failover Switch)─────────────────────────┘
```

1. **Bidirectional Outbox Replication:** 
   - Every business transaction committed to the active database writes a change envelope to a durable `DatabaseOutbox` table.
   - The background replication worker drains and applies these changes to the standby database every 2 seconds.
2. **Background Health Monitoring:**
   - `DatabaseAvailabilityMonitor` continuously probes both database engines.
   - Real-time status, standby health, and pending change counts are broadcast to administrators via the status banner.
3. **Live Failover Simulation:**
   - Stop the active database to test resilience:
     ```bash
     # If PostgreSQL is active:
     docker compose stop postgres
     
     # If SQL Server is active:
     docker compose stop sqlserver
     ```
   - Within seconds, the admin banner alerts that the active provider is down and presents a **Switch Database** action.
   - Clicking the switch button drains replication, swaps persistent state, restarts the host cleanly, and automatically reloads the browser onto the standby database with zero data loss.
4. **Recovery & Failback:**
   - Restart the recovered database (`docker compose start postgres` / `docker compose start sqlserver`).
   - The monitor detects recovery, syncs queued changes, and offers a **Switch Back** button.

---

## 📦 Docker Volumes Breakdown

Docker named volumes ensure all data survives container restarts and image updates:

| Volume Name | Service | Purpose |
|---|---|---|
| `company-employees-postgres-data` | `postgres` | Persists PostgreSQL database schemas, tables, and records |
| `company-employees-sqlserver-data` | `sqlserver` | Persists Microsoft SQL Server `.mdf` and `.ldf` data files |
| `company-employees-dp-keys` | `app` | Persists ASP.NET Core Data Protection keys (preserves login sessions) |
| `company-employees-app-data` | `app` | Persists runtime `setup-state.json` (remembers setup completion) |

---

## 🧪 Automated Tests

Run the complete test suite from the repository root:

```bash
dotnet test CompanyEmployees.slnx
```

Collect code coverage reports (Coverlet):

```bash
dotnet test CompanyEmployees.slnx --collect:"XPlat Code Coverage"
```

*   `CompanyEmployees.Domain.Tests` — Unit tests for domain entities, invariants, and business calculation rules.
*   `CompanyEmployees.Application.Tests` — Integration and workflow tests with mocked persistence gateways.
