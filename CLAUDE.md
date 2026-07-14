# Expense Tracker — Backend API

## Project Context
This is a web port of a mobile expense tracker app (originally Kotlin/Android, MVVM 
architecture). This repo is the ASP.NET Core Web API backend only. The React frontend 
lives in a separate sibling repo (`expense-tracker-web`) and consumes this API over REST.

## Stack
- ASP.NET Core Web API (.NET 8, C#)
- Entity Framework Core (Code First) — SqlServer provider, Azure SQL (decided 2026-07-14)
- ASP.NET Core Identity + JWT (access + refresh tokens) for auth; Google OAuth via 
  Microsoft.AspNetCore.Authentication.Google (Phase 2)
- Azure AI Vision for OCR (receipt scanning)
- ML.NET for the forecasting module

## Data Model (core entities)
- User (Identity user)
- Board — a shared workspace; has many BoardMembers, many Expenses
- BoardMember — join table, User <-> Board, with a role (owner/member)
- Category — global, NOT board-scoped, shared across all boards
- Expense — belongs to a Board and a Category, created by a User

## Conventions
- Controllers in /Controllers, one per resource (BoardsController, ExpensesController, 
  CategoriesController, AuthController)
- Never expose EF Core entities directly from controllers — use DTOs in /DTOs for all 
  request/response bodies
- All EF Core calls are async (async/await, no .Result or .Wait())
- Business logic goes in a /Services layer, not directly in controllers
- Use FluentValidation or DataAnnotations for request validation
- Connection strings and API keys go in appsettings.Development.json (gitignored) or 
  user-secrets — never hardcode or commit secrets
- After any change, run `dotnet build` to confirm it compiles before considering the task done
- Use minimal, focused migrations — one logical change per migration, named descriptively

## API Design
- RESTful routes: /api/boards, /api/boards/{id}/expenses, /api/categories, etc.
- Return standard HTTP status codes (201 on create, 404 on not found, etc.)
- All endpoints except auth require a valid JWT (Authorization: Bearer header)

## Build Order (for reference — don't build everything at once)
1. EF Core models + initial migration
2. Identity + JWT auth (+ Google OAuth)
3. Boards + Categories CRUD
4. Expenses CRUD (manual entry)
5. Dashboard aggregation endpoints (spend by category, spend over time)
6. OCR endpoint (Azure AI Vision integration)
7. Forecasting endpoint (ML.NET)

## Testing
- xUnit in a separate /Tests project (ExpenseTracker.Api.Tests), referencing the main API project
- Use Moq for mocking dependencies, FluentAssertions for assertions
- Use EF Core InMemory provider for tests touching the DbContext
- Test the Services layer thoroughly; controllers only need integration tests for critical
  paths (auth, expense creation) using WebApplicationFactory
- Every new service method should have a corresponding test before the task is considered done
- Run `dotnet test` after adding/changing tests to confirm they pass