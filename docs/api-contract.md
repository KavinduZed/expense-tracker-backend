# Expense Tracker API — Contract (Phase 1)

Reference for the frontend (`expense-tracker-web`). All of Phase 1 is implemented and on `main`.

## Basics
- Base URL: frontend reads `VITE_API_BASE_URL` (e.g. `https://localhost:7xxx`). Backend also serves Swagger at `/swagger` in Development.
- Auth: JWT **access token** (~15 min) sent as `Authorization: Bearer <token>`. A long-lived **refresh token** (~14 days) is returned in the response body and is **rotated on every refresh** (old one becomes invalid).
- All endpoints require a Bearer token EXCEPT: `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`, and `GET /health`.
- Errors: RFC-7807 **ProblemDetails** JSON — `{ status, title, detail, instance }`. Codes: 400 validation, 401 unauthenticated/bad creds, 404 not found (also used when you're not a member of a board — existence is not leaked), 409 conflict.
- Dates: `DateOnly` serialized as `"YYYY-MM-DD"`. Money: `decimal`.
- CORS: backend already allows `http://localhost:5173` (Vite default). Override via `Cors:AllowedOrigins`.
- On registration a default board named **"Personal"** is auto-created with the user as Owner.
- Seeded global categories (Ids 1–7): Food, Transport, Shopping, Bills, Entertainment, Health, Other.

## Auth — `/api/auth`
| Method | Route | Body | Returns |
|---|---|---|---|
| POST | `/register` | `{ email, password, displayName }` | 201 `AuthResponse` |
| POST | `/login` | `{ email, password }` | 200 `AuthResponse` |
| POST | `/refresh` | `{ refreshToken }` | 200 `AuthResponse` (rotated) |
| POST | `/logout` | `{ refreshToken }` | 204 (auth) |
| GET | `/me` | — | 200 `UserDto` (auth) |

- `AuthResponse` = `{ accessToken, accessTokenExpiresAtUtc, refreshToken, user: UserDto }`
- `UserDto` = `{ id, email, displayName, currency }`
- Password: min 8 chars + ASP.NET Identity default complexity (digit, upper, non-alphanumeric). Weak password → 400; duplicate email → 409.

## Profile — `/api/profile`
| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/` | — | `UserDto` |
| PUT | `/` | `{ displayName, currency }` | `UserDto` (currency = 3-letter code) |

## Boards — `/api/boards`
| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/` | — | `BoardDto[]` |
| POST | `/` | `{ name }` | 201 `BoardDto` |
| GET | `/{id}` | — | `BoardDto` |
| PUT | `/{id}` | `{ name }` | `BoardDto` (owner only; 409 if not owner) |
| DELETE | `/{id}` | — | 204 (owner only) |
| GET | `/{id}/members` | — | `BoardMemberDto[]` |
| POST | `/{id}/members` | `{ email }` | 201 `BoardMemberDto` (owner only; unknown email→404, dup→409) |
| DELETE | `/{id}/members/{userId}` | — | 204 (owner only; owner can't be removed→409) |

- `BoardDto` = `{ id, name, ownerId, createdAt, role, memberCount }` (role = "Owner" | "Member")
- `BoardMemberDto` = `{ userId, email, displayName, role }`

## Categories — `/api/categories` (global, not board-scoped)
| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/` | — | `CategoryDto[]` |
| POST | `/` | `{ name, icon? }` | 201 `CategoryDto` (dup name→409) |
| PUT | `/{id}` | `{ name, icon? }` | `CategoryDto` |
| DELETE | `/{id}` | — | 204 (in use by an expense→409) |

- `CategoryDto` = `{ id, name, icon?, isDefault }`

## Expenses
| Method | Route | Body / Query | Returns |
|---|---|---|---|
| GET | `/api/boards/{boardId}/expenses` | `?from&to&categoryId&page&pageSize` | `PagedResponse<ExpenseDto>` |
| POST | `/api/boards/{boardId}/expenses` | `{ name, amount, categoryId, date, description? }` | 201 `ExpenseDto` |
| GET | `/api/expenses/{id}` | — | `ExpenseDto` |
| PUT | `/api/expenses/{id}` | `{ name, amount, categoryId, date, description? }` | `ExpenseDto` |
| DELETE | `/api/expenses/{id}` | — | 204 |

- `ExpenseDto` = `{ id, boardId, categoryId, categoryName, name, amount, date, description?, createdByUserId, createdAt }`
- `PagedResponse<T>` = `{ items: T[], page, pageSize, totalCount }`
- List is newest-first. Amount must be > 0. Unknown categoryId → 400. Non-member of the board → 404.

## Dashboard — `/api/boards/{boardId}/dashboard`
| Method | Route | Query | Returns |
|---|---|---|---|
| GET | `/spend-by-category` | `?from&to` | `CategorySpendDto[]` — donut chart |
| GET | `/spend-over-time` | `?from&to&interval=day\|week\|month` | `TimePointDto[]` — line chart |

- `CategorySpendDto` = `{ categoryId, categoryName, total }` (ordered by total desc)
- `TimePointDto` = `{ periodStart, total }` (ordered ascending; invalid interval → 400)

## Health
- `GET /health` — anonymous, 200 when up (includes SQL connectivity).

## Running the backend locally (for the FE dev to hit)
```
cd expense-tracker-backend/ExpenseTracker.Api
dotnet run          # serves https://localhost:<port> + /swagger
```
Requires user-secrets: `ConnectionStrings:Default` (Azure SQL) and `Jwt:Key`.

## Open decision for the frontend
Backend returns access + refresh tokens **in the JSON body** (it does NOT set httpOnly cookies). The FE must decide how to persist them (e.g. access token in memory, refresh token in memory vs localStorage) and implement a 401 → refresh → retry interceptor in `src/api/client.ts`.
