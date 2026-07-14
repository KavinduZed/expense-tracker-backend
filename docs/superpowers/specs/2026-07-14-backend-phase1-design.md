# Expense Tracker Backend — Phase 1 Design

**Date:** 2026-07-14
**Status:** Approved
**Repo:** expense-tracker-backend (ASP.NET Core Web API, .NET 8)

## Purpose

Portfolio-grade backend for the Expense Tracker web app (a web port of an existing
Kotlin/Android mobile app). The goal is to showcase experienced-developer practices to
recruiters/interviewers: clean layered architecture, real JWT auth with refresh tokens,
thorough tests, CI, and consistent API design. The React frontend lives in a sibling
repo (`expense-tracker-web`) and consumes this API over REST.

## Decisions (locked)

| Decision | Choice |
|---|---|
| Database | Azure SQL used directly for dev and prod; EF Core (Code First) with the SqlServer provider |
| Scope | Phase 1 = core: auth, profile, boards, categories, expenses, dashboard aggregation |
| Auth | ASP.NET Core Identity + JWT access/refresh tokens; Google OAuth deferred to Phase 2 |
| OCR + Forecasting | Deferred entirely to Phase 2 (no endpoints in Phase 1; architecture leaves room) |
| DevOps | GitHub Actions CI (build + test on push) now; no Docker; Azure App Service native .NET deploy as a later step |

## Architecture

```
HTTP → Controllers → Services (interfaces) → EF Core (AppDbContext) → Azure SQL
         │ DTOs in/out     │ business logic       │ entities
    FluentValidation    ProblemDetails errors   async + CancellationToken
```

- Controllers are thin: bind DTO → call service → map result to HTTP status.
- All business logic lives in `/Services` behind interfaces (mockable in tests).
- EF Core entities never cross the controller boundary — DTOs only.
- All data access is async; no `.Result` / `.Wait()`.

## Solution Structure

```
ExpenseTracker.Api/
├── Models/         User, Board, BoardMember, Category, Expense, RefreshToken
├── Data/           AppDbContext, DbSeeder (default categories), Migrations/
├── DTOs/           request/response records, grouped per resource
├── Services/       AuthService, BoardService, CategoryService, ExpenseService,
│                   DashboardService, ProfileService (+ interfaces)
├── Controllers/    Auth, Profile, Boards, Categories, Expenses, Dashboard
├── Validation/     FluentValidation validators
├── Middleware/     Global exception handler → RFC-7807 ProblemDetails
└── Program.cs      DI, Identity, JWT bearer, Swagger, CORS, Serilog, health checks
Tests/ExpenseTracker.Api.Tests/   xUnit + Moq + FluentAssertions + EF InMemory
.github/workflows/ci.yml          dotnet build + dotnet test on push/PR
```

## Data Model

- **User** (extends IdentityUser): DisplayName, Currency, CreatedAt.
- **Board**: Id, Name, OwnerId → User, CreatedAt. A shared workspace for expenses.
- **BoardMember**: Id, BoardId, UserId, Role (Owner | Member). Join table giving
  User ⟷ Board many-to-many with a role.
- **Category**: Id, Name, Icon, IsDefault. Global (not board-scoped); defaults seeded.
- **Expense**: Id, BoardId, CategoryId, CreatedByUserId, Name, Amount (decimal 18,2),
  Date, Description (nullable), CreatedAt.
- **RefreshToken**: Id, UserId, TokenHash, ExpiresAt, RevokedAt (nullable), CreatedAt.

Behavioral rules:
- On registration, a default board ("Personal") is auto-created with the user as Owner
  (mirrors the mobile app).
- Category deletion is blocked while expenses reference it (409 Conflict).
- Every board-scoped operation verifies the caller is a member of that board; non-members
  get 404 (not 403) to avoid leaking board existence.

## API Endpoints

```
Auth:       POST /api/auth/register · /login · /refresh · /logout    GET /api/auth/me
Profile:    GET · PUT /api/profile                     (display name, currency)
Boards:     GET · POST /api/boards
            GET · PUT · DELETE /api/boards/{id}
            GET · POST /api/boards/{id}/members        (basic sharing by email)
            DELETE /api/boards/{id}/members/{userId}
Categories: GET · POST /api/categories
            PUT · DELETE /api/categories/{id}
Expenses:   GET · POST /api/boards/{boardId}/expenses  (list: date-range + category filters,
                                                        newest-first, paged)
            GET · PUT · DELETE /api/expenses/{id}
Dashboard:  GET /api/boards/{boardId}/dashboard/spend-by-category?from&to
            GET /api/boards/{boardId}/dashboard/spend-over-time?from&to&interval=day|week|month
```

Conventions:
- REST status codes: 201 + Location on create, 204 on delete, 404 unknown/unauthorized
  resource, 409 conflict, 400 validation failure (ProblemDetails body).
- All endpoints except `/api/auth/register|login|refresh` and `/health` require a valid
  JWT (`Authorization: Bearer`).

## Auth Design

- ASP.NET Core Identity manages users and password hashing.
- Login/register return a short-lived **access token** (JWT, ~15 min) and a long-lived
  **refresh token** (opaque, ~14 days).
- Refresh tokens are stored **hashed** in the DB, **rotated on every use** (old one
  revoked, new one issued), and revoked on logout.
- JWT signing key + connection string live in user-secrets /
  `appsettings.Development.json` (gitignored) — never committed.

## Cross-Cutting

- Global exception middleware returning RFC-7807 ProblemDetails (consistent error contract).
- FluentValidation on all request DTOs, auto-run via the validation pipeline.
- Serilog structured logging (console sink; request logging enabled).
- `/health` endpoint (DB connectivity check).
- CORS policy allowing the React dev origin (configurable per environment).
- Swagger/OpenAPI in Development with a JWT "Authorize" button.

## Testing Strategy

- xUnit in `Tests/ExpenseTracker.Api.Tests`; Moq for mocks, FluentAssertions for asserts.
- Services tested thoroughly against EF Core InMemory provider.
- Validators unit-tested directly.
- `WebApplicationFactory` integration tests for the critical paths: register → login →
  refresh, and expense creation with board-membership enforcement.
- Every service method has at least one test before its task is considered done.
- `dotnet build` and `dotnet test` must pass at the end of every implementation step.

## CI

GitHub Actions workflow (`.github/workflows/ci.yml`): on push and PR — restore, build
(Release), test. No deploy step in Phase 1.

## Build Order

1. EF Core models + AppDbContext + initial migration + category seeding
2. Identity + JWT access/refresh auth (register, login, refresh, logout, me)
3. Boards + BoardMembers CRUD (+ default board on registration)
4. Categories CRUD
5. Expenses CRUD with filtering/paging
6. Dashboard aggregation endpoints
7. Cross-cutting polish (exception middleware, Serilog, health, CORS, Swagger JWT)
8. CI workflow

## Deferred to Phase 2

- Google OAuth sign-in
- OCR receipt scanning (`IReceiptScanner` → Azure AI Vision F0 free tier)
- Forecasting (`IForecaster` → ML.NET, runs in-process, free)
- Azure App Service deployment (native .NET deploy, no Docker)
- Dockerfile (optional nice-to-have if a target role calls for containers)

## Prerequisites (user-provided)

- Azure SQL database created (free tier); connection string supplied via user-secrets or
  `appsettings.Development.json`.
