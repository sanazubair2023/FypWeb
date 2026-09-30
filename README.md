# Maid &amp; Servant Online — ASP.NET Core MVC

A single, production-ready **ASP.NET Core MVC** web application migrated from the original
React Native frontend + ASP.NET Core Web API stack. It serves all four roles —
**Client, Worker, Company, Police** — as responsive, server-rendered Bootstrap 5 pages.

The database, `DbContext` and entity models are **100% identical** to the original API
(same schema, properties, types and relationships) — no migrations or schema changes are required.

---

## Requirements

- **.NET 10 SDK** (the project targets `net10.0`, matching the original API)
- **SQL Server** (Express / LocalDB / full) with the existing **`Fyp-1`** database already in place

> Passwords are compared as plain text, exactly like the original API, so your existing
> user rows keep working without any data changes.

---

## Setup &amp; Run

1. **Unzip** the archive and open a terminal in the `FypWeb` project folder
   (the one containing `FypWeb.csproj`).

2. **Point it at your database.** Edit `appsettings.json` → `ConnectionStrings:fyp1`
   and set it to your SQL Server instance. Example (LocalDB):

   ```json
   "ConnectionStrings": {
     "fyp1": "Server=(localdb)\\MSSQLLocalDB;Database=Fyp-1;Trusted_Connection=True;TrustServerCertificate=True;"
   }
   ```

   The default value uses `DESKTOP-4HSSL4Q\SQLEXPRESS` / database `Fyp-1` — the same string the API used.

3. **Restore &amp; run:**

   ```bash
   dotnet restore
   dotnet run
   ```

4. Open the URL shown in the console (by default `https://localhost:7149` or `http://localhost:5150`).
   You'll land on the **Sign In** page.

---

## Roles &amp; sign-in keys

| Role    | Signs in with        | Lands on                    |
|---------|----------------------|-----------------------------|
| Client  | Email + password     | Client Dashboard            |
| Worker  | CNIC + password      | Worker Dashboard            |
| Company | License No. + password | Worker Directory          |
| Police  | Badge / Service ID + password | Verification Portal |

New accounts (including **Police**, which the original app was missing) can be created from
the **Create Account** page.

---

## What was migrated

- **Auth** — JWT replaced with ASP.NET Core **cookie authentication**; a custom sign-in
  validates against the existing four role tables and stores the role as a claim.
- **Screens → Views** — every React Native screen was rebuilt as a responsive `.cshtml`
  view inside a collapsible **sidebar dashboard shell** (mobile / tablet / desktop).
- **API endpoints → MVC controllers** — actions return Views / Redirects, or JSON for the
  handful of AJAX interactions (duty toggle, radius, time slots, live location, skill picker).
- **Business logic preserved** — part-time / full-time matching via Haversine distance vs the
  worker's service radius, the full interview → hiring → resignation / termination state machine,
  reviews, company skill certificates and police character certificates (issue &amp; renew).
- **Maps** — Leaflet + OpenStreetMap with HTML5 browser geolocation for pin-drop location.
- **Seed images** — migrated to `wwwroot/Images` (served at `/Images/...`, matching the DB paths).

---

## Project structure

```
FypWeb.sln
FypWeb/
├─ Program.cs                 # MVC + EF + cookie auth wiring
├─ appsettings.json           # connection string "fyp1"
├─ Controllers/               # Home, Account, Client, Worker, Company, Police
├─ Models/                    # entities + Fyp1Context (verbatim from the API)
├─ ViewModels/                # request/display DTOs
├─ Helpers/                   # GeoHelper, ImageService, Claims + View helpers
├─ Views/
│  ├─ Shared/                 # _Layout, _Sidebar, _AuthLayout, Error, validation
│  ├─ Account/                # Login, Signup
│  ├─ Client/  Worker/  Company/  Police/
└─ wwwroot/
   ├─ css/site.css            # brand theme (#1E64D3)
   ├─ js/site.js              # sidebar, geolocation, Leaflet, AJAX helpers
   └─ Images/                 # seed + uploaded profile photos
```
