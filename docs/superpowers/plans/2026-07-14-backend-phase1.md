# Expense Tracker Backend — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **IMPORTANT — no git commits:** The user commits manually. Wherever a plan step says **Checkpoint**, STOP, report what changed, and let the user commit. Never run `git add`/`git commit`/`git push`.

**Goal:** Build the Phase 1 core of the Expense Tracker API: EF Core data model on Azure SQL, Identity + JWT access/refresh auth, Boards/Categories/Expenses CRUD, dashboard aggregation, ProblemDetails errors, tests, and CI.

**Architecture:** Thin controllers → services behind interfaces → `AppDbContext` (EF Core, SqlServer provider) → Azure SQL. DTOs at the boundary, FluentValidation on input, domain exceptions mapped to RFC-7807 ProblemDetails by middleware. Spec: `docs/superpowers/specs/2026-07-14-backend-phase1-design.md`.

**Tech Stack:** .NET 8, ASP.NET Core Web API, EF Core 8 (SqlServer), ASP.NET Core Identity, JWT Bearer, FluentValidation, Serilog, xUnit + Moq + FluentAssertions + EF InMemory + WebApplicationFactory, GitHub Actions.

## Global Constraints

- .NET 8; all EF Core / I/O calls async with `CancellationToken` — no `.Result` / `.Wait()`.
- EF entities never returned from controllers — DTOs only (records in `/DTOs`).
- Business logic in `/Services` behind interfaces; controllers stay thin.
- Secrets (connection string, JWT key) only in user-secrets — never committed.
- Non-members of a board get **404** (not 403) to avoid leaking board existence.
- Status codes: 201+Location on create, 204 on delete, 400 validation, 401 bad credentials/token, 404 not found, 409 conflict.
- After every task: `dotnet build` and `dotnet test` pass.
- All work happens in `expense-tracker-backend/`; run commands from that repo root.

---

### Task 1: Packages, EF Core models, AppDbContext, initial migration

**Files:**
- Modify: `ExpenseTracker.Api/ExpenseTracker.Api.csproj` (via `dotnet add package`)
- Modify: `Tests/ExpenseTracker.Api.Tests/ExpenseTracker.Api.Tests.csproj` (via `dotnet add package` / `dotnet add reference`)
- Create: `ExpenseTracker.Api/Models/User.cs`, `Board.cs`, `BoardMember.cs`, `Category.cs`, `Expense.cs`, `RefreshToken.cs`
- Create: `ExpenseTracker.Api/Data/AppDbContext.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (register DbContext)
- Modify: `ExpenseTracker.Api/appsettings.json` (empty ConnectionStrings placeholder)
- Delete: `Tests/ExpenseTracker.Api.Tests/UnitTest1.cs`
- Test: `Tests/ExpenseTracker.Api.Tests/Data/AppDbContextTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: entities `User`, `Board`, `BoardMember` (+`BoardRole` enum), `Category`, `Expense`, `RefreshToken`; `AppDbContext` with DbSets `Boards`, `BoardMembers`, `Categories`, `Expenses`, `RefreshTokens`; 7 seeded categories (Ids 1–7).

- [ ] **Step 1: Add NuGet packages and project reference**

```bash
cd ExpenseTracker.Api
dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 8.0.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.*
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.*
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.*
dotnet add package FluentValidation.AspNetCore --version 11.*
dotnet user-secrets init
cd ../Tests/ExpenseTracker.Api.Tests
dotnet add reference ../../ExpenseTracker.Api/ExpenseTracker.Api.csproj
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.*
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.*
cd ../..
```

Also install the EF CLI if missing: `dotnet tool install --global dotnet-ef` (ignore "already installed" error).

- [ ] **Step 2: Delete the placeholder test**

Delete `Tests/ExpenseTracker.Api.Tests/UnitTest1.cs`.

- [ ] **Step 3: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/TestDb.cs` (shared helper used by all later service tests):

```csharp
using ExpenseTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests;

public static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"test-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated(); // applies HasData seed
        return db;
    }
}
```

Create `Tests/ExpenseTracker.Api.Tests/Data/AppDbContextTests.cs`:

```csharp
using ExpenseTracker.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task SeedsSevenDefaultCategories()
    {
        await using var db = TestDb.Create();
        var categories = await db.Categories.ToListAsync();
        categories.Should().HaveCount(7);
        categories.Should().OnlyContain(c => c.IsDefault);
        categories.Select(c => c.Name).Should().Contain(new[] { "Food", "Transport", "Other" });
    }

    [Fact]
    public async Task CanPersistFullObjectGraph()
    {
        await using var db = TestDb.Create();
        var user = new User { Id = "u1", UserName = "a@b.com", Email = "a@b.com", DisplayName = "Alice" };
        var board = new Board { Name = "Personal", OwnerId = "u1" };
        db.Users.Add(user);
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = "u1", Role = BoardRole.Owner });
        db.Expenses.Add(new Expense
        {
            Board = board, CategoryId = 1, CreatedByUserId = "u1",
            Name = "Lunch", Amount = 12.50m, Date = new DateOnly(2026, 7, 1)
        });
        await db.SaveChangesAsync();

        var expense = await db.Expenses.Include(e => e.Category).SingleAsync();
        expense.Amount.Should().Be(12.50m);
        expense.Category!.Name.Should().Be("Food");
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILURE — `AppDbContext`, `User`, etc. do not exist yet.

- [ ] **Step 5: Create the entity models**

`ExpenseTracker.Api/Models/User.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Models;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BoardMember> BoardMemberships { get; set; } = new List<BoardMember>();
}
```

`ExpenseTracker.Api/Models/Board.cs`:

```csharp
namespace ExpenseTracker.Api.Models;

public class Board
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public User? Owner { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BoardMember> Members { get; set; } = new List<BoardMember>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
```

`ExpenseTracker.Api/Models/BoardMember.cs`:

```csharp
namespace ExpenseTracker.Api.Models;

public enum BoardRole { Owner = 0, Member = 1 }

public class BoardMember
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board? Board { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public BoardRole Role { get; set; } = BoardRole.Member;
}
```

`ExpenseTracker.Api/Models/Category.cs`:

```csharp
namespace ExpenseTracker.Api.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public bool IsDefault { get; set; }
}
```

`ExpenseTracker.Api/Models/Expense.cs`:

```csharp
namespace ExpenseTracker.Api.Models;

public class Expense
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board? Board { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public User? CreatedByUser { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

`ExpenseTracker.Api/Models/RefreshToken.cs`:

```csharp
namespace ExpenseTracker.Api.Models;

public class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
```

- [ ] **Step 6: Create AppDbContext with configuration + seed**

`ExpenseTracker.Api/Data/AppDbContext.cs`:

```csharp
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Board>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // Restrict: SQL Server disallows multiple cascade paths via User
            b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BoardMember>(b =>
        {
            b.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
            b.HasOne(x => x.Board).WithMany(x => x.Members).HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany(x => x.BoardMemberships).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(50).IsRequired();
            b.Property(x => x.Icon).HasMaxLength(50);
            b.HasData(
                new Category { Id = 1, Name = "Food", Icon = "restaurant", IsDefault = true },
                new Category { Id = 2, Name = "Transport", Icon = "directions_car", IsDefault = true },
                new Category { Id = 3, Name = "Shopping", Icon = "shopping_bag", IsDefault = true },
                new Category { Id = 4, Name = "Bills", Icon = "receipt_long", IsDefault = true },
                new Category { Id = 5, Name = "Entertainment", Icon = "movie", IsDefault = true },
                new Category { Id = 6, Name = "Health", Icon = "favorite", IsDefault = true },
                new Category { Id = 7, Name = "Other", Icon = "category", IsDefault = true });
        });

        builder.Entity<Expense>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.HasIndex(x => new { x.BoardId, x.Date });
            b.HasOne(x => x.Board).WithMany(x => x.Expenses).HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(88).IsRequired();
            b.HasIndex(x => x.TokenHash);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

- [ ] **Step 7: Register DbContext in Program.cs and add config placeholder**

In `ExpenseTracker.Api/Program.cs`, after `var builder = ...` add:

```csharp
using ExpenseTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

In `ExpenseTracker.Api/appsettings.json` add (empty on purpose — real value lives in user-secrets):

```json
"ConnectionStrings": { "Default": "" }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet build` then `dotnet test`
Expected: build succeeds; both `AppDbContextTests` PASS.

- [ ] **Step 9: Set the Azure SQL connection string (USER INPUT REQUIRED)**

Ask the user for their Azure SQL connection string, then:

```bash
cd ExpenseTracker.Api
dotnet user-secrets set "ConnectionStrings:Default" "<connection string from user>"
```

- [ ] **Step 10: Create and apply the initial migration**

```bash
cd ExpenseTracker.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Expected: `Migrations/` folder created; `dotnet ef database update` succeeds against Azure SQL (requires the firewall to allow the user's IP — if it fails with a firewall error, tell the user to add their client IP in Azure Portal → SQL server → Networking).

- [ ] **Step 11: Checkpoint — user commits**

Report: models + DbContext + seed + initial migration done, tests green. Suggested message: `feat: EF Core data model, AppDbContext, seed categories, initial migration`.

---

### Task 2: TokenService (JWT access + refresh token primitives)

**Files:**
- Create: `ExpenseTracker.Api/Services/ITokenService.cs`, `ExpenseTracker.Api/Services/TokenService.cs`
- Modify: `ExpenseTracker.Api/appsettings.json` (Jwt section, no key)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/TokenServiceTests.cs`

**Interfaces:**
- Consumes: `User` entity (Task 1).
- Produces: `ITokenService` with `(string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user)`, `string GenerateRefreshToken()`, `string HashToken(string token)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/TokenServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ExpenseTracker.Api.Tests.Services;

public class TokenServiceTests
{
    private static TokenService CreateSut() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ExpenseTracker",
            ["Jwt:Audience"] = "ExpenseTracker",
            ["Jwt:Key"] = "unit-test-signing-key-at-least-32-chars!!",
            ["Jwt:AccessTokenMinutes"] = "15",
        }).Build());

    private static readonly User TestUser = new()
        { Id = "u1", Email = "a@b.com", UserName = "a@b.com", DisplayName = "Alice" };

    [Fact]
    public void GenerateAccessToken_ContainsSubEmailAndExpiry()
    {
        var (token, expires) = CreateSut().GenerateAccessToken(TestUser);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "u1");
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == "a@b.com");
        expires.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateRefreshToken_IsUniqueAndLong()
    {
        var sut = CreateSut();
        var t1 = sut.GenerateRefreshToken();
        var t2 = sut.GenerateRefreshToken();
        t1.Should().NotBe(t2);
        t1.Length.Should().BeGreaterThan(60);
    }

    [Fact]
    public void HashToken_IsDeterministicAndNotIdentity()
    {
        var sut = CreateSut();
        sut.HashToken("abc").Should().Be(sut.HashToken("abc"));
        sut.HashToken("abc").Should().NotBe("abc");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TokenServiceTests"`
Expected: build FAILURE — `ITokenService`/`TokenService` missing.

- [ ] **Step 3: Implement TokenService**

`ExpenseTracker.Api/Services/ITokenService.cs`:

```csharp
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}
```

`ExpenseTracker.Api/Services/TokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Services;

public class TokenService(IConfiguration config) : ITokenService
{
    public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user)
    {
        var jwt = config.GetSection("Jwt");
        var expires = DateTime.UtcNow.AddMinutes(jwt.GetValue<int>("AccessTokenMinutes", 15));
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim("name", user.DisplayName),
            ],
            expires: expires,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

Add the Jwt section to `ExpenseTracker.Api/appsettings.json` (key intentionally absent — user-secrets only):

```json
"Jwt": {
  "Issuer": "ExpenseTracker",
  "Audience": "ExpenseTracker",
  "AccessTokenMinutes": 15,
  "RefreshTokenDays": 14
}
```

Then generate and store a dev signing key:

```bash
cd ExpenseTracker.Api
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
```

(If openssl is unavailable in PowerShell, use any random 48+ char string.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TokenServiceTests"`
Expected: 3 PASS.

- [ ] **Step 5: Checkpoint — user commits**

Suggested message: `feat: JWT/refresh token service`.

---

### Task 3: Auth — DTOs, exceptions, AuthService, AuthController, integration tests

**Files:**
- Create: `ExpenseTracker.Api/Exceptions/DomainExceptions.cs`
- Create: `ExpenseTracker.Api/DTOs/AuthDtos.cs`
- Create: `ExpenseTracker.Api/Services/IAuthService.cs`, `ExpenseTracker.Api/Services/AuthService.cs`
- Create: `ExpenseTracker.Api/Validation/AuthValidators.cs`
- Create: `ExpenseTracker.Api/Controllers/AuthController.cs`
- Create: `ExpenseTracker.Api/Extensions/ClaimsPrincipalExtensions.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (Identity, JWT bearer, auth-by-default, DI, seeding hook, `partial class Program`)
- Test: `Tests/ExpenseTracker.Api.Tests/Integration/ApiFactory.cs`, `Tests/ExpenseTracker.Api.Tests/Integration/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `ITokenService` (Task 2), `AppDbContext`, entities (Task 1).
- Produces: `IAuthService` (`RegisterAsync`, `LoginAsync`, `RefreshAsync`, `LogoutAsync`, `GetMeAsync`); DTO records `RegisterRequest(string Email, string Password, string DisplayName)`, `LoginRequest(string Email, string Password)`, `RefreshRequest(string RefreshToken)`, `UserDto(string Id, string Email, string DisplayName, string Currency)`, `AuthResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, UserDto User)`; exceptions `NotFoundException`, `ConflictException`, `UnauthorizedException`, `BadRequestException`; extension `ClaimsPrincipal.GetUserId()`; test fixture `ApiFactory`.

- [ ] **Step 1: Create domain exceptions (needed by AuthService and every later service)**

`ExpenseTracker.Api/Exceptions/DomainExceptions.cs`:

```csharp
namespace ExpenseTracker.Api.Exceptions;

public class NotFoundException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);
public class UnauthorizedException(string message) : Exception(message);
public class BadRequestException(string message) : Exception(message);
```

- [ ] **Step 2: Create auth DTOs**

`ExpenseTracker.Api/DTOs/AuthDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record UserDto(string Id, string Email, string DisplayName, string Currency);
public record AuthResponse(
    string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, UserDto User);
```

- [ ] **Step 3: Write the failing integration tests**

Create `Tests/ExpenseTracker.Api.Tests/Integration/ApiFactory.cs`:

```csharp
using ExpenseTracker.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Api.Tests.Integration;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"it-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-test-signing-key-32-chars-min!!",
            }));

        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }
}
```

Create `Tests/ExpenseTracker.Api.Tests/Integration/AuthEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseTracker.Api.DTOs;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Integration;

public class AuthEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public AuthEndpointsTests(ApiFactory factory) => _client = factory.CreateClient();

    private static RegisterRequest NewUser(string email = "") => new(
        string.IsNullOrEmpty(email) ? $"{Guid.NewGuid():N}@test.com" : email,
        "Passw0rd!x", "Test User");

    [Fact]
    public async Task Register_Returns201_WithTokensAndUser()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", NewUser());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.User.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var request = NewUser();
        await _client.PostAsJsonAsync("/api/auth/register", request);
        var second = await _client.PostAsJsonAsync("/api/auth/register", request);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_InvalidBody_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("not-an-email", "short", ""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_GoodCredentials_Returns200_BadCredentials401()
    {
        var request = NewUser();
        await _client.PostAsJsonAsync("/api/auth/register", request);

        var ok = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(request.Email, request.Password));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var bad = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(request.Email, "WrongPass1!"));
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenStopsWorking()
    {
        var register = await _client.PostAsJsonAsync("/api/auth/register", NewUser());
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();

        var refresh1 = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(auth!.RefreshToken));
        refresh1.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh1.Content.ReadFromJsonAsync<AuthResponse>();
        rotated!.RefreshToken.Should().NotBe(auth.RefreshToken);

        // old token was revoked by rotation
        var refresh2 = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(auth.RefreshToken));
        refresh2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsUser_WithoutToken401()
    {
        var register = await _client.PostAsJsonAsync("/api/auth/register", NewUser());
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var ok = await _client.SendAsync(request);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<UserDto>())!.Email.Should().Be(auth.User.Email);

        var anon = await _client.GetAsync("/api/auth/me");
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AuthEndpointsTests"`
Expected: build FAILURE (`IAuthService`, controller, `Program` partial missing).

- [ ] **Step 5: Implement AuthService**

`ExpenseTracker.Api/Services/IAuthService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct);
    Task LogoutAsync(string userId, RefreshRequest request, CancellationToken ct);
    Task<UserDto> GetMeAsync(string userId, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/AuthService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class AuthService(
    UserManager<User> userManager,
    AppDbContext db,
    ITokenService tokenService,
    IConfiguration config) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new ConflictException(errors);
        }

        // Default board, mirroring the mobile app
        var board = new Board { Name = "Personal", OwnerId = user.Id };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = user.Id, Role = BoardRole.Owner });
        await db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedException("Invalid email or password.");
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokenService.HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive)
            throw new UnauthorizedException("Invalid or expired refresh token.");

        stored.RevokedAtUtc = DateTime.UtcNow; // rotate: one-time use
        return await IssueTokensAsync(stored.User!, ct);
    }

    public async Task LogoutAsync(string userId, RefreshRequest request, CancellationToken ct)
    {
        var hash = tokenService.HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.UserId == userId, ct);
        if (stored is not null && stored.IsActive)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserDto> GetMeAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        return ToDto(user);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(
                config.GetSection("Jwt").GetValue<int>("RefreshTokenDays", 14)),
        });
        await db.SaveChangesAsync(ct);
        return new AuthResponse(accessToken, expiresAt, refreshToken, ToDto(user));
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Email!, user.DisplayName, user.Currency);
}
```

- [ ] **Step 6: Create validators, user-id extension, and controller**

`ExpenseTracker.Api/Validation/AuthValidators.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(50);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}
```

`ExpenseTracker.Api/Extensions/ClaimsPrincipalExtensions.cs`:

```csharp
using System.Security.Claims;
using ExpenseTracker.Api.Exceptions;

namespace ExpenseTracker.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedException("Missing user id claim.");
}
```

`ExpenseTracker.Api/Controllers/AuthController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var response = await authService.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Me), null, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await authService.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Ok(await authService.RefreshAsync(request, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        Ok(await authService.GetMeAsync(User.GetUserId(), ct));
}
```

- [ ] **Step 7: Wire up Program.cs**

Replace `ExpenseTracker.Api/Program.cs` entirely with:

```csharp
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddIdentityCore<User>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>();

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured."))),
        ClockSkew = TimeSpan.FromSeconds(30),
    });

// Every endpoint requires auth unless explicitly [AllowAnonymous]
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations on startup (relational only; tests use InMemory)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) db.Database.Migrate();
    else db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
```

- [ ] **Step 8: Run tests — auth failures map to 500 until middleware exists; verify partially**

Run: `dotnet test --filter "FullyQualifiedName~AuthEndpointsTests"`
Expected at this point: happy-path tests (`Register_Returns201`, `Login...200`, `Me...200`) PASS; tests asserting 401/409 FAIL with 500 responses — the exception→status mapping arrives in the next step. This is the expected intermediate state.

- [ ] **Step 9: Add exception-handling middleware (minimal version now; extended in Task 9)**

Create `ExpenseTracker.Api/Middleware/ExceptionHandlingMiddleware.cs`:

```csharp
using ExpenseTracker.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, title) = ex switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                BadRequestException => (StatusCodes.Status400BadRequest, "Bad Request"),
                _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
            };
            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred." : ex.Message,
                Instance = context.Request.Path,
            });
        }
    }
}
```

In `Program.cs`, add as the FIRST middleware (right after `var app = builder.Build();` block that seeds the DB):

```csharp
app.UseMiddleware<ExpenseTracker.Api.Middleware.ExceptionHandlingMiddleware>();
```

- [ ] **Step 10: Run all tests to verify they pass**

Run: `dotnet test`
Expected: ALL tests PASS (AppDbContext, TokenService, AuthEndpoints).

- [ ] **Step 11: Smoke-test against Azure SQL**

Run: `dotnet run --project ExpenseTracker.Api` then open Swagger at the launch URL (`/swagger`). Register a user via Swagger; confirm 201. Stop the app.
Expected: works against real Azure SQL; default board row visible if queried.

- [ ] **Step 12: Checkpoint — user commits**

Suggested message: `feat: Identity + JWT auth with refresh rotation, ProblemDetails middleware`.

---

### Task 4: Profile endpoints (GET/PUT /api/profile)

**Files:**
- Create: `ExpenseTracker.Api/DTOs/ProfileDtos.cs`
- Create: `ExpenseTracker.Api/Services/IProfileService.cs`, `ExpenseTracker.Api/Services/ProfileService.cs`
- Create: `ExpenseTracker.Api/Validation/ProfileValidators.cs`
- Create: `ExpenseTracker.Api/Controllers/ProfileController.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (DI registration)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/ProfileServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `UserDto`, exceptions, `TestDb` helper.
- Produces: `IProfileService` with `Task<UserDto> GetAsync(string userId, CancellationToken ct)` and `Task<UserDto> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken ct)`; `UpdateProfileRequest(string DisplayName, string Currency)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/ProfileServiceTests.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Services;

public class ProfileServiceTests
{
    [Fact]
    public async Task Get_ReturnsProfile()
    {
        await using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", Email = "a@b.com", UserName = "a@b.com", DisplayName = "Alice" });
        await db.SaveChangesAsync();

        var result = await new ProfileService(db).GetAsync("u1", default);
        result.DisplayName.Should().Be("Alice");
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Get_UnknownUser_Throws404()
    {
        await using var db = TestDb.Create();
        var act = () => new ProfileService(db).GetAsync("nope", default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_ChangesDisplayNameAndCurrency()
    {
        await using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", Email = "a@b.com", UserName = "a@b.com", DisplayName = "Alice" });
        await db.SaveChangesAsync();

        var result = await new ProfileService(db).UpdateAsync(
            "u1", new UpdateProfileRequest("Alicia", "EUR"), default);
        result.DisplayName.Should().Be("Alicia");
        result.Currency.Should().Be("EUR");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ProfileServiceTests"`
Expected: build FAILURE — `ProfileService` missing.

- [ ] **Step 3: Implement DTOs, service, validator, controller**

`ExpenseTracker.Api/DTOs/ProfileDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record UpdateProfileRequest(string DisplayName, string Currency);
```

`ExpenseTracker.Api/Services/IProfileService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IProfileService
{
    Task<UserDto> GetAsync(string userId, CancellationToken ct);
    Task<UserDto> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/ProfileService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class ProfileService(AppDbContext db) : IProfileService
{
    public async Task<UserDto> GetAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        return new UserDto(user.Id, user.Email!, user.DisplayName, user.Currency);
    }

    public async Task<UserDto> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        user.DisplayName = request.DisplayName;
        user.Currency = request.Currency;
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email!, user.DisplayName, user.Currency);
    }
}
```

`ExpenseTracker.Api/Validation/ProfileValidators.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
```

`ExpenseTracker.Api/Controllers/ProfileController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct) =>
        Ok(await profileService.GetAsync(User.GetUserId(), ct));

    [HttpPut]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profileService.UpdateAsync(User.GetUserId(), request, ct));
}
```

In `Program.cs` add: `builder.Services.AddScoped<IProfileService, ProfileService>();`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 5: Checkpoint — user commits**

Suggested message: `feat: profile endpoints`.

---

### Task 5: Boards + BoardMembers

**Files:**
- Create: `ExpenseTracker.Api/DTOs/BoardDtos.cs`
- Create: `ExpenseTracker.Api/Services/IBoardAccessGuard.cs`, `BoardAccessGuard.cs`, `IBoardService.cs`, `BoardService.cs`
- Create: `ExpenseTracker.Api/Validation/BoardValidators.cs`
- Create: `ExpenseTracker.Api/Controllers/BoardsController.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (DI)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/BoardServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, entities, exceptions, `TestDb`.
- Produces:
  - `IBoardAccessGuard`: `Task EnsureMemberAsync(int boardId, string userId, CancellationToken ct)`, `Task EnsureOwnerAsync(int boardId, string userId, CancellationToken ct)` — both throw `NotFoundException` for non-members (404-not-403 rule; `EnsureOwnerAsync` throws `NotFoundException` when not a member, `ConflictException("Only the board owner can do this.")` when member-but-not-owner).
  - `IBoardService`: `GetBoardsAsync(userId, ct)` → `IReadOnlyList<BoardDto>`; `GetBoardAsync(boardId, userId, ct)`; `CreateBoardAsync(userId, CreateBoardRequest, ct)`; `UpdateBoardAsync(boardId, userId, UpdateBoardRequest, ct)`; `DeleteBoardAsync(boardId, userId, ct)`; `GetMembersAsync(boardId, userId, ct)` → `IReadOnlyList<BoardMemberDto>`; `AddMemberAsync(boardId, userId, AddMemberRequest, ct)`; `RemoveMemberAsync(boardId, userId, memberUserId, ct)`.
  - Records: `BoardDto(int Id, string Name, string OwnerId, DateTime CreatedAt, string Role, int MemberCount)`, `CreateBoardRequest(string Name)`, `UpdateBoardRequest(string Name)`, `BoardMemberDto(string UserId, string Email, string DisplayName, string Role)`, `AddMemberRequest(string Email)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/BoardServiceTests.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class BoardServiceTests
{
    private static BoardService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<(string ownerId, string otherId)> SeedUsersAsync(AppDbContext db)
    {
        db.Users.Add(new User { Id = "owner", Email = "o@t.com", UserName = "o@t.com", DisplayName = "Owner" });
        db.Users.Add(new User { Id = "other", Email = "x@t.com", UserName = "x@t.com", DisplayName = "Other" });
        await db.SaveChangesAsync();
        return ("owner", "other");
    }

    [Fact]
    public async Task Create_MakesCallerOwnerMember()
    {
        await using var db = TestDb.Create();
        var (ownerId, _) = await SeedUsersAsync(db);

        var board = await Sut(db).CreateBoardAsync(ownerId, new CreateBoardRequest("Trip"), default);

        board.Name.Should().Be("Trip");
        board.Role.Should().Be("Owner");
        (await db.BoardMembers.CountAsync(m => m.BoardId == board.Id && m.UserId == ownerId))
            .Should().Be(1);
    }

    [Fact]
    public async Task GetBoards_OnlyReturnsBoardsUserBelongsTo()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var sut = Sut(db);
        await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);
        await sut.CreateBoardAsync(otherId, new CreateBoardRequest("Theirs"), default);

        var boards = await sut.GetBoardsAsync(ownerId, default);
        boards.Should().ContainSingle(b => b.Name == "Mine");
    }

    [Fact]
    public async Task GetBoard_NonMember_Throws404()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var board = await Sut(db).CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);

        var act = () => Sut(db).GetBoardAsync(board.Id, otherId, default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_NonOwnerMember_Throws409_Delete_ByOwner_Removes()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var sut = Sut(db);
        var board = await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);
        await sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);

        var act = () => sut.UpdateBoardAsync(board.Id, otherId, new UpdateBoardRequest("Hijack"), default);
        await act.Should().ThrowAsync<ConflictException>();

        await sut.DeleteBoardAsync(board.Id, ownerId, default);
        (await db.Boards.AnyAsync(b => b.Id == board.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AddMember_UnknownEmail404_Duplicate409_RemoveOwner409()
    {
        await using var db = TestDb.Create();
        var (ownerId, _) = await SeedUsersAsync(db);
        var sut = Sut(db);
        var board = await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);

        var unknown = () => sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("ghost@t.com"), default);
        await unknown.Should().ThrowAsync<NotFoundException>();

        await sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);
        var dupe = () => sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);
        await dupe.Should().ThrowAsync<ConflictException>();

        var removeOwner = () => sut.RemoveMemberAsync(board.Id, ownerId, ownerId, default);
        await removeOwner.Should().ThrowAsync<ConflictException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BoardServiceTests"`
Expected: build FAILURE — `BoardService`/`BoardAccessGuard` missing.

- [ ] **Step 3: Implement DTOs and access guard**

`ExpenseTracker.Api/DTOs/BoardDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record BoardDto(int Id, string Name, string OwnerId, DateTime CreatedAt, string Role, int MemberCount);
public record CreateBoardRequest(string Name);
public record UpdateBoardRequest(string Name);
public record BoardMemberDto(string UserId, string Email, string DisplayName, string Role);
public record AddMemberRequest(string Email);
```

`ExpenseTracker.Api/Services/IBoardAccessGuard.cs`:

```csharp
namespace ExpenseTracker.Api.Services;

public interface IBoardAccessGuard
{
    Task EnsureMemberAsync(int boardId, string userId, CancellationToken ct);
    Task EnsureOwnerAsync(int boardId, string userId, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/BoardAccessGuard.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class BoardAccessGuard(AppDbContext db) : IBoardAccessGuard
{
    public async Task EnsureMemberAsync(int boardId, string userId, CancellationToken ct)
    {
        var isMember = await db.BoardMembers.AnyAsync(
            m => m.BoardId == boardId && m.UserId == userId, ct);
        if (!isMember) throw new NotFoundException("Board not found.");
    }

    public async Task EnsureOwnerAsync(int boardId, string userId, CancellationToken ct)
    {
        var membership = await db.BoardMembers.SingleOrDefaultAsync(
            m => m.BoardId == boardId && m.UserId == userId, ct)
            ?? throw new NotFoundException("Board not found.");
        if (membership.Role != BoardRole.Owner)
            throw new ConflictException("Only the board owner can do this.");
    }
}
```

- [ ] **Step 4: Implement BoardService**

`ExpenseTracker.Api/Services/IBoardService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IBoardService
{
    Task<IReadOnlyList<BoardDto>> GetBoardsAsync(string userId, CancellationToken ct);
    Task<BoardDto> GetBoardAsync(int boardId, string userId, CancellationToken ct);
    Task<BoardDto> CreateBoardAsync(string userId, CreateBoardRequest request, CancellationToken ct);
    Task<BoardDto> UpdateBoardAsync(int boardId, string userId, UpdateBoardRequest request, CancellationToken ct);
    Task DeleteBoardAsync(int boardId, string userId, CancellationToken ct);
    Task<IReadOnlyList<BoardMemberDto>> GetMembersAsync(int boardId, string userId, CancellationToken ct);
    Task<BoardMemberDto> AddMemberAsync(int boardId, string userId, AddMemberRequest request, CancellationToken ct);
    Task RemoveMemberAsync(int boardId, string userId, string memberUserId, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/BoardService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class BoardService(AppDbContext db, IBoardAccessGuard guard) : IBoardService
{
    public async Task<IReadOnlyList<BoardDto>> GetBoardsAsync(string userId, CancellationToken ct) =>
        await db.BoardMembers
            .Where(m => m.UserId == userId)
            .Select(m => new BoardDto(
                m.Board!.Id, m.Board.Name, m.Board.OwnerId, m.Board.CreatedAt,
                m.Role.ToString(), m.Board.Members.Count))
            .ToListAsync(ct);

    public async Task<BoardDto> GetBoardAsync(int boardId, string userId, CancellationToken ct)
    {
        var dto = await db.BoardMembers
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => new BoardDto(
                m.Board!.Id, m.Board.Name, m.Board.OwnerId, m.Board.CreatedAt,
                m.Role.ToString(), m.Board.Members.Count))
            .SingleOrDefaultAsync(ct);
        return dto ?? throw new NotFoundException("Board not found.");
    }

    public async Task<BoardDto> CreateBoardAsync(string userId, CreateBoardRequest request, CancellationToken ct)
    {
        var board = new Board { Name = request.Name, OwnerId = userId };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = userId, Role = BoardRole.Owner });
        await db.SaveChangesAsync(ct);
        return new BoardDto(board.Id, board.Name, board.OwnerId, board.CreatedAt, "Owner", 1);
    }

    public async Task<BoardDto> UpdateBoardAsync(int boardId, string userId, UpdateBoardRequest request, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var board = await db.Boards.Include(b => b.Members).SingleAsync(b => b.Id == boardId, ct);
        board.Name = request.Name;
        await db.SaveChangesAsync(ct);
        return new BoardDto(board.Id, board.Name, board.OwnerId, board.CreatedAt, "Owner", board.Members.Count);
    }

    public async Task DeleteBoardAsync(int boardId, string userId, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var board = await db.Boards.SingleAsync(b => b.Id == boardId, ct);
        db.Boards.Remove(board); // cascades to members + expenses
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BoardMemberDto>> GetMembersAsync(int boardId, string userId, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        return await db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .Select(m => new BoardMemberDto(
                m.UserId, m.User!.Email!, m.User.DisplayName, m.Role.ToString()))
            .ToListAsync(ct);
    }

    public async Task<BoardMemberDto> AddMemberAsync(int boardId, string userId, AddMemberRequest request, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email, ct)
            ?? throw new NotFoundException("No user with that email.");
        var exists = await db.BoardMembers.AnyAsync(
            m => m.BoardId == boardId && m.UserId == user.Id, ct);
        if (exists) throw new ConflictException("User is already a member of this board.");

        db.BoardMembers.Add(new BoardMember { BoardId = boardId, UserId = user.Id, Role = BoardRole.Member });
        await db.SaveChangesAsync(ct);
        return new BoardMemberDto(user.Id, user.Email!, user.DisplayName, "Member");
    }

    public async Task RemoveMemberAsync(int boardId, string userId, string memberUserId, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var membership = await db.BoardMembers.SingleOrDefaultAsync(
            m => m.BoardId == boardId && m.UserId == memberUserId, ct)
            ?? throw new NotFoundException("Member not found on this board.");
        if (membership.Role == BoardRole.Owner)
            throw new ConflictException("The board owner cannot be removed.");
        db.BoardMembers.Remove(membership);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Implement validators and controller**

`ExpenseTracker.Api/Validation/BoardValidators.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class CreateBoardRequestValidator : AbstractValidator<CreateBoardRequest>
{
    public CreateBoardRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public class UpdateBoardRequestValidator : AbstractValidator<UpdateBoardRequest>
{
    public UpdateBoardRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
}
```

`ExpenseTracker.Api/Controllers/BoardsController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/boards")]
public class BoardsController(IBoardService boardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BoardDto>>> GetAll(CancellationToken ct) =>
        Ok(await boardService.GetBoardsAsync(User.GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BoardDto>> Get(int id, CancellationToken ct) =>
        Ok(await boardService.GetBoardAsync(id, User.GetUserId(), ct));

    [HttpPost]
    public async Task<ActionResult<BoardDto>> Create(CreateBoardRequest request, CancellationToken ct)
    {
        var board = await boardService.CreateBoardAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = board.Id }, board);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BoardDto>> Update(int id, UpdateBoardRequest request, CancellationToken ct) =>
        Ok(await boardService.UpdateBoardAsync(id, User.GetUserId(), request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await boardService.DeleteBoardAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet("{id:int}/members")]
    public async Task<ActionResult<IReadOnlyList<BoardMemberDto>>> GetMembers(int id, CancellationToken ct) =>
        Ok(await boardService.GetMembersAsync(id, User.GetUserId(), ct));

    [HttpPost("{id:int}/members")]
    public async Task<ActionResult<BoardMemberDto>> AddMember(int id, AddMemberRequest request, CancellationToken ct)
    {
        var member = await boardService.AddMemberAsync(id, User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetMembers), new { id }, member);
    }

    [HttpDelete("{id:int}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int id, string userId, CancellationToken ct)
    {
        await boardService.RemoveMemberAsync(id, User.GetUserId(), userId, ct);
        return NoContent();
    }
}
```

In `Program.cs` add:

```csharp
builder.Services.AddScoped<IBoardAccessGuard, BoardAccessGuard>();
builder.Services.AddScoped<IBoardService, BoardService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 7: Checkpoint — user commits**

Suggested message: `feat: boards and board members with owner/member roles`.

---

### Task 6: Categories CRUD

**Files:**
- Create: `ExpenseTracker.Api/DTOs/CategoryDtos.cs`
- Create: `ExpenseTracker.Api/Services/ICategoryService.cs`, `CategoryService.cs`
- Create: `ExpenseTracker.Api/Validation/CategoryValidators.cs`
- Create: `ExpenseTracker.Api/Controllers/CategoriesController.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (DI)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/CategoryServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (7 seeded categories, Ids 1–7), exceptions, `TestDb`.
- Produces: `ICategoryService`: `GetAllAsync(ct)` → `IReadOnlyList<CategoryDto>`; `CreateAsync(CreateCategoryRequest, ct)`; `UpdateAsync(id, UpdateCategoryRequest, ct)`; `DeleteAsync(id, ct)`. Records: `CategoryDto(int Id, string Name, string? Icon, bool IsDefault)`, `CreateCategoryRequest(string Name, string? Icon)`, `UpdateCategoryRequest(string Name, string? Icon)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/CategoryServiceTests.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class CategoryServiceTests
{
    [Fact]
    public async Task GetAll_ReturnsSeededCategories()
    {
        await using var db = TestDb.Create();
        var all = await new CategoryService(db).GetAllAsync(default);
        all.Should().HaveCount(7);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws409()
    {
        await using var db = TestDb.Create();
        var sut = new CategoryService(db);
        var act = () => sut.CreateAsync(new CreateCategoryRequest("Food", null), default);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Create_Update_Delete_Roundtrip()
    {
        await using var db = TestDb.Create();
        var sut = new CategoryService(db);

        var created = await sut.CreateAsync(new CreateCategoryRequest("Pets", "pets"), default);
        created.IsDefault.Should().BeFalse();

        var updated = await sut.UpdateAsync(created.Id, new UpdateCategoryRequest("Pet Care", "pets"), default);
        updated.Name.Should().Be("Pet Care");

        await sut.DeleteAsync(created.Id, default);
        (await db.Categories.AnyAsync(c => c.Id == created.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_CategoryInUse_Throws409()
    {
        await using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", Email = "a@b.com", UserName = "a@b.com", DisplayName = "A" });
        var board = new Board { Name = "B", OwnerId = "u1" };
        db.Boards.Add(board);
        db.Expenses.Add(new Expense
        {
            Board = board, CategoryId = 1, CreatedByUserId = "u1",
            Name = "Lunch", Amount = 5m, Date = new DateOnly(2026, 7, 1)
        });
        await db.SaveChangesAsync();

        var act = () => new CategoryService(db).DeleteAsync(1, default);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Update_UnknownId_Throws404()
    {
        await using var db = TestDb.Create();
        var act = () => new CategoryService(db).UpdateAsync(999, new UpdateCategoryRequest("X", null), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CategoryServiceTests"`
Expected: build FAILURE — `CategoryService` missing.

- [ ] **Step 3: Implement DTOs, service, validators, controller**

`ExpenseTracker.Api/DTOs/CategoryDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record CategoryDto(int Id, string Name, string? Icon, bool IsDefault);
public record CreateCategoryRequest(string Name, string? Icon);
public record UpdateCategoryRequest(string Name, string? Icon);
```

`ExpenseTracker.Api/Services/ICategoryService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/CategoryService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct) =>
        await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Icon, c.IsDefault))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var duplicate = await db.Categories.AnyAsync(c => c.Name == request.Name, ct);
        if (duplicate) throw new ConflictException("A category with that name already exists.");

        var category = new Category { Name = request.Name, Icon = request.Icon, IsDefault = false };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Icon, category.IsDefault);
    }

    public async Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        var duplicate = await db.Categories.AnyAsync(c => c.Name == request.Name && c.Id != id, ct);
        if (duplicate) throw new ConflictException("A category with that name already exists.");

        category.Name = request.Name;
        category.Icon = request.Icon;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Icon, category.IsDefault);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        var inUse = await db.Expenses.AnyAsync(e => e.CategoryId == id, ct);
        if (inUse) throw new ConflictException("Category is in use by expenses and cannot be deleted.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}
```

`ExpenseTracker.Api/Validation/CategoryValidators.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}

public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}
```

`ExpenseTracker.Api/Controllers/CategoriesController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken ct) =>
        Ok(await categoryService.GetAllAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await categoryService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), null, category);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Update(int id, UpdateCategoryRequest request, CancellationToken ct) =>
        Ok(await categoryService.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await categoryService.DeleteAsync(id, ct);
        return NoContent();
    }
}
```

In `Program.cs` add: `builder.Services.AddScoped<ICategoryService, CategoryService>();`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 5: Checkpoint — user commits**

Suggested message: `feat: global categories CRUD with in-use delete protection`.

---

### Task 7: Expenses CRUD with filtering + paging

**Files:**
- Create: `ExpenseTracker.Api/DTOs/ExpenseDtos.cs`
- Create: `ExpenseTracker.Api/Services/IExpenseService.cs`, `ExpenseService.cs`
- Create: `ExpenseTracker.Api/Validation/ExpenseValidators.cs`
- Create: `ExpenseTracker.Api/Controllers/ExpensesController.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (DI)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/ExpenseServiceTests.cs`

**Interfaces:**
- Consumes: `IBoardAccessGuard` (Task 5), `AppDbContext`, exceptions, `TestDb`.
- Produces: `IExpenseService`: `GetExpensesAsync(int boardId, string userId, ExpenseListQuery query, CancellationToken ct)` → `PagedResponse<ExpenseDto>`; `GetExpenseAsync(int id, string userId, ct)`; `CreateAsync(int boardId, string userId, CreateExpenseRequest, ct)`; `UpdateAsync(int id, string userId, UpdateExpenseRequest, ct)`; `DeleteAsync(int id, string userId, ct)`. Records: `ExpenseDto(int Id, int BoardId, int CategoryId, string CategoryName, string Name, decimal Amount, DateOnly Date, string? Description, string CreatedByUserId, DateTime CreatedAt)`, `CreateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description)`, `UpdateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description)`, `ExpenseListQuery(DateOnly? From, DateOnly? To, int? CategoryId, int Page = 1, int PageSize = 20)`, `PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/ExpenseServiceTests.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class ExpenseServiceTests
{
    private static ExpenseService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<int> SeedBoardAsync(AppDbContext db, string userId = "u1")
    {
        db.Users.Add(new User { Id = userId, Email = $"{userId}@t.com", UserName = $"{userId}@t.com", DisplayName = userId });
        var board = new Board { Name = "B", OwnerId = userId };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = userId, Role = BoardRole.Owner });
        await db.SaveChangesAsync();
        return board.Id;
    }

    [Fact]
    public async Task Create_ReturnsDtoWithCategoryName()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);

        var dto = await Sut(db).CreateAsync(boardId, "u1",
            new CreateExpenseRequest("Lunch", 12.50m, 1, new DateOnly(2026, 7, 1), null), default);

        dto.CategoryName.Should().Be("Food");
        dto.Amount.Should().Be(12.50m);
    }

    [Fact]
    public async Task Create_UnknownCategory_Throws400()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var act = () => Sut(db).CreateAsync(boardId, "u1",
            new CreateExpenseRequest("X", 1m, 999, new DateOnly(2026, 7, 1), null), default);
        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Create_NonMember_Throws404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        db.Users.Add(new User { Id = "intruder", Email = "i@t.com", UserName = "i@t.com", DisplayName = "I" });
        await db.SaveChangesAsync();

        var act = () => Sut(db).CreateAsync(boardId, "intruder",
            new CreateExpenseRequest("X", 1m, 1, new DateOnly(2026, 7, 1), null), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task List_FiltersByDateAndCategory_AndPages()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var sut = Sut(db);
        for (var day = 1; day <= 10; day++)
            await sut.CreateAsync(boardId, "u1", new CreateExpenseRequest(
                $"e{day}", day, day <= 5 ? 1 : 2, new DateOnly(2026, 7, day), null), default);

        var filtered = await sut.GetExpensesAsync(boardId, "u1",
            new ExpenseListQuery(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 7), 1), default);
        filtered.TotalCount.Should().Be(3); // days 3,4,5 are category 1

        var paged = await sut.GetExpensesAsync(boardId, "u1",
            new ExpenseListQuery(null, null, null, Page: 2, PageSize: 4), default);
        paged.Items.Should().HaveCount(4);
        paged.TotalCount.Should().Be(10);
        // newest-first: page 2 starts at the 5th newest (day 6)
        paged.Items[0].Name.Should().Be("e6");
    }

    [Fact]
    public async Task Update_And_Delete_Work_UnknownId404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var sut = Sut(db);
        var created = await sut.CreateAsync(boardId, "u1",
            new CreateExpenseRequest("Lunch", 10m, 1, new DateOnly(2026, 7, 1), null), default);

        var updated = await sut.UpdateAsync(created.Id, "u1",
            new UpdateExpenseRequest("Dinner", 20m, 2, new DateOnly(2026, 7, 2), "late"), default);
        updated.Name.Should().Be("Dinner");
        updated.CategoryName.Should().Be("Transport");

        await sut.DeleteAsync(created.Id, "u1", default);
        var act = () => sut.GetExpenseAsync(created.Id, "u1", default);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ExpenseServiceTests"`
Expected: build FAILURE — `ExpenseService` missing.

- [ ] **Step 3: Implement DTOs and service**

`ExpenseTracker.Api/DTOs/ExpenseDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record ExpenseDto(
    int Id, int BoardId, int CategoryId, string CategoryName, string Name,
    decimal Amount, DateOnly Date, string? Description, string CreatedByUserId, DateTime CreatedAt);
public record CreateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description);
public record UpdateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description);
public record ExpenseListQuery(DateOnly? From = null, DateOnly? To = null, int? CategoryId = null,
    int Page = 1, int PageSize = 20);
public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

`ExpenseTracker.Api/Services/IExpenseService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IExpenseService
{
    Task<PagedResponse<ExpenseDto>> GetExpensesAsync(int boardId, string userId, ExpenseListQuery query, CancellationToken ct);
    Task<ExpenseDto> GetExpenseAsync(int id, string userId, CancellationToken ct);
    Task<ExpenseDto> CreateAsync(int boardId, string userId, CreateExpenseRequest request, CancellationToken ct);
    Task<ExpenseDto> UpdateAsync(int id, string userId, UpdateExpenseRequest request, CancellationToken ct);
    Task DeleteAsync(int id, string userId, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/ExpenseService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class ExpenseService(AppDbContext db, IBoardAccessGuard guard) : IExpenseService
{
    public async Task<PagedResponse<ExpenseDto>> GetExpensesAsync(
        int boardId, string userId, ExpenseListQuery query, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(query.Page, 1);

        var q = db.Expenses.Where(e => e.BoardId == boardId);
        if (query.From is { } from) q = q.Where(e => e.Date >= from);
        if (query.To is { } to) q = q.Where(e => e.Date <= to);
        if (query.CategoryId is { } categoryId) q = q.Where(e => e.CategoryId == categoryId);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => ToDtoExpression(e))
            .ToListAsync(ct);
        return new PagedResponse<ExpenseDto>(items, page, pageSize, total);
    }

    public async Task<ExpenseDto> GetExpenseAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        return ToDto(expense);
    }

    public async Task<ExpenseDto> CreateAsync(
        int boardId, string userId, CreateExpenseRequest request, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new BadRequestException("Unknown category.");

        var expense = new Expense
        {
            BoardId = boardId,
            CategoryId = category.Id,
            CreatedByUserId = userId,
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date,
            Description = request.Description,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        expense.Category = category;
        return ToDto(expense);
    }

    public async Task<ExpenseDto> UpdateAsync(
        int id, string userId, UpdateExpenseRequest request, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new BadRequestException("Unknown category.");

        expense.Name = request.Name;
        expense.Amount = request.Amount;
        expense.CategoryId = category.Id;
        expense.Category = category;
        expense.Date = request.Date;
        expense.Description = request.Description;
        await db.SaveChangesAsync(ct);
        return ToDto(expense);
    }

    public async Task DeleteAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Expense> FindAccessibleAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await db.Expenses.Include(e => e.Category)
            .SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Expense not found.");
        await guard.EnsureMemberAsync(expense.BoardId, userId, ct); // non-member -> 404
        return expense;
    }

    private static ExpenseDto ToDtoExpression(Expense e) =>
        new(e.Id, e.BoardId, e.CategoryId, e.Category!.Name, e.Name,
            e.Amount, e.Date, e.Description, e.CreatedByUserId, e.CreatedAt);

    private static ExpenseDto ToDto(Expense e) => ToDtoExpression(e);
}
```

- [ ] **Step 4: Implement validators and controller**

`ExpenseTracker.Api/Validation/ExpenseValidators.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
```

`ExpenseTracker.Api/Controllers/ExpensesController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
public class ExpensesController(IExpenseService expenseService) : ControllerBase
{
    [HttpGet("api/boards/{boardId:int}/expenses")]
    public async Task<ActionResult<PagedResponse<ExpenseDto>>> List(
        int boardId, [FromQuery] ExpenseListQuery query, CancellationToken ct) =>
        Ok(await expenseService.GetExpensesAsync(boardId, User.GetUserId(), query, ct));

    [HttpPost("api/boards/{boardId:int}/expenses")]
    public async Task<ActionResult<ExpenseDto>> Create(
        int boardId, CreateExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenseService.CreateAsync(boardId, User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = expense.Id }, expense);
    }

    [HttpGet("api/expenses/{id:int}")]
    public async Task<ActionResult<ExpenseDto>> Get(int id, CancellationToken ct) =>
        Ok(await expenseService.GetExpenseAsync(id, User.GetUserId(), ct));

    [HttpPut("api/expenses/{id:int}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        int id, UpdateExpenseRequest request, CancellationToken ct) =>
        Ok(await expenseService.UpdateAsync(id, User.GetUserId(), request, ct));

    [HttpDelete("api/expenses/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await expenseService.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}
```

In `Program.cs` add: `builder.Services.AddScoped<IExpenseService, ExpenseService>();`

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 6: Checkpoint — user commits**

Suggested message: `feat: expenses CRUD with board scoping, filters, paging`.

---

### Task 8: Dashboard aggregation endpoints

**Files:**
- Create: `ExpenseTracker.Api/DTOs/DashboardDtos.cs`
- Create: `ExpenseTracker.Api/Services/IDashboardService.cs`, `DashboardService.cs`
- Create: `ExpenseTracker.Api/Controllers/DashboardController.cs`
- Modify: `ExpenseTracker.Api/Program.cs` (DI)
- Test: `Tests/ExpenseTracker.Api.Tests/Services/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `IBoardAccessGuard`, `AppDbContext`, exceptions, `TestDb`.
- Produces: `IDashboardService`: `GetSpendByCategoryAsync(int boardId, string userId, DateOnly? from, DateOnly? to, CancellationToken ct)` → `IReadOnlyList<CategorySpendDto>`; `GetSpendOverTimeAsync(int boardId, string userId, DateOnly? from, DateOnly? to, string interval, CancellationToken ct)` → `IReadOnlyList<TimePointDto>` (interval: `"day" | "week" | "month"`, invalid → `BadRequestException`). Records: `CategorySpendDto(int CategoryId, string CategoryName, decimal Total)`, `TimePointDto(DateOnly PeriodStart, decimal Total)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Services/DashboardServiceTests.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Services;

public class DashboardServiceTests
{
    private static DashboardService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<int> SeedAsync(AppDbContext db)
    {
        db.Users.Add(new User { Id = "u1", Email = "a@t.com", UserName = "a@t.com", DisplayName = "A" });
        var board = new Board { Name = "B", OwnerId = "u1" };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = "u1", Role = BoardRole.Owner });
        // Food: 10 + 20 (Jul 1, Jul 2) ; Transport: 5 (Aug 1)
        db.Expenses.AddRange(
            new Expense { Board = board, CategoryId = 1, CreatedByUserId = "u1", Name = "a", Amount = 10, Date = new DateOnly(2026, 7, 1) },
            new Expense { Board = board, CategoryId = 1, CreatedByUserId = "u1", Name = "b", Amount = 20, Date = new DateOnly(2026, 7, 2) },
            new Expense { Board = board, CategoryId = 2, CreatedByUserId = "u1", Name = "c", Amount = 5, Date = new DateOnly(2026, 8, 1) });
        await db.SaveChangesAsync();
        return board.Id;
    }

    [Fact]
    public async Task SpendByCategory_SumsAndOrdersDescending()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var result = await Sut(db).GetSpendByCategoryAsync(boardId, "u1", null, null, default);

        result.Should().HaveCount(2);
        result[0].CategoryName.Should().Be("Food");
        result[0].Total.Should().Be(30);
        result[1].Total.Should().Be(5);
    }

    [Fact]
    public async Task SpendByCategory_RespectsDateRange()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var result = await Sut(db).GetSpendByCategoryAsync(
            boardId, "u1", new DateOnly(2026, 8, 1), null, default);

        result.Should().ContainSingle(r => r.CategoryName == "Transport");
    }

    [Fact]
    public async Task SpendOverTime_MonthBucketsAndDayBuckets()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);
        var sut = Sut(db);

        var monthly = await sut.GetSpendOverTimeAsync(boardId, "u1", null, null, "month", default);
        monthly.Should().HaveCount(2);
        monthly[0].PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        monthly[0].Total.Should().Be(30);

        var daily = await sut.GetSpendOverTimeAsync(boardId, "u1", null, null, "day", default);
        daily.Should().HaveCount(3);
    }

    [Fact]
    public async Task SpendOverTime_InvalidInterval_Throws400_NonMember404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var badInterval = () => Sut(db).GetSpendOverTimeAsync(boardId, "u1", null, null, "year", default);
        await badInterval.Should().ThrowAsync<BadRequestException>();

        db.Users.Add(new User { Id = "x", Email = "x@t.com", UserName = "x@t.com", DisplayName = "X" });
        await db.SaveChangesAsync();
        var nonMember = () => Sut(db).GetSpendByCategoryAsync(boardId, "x", null, null, default);
        await nonMember.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: build FAILURE — `DashboardService` missing.

- [ ] **Step 3: Implement DTOs, service, controller**

`ExpenseTracker.Api/DTOs/DashboardDtos.cs`:

```csharp
namespace ExpenseTracker.Api.DTOs;

public record CategorySpendDto(int CategoryId, string CategoryName, decimal Total);
public record TimePointDto(DateOnly PeriodStart, decimal Total);
```

`ExpenseTracker.Api/Services/IDashboardService.cs`:

```csharp
using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<CategorySpendDto>> GetSpendByCategoryAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<TimePointDto>> GetSpendOverTimeAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, string interval, CancellationToken ct);
}
```

`ExpenseTracker.Api/Services/DashboardService.cs`:

```csharp
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class DashboardService(AppDbContext db, IBoardAccessGuard guard) : IDashboardService
{
    public async Task<IReadOnlyList<CategorySpendDto>> GetSpendByCategoryAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var q = Filter(boardId, from, to);
        return await q
            .GroupBy(e => new { e.CategoryId, e.Category!.Name })
            .Select(g => new CategorySpendDto(g.Key.CategoryId, g.Key.Name, g.Sum(e => e.Amount)))
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimePointDto>> GetSpendOverTimeAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, string interval, CancellationToken ct)
    {
        Func<DateOnly, DateOnly> bucket = interval switch
        {
            "day" => d => d,
            "week" => d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)), // Monday start
            "month" => d => new DateOnly(d.Year, d.Month, 1),
            _ => throw new BadRequestException("interval must be one of: day, week, month."),
        };
        await guard.EnsureMemberAsync(boardId, userId, ct);

        var rows = await Filter(boardId, from, to)
            .Select(e => new { e.Date, e.Amount })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => bucket(r.Date))
            .Select(g => new TimePointDto(g.Key, g.Sum(r => r.Amount)))
            .OrderBy(p => p.PeriodStart)
            .ToList();
    }

    private IQueryable<Models.Expense> Filter(int boardId, DateOnly? from, DateOnly? to)
    {
        var q = db.Expenses.Where(e => e.BoardId == boardId);
        if (from is { } f) q = q.Where(e => e.Date >= f);
        if (to is { } t) q = q.Where(e => e.Date <= t);
        return q;
    }
}
```

`ExpenseTracker.Api/Controllers/DashboardController.cs`:

```csharp
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId:int}/dashboard")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("spend-by-category")]
    public async Task<ActionResult<IReadOnlyList<CategorySpendDto>>> SpendByCategory(
        int boardId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await dashboardService.GetSpendByCategoryAsync(boardId, User.GetUserId(), from, to, ct));

    [HttpGet("spend-over-time")]
    public async Task<ActionResult<IReadOnlyList<TimePointDto>>> SpendOverTime(
        int boardId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string interval = "month", CancellationToken ct = default) =>
        Ok(await dashboardService.GetSpendOverTimeAsync(boardId, User.GetUserId(), from, to, interval, ct));
}
```

In `Program.cs` add: `builder.Services.AddScoped<IDashboardService, DashboardService>();`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 5: Checkpoint — user commits**

Suggested message: `feat: dashboard spend-by-category and spend-over-time endpoints`.

---

### Task 9: Cross-cutting polish — Serilog, health check, CORS, Swagger JWT

**Files:**
- Modify: `ExpenseTracker.Api/ExpenseTracker.Api.csproj` (Serilog + health check packages)
- Modify: `ExpenseTracker.Api/Program.cs`
- Modify: `ExpenseTracker.Api/appsettings.json` (Cors:AllowedOrigins)
- Test: `Tests/ExpenseTracker.Api.Tests/Integration/HealthAndSecurityTests.cs`

**Interfaces:**
- Consumes: `ApiFactory` (Task 3).
- Produces: `/health` endpoint (anonymous); CORS policy `Frontend`; Swagger JWT Authorize button; Serilog request logging.

- [ ] **Step 1: Add packages**

```bash
cd ExpenseTracker.Api
dotnet add package Serilog.AspNetCore --version 8.*
dotnet add package AspNetCore.HealthChecks.SqlServer --version 8.*
```

- [ ] **Step 2: Write the failing tests**

Create `Tests/ExpenseTracker.Api.Tests/Integration/HealthAndSecurityTests.cs`:

```csharp
using System.Net;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Integration;

public class HealthAndSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_IsAnonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/boards")]
    [InlineData("/api/categories")]
    [InlineData("/api/profile")]
    public async Task ProtectedEndpoints_Return401WithoutToken(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~HealthAndSecurityTests"`
Expected: `Health_IsAnonymous` FAILS (404 — endpoint doesn't exist); 401 tests already PASS (fallback policy).

- [ ] **Step 4: Update Program.cs and config**

In `Program.cs`:

Add at the very top (before `var builder`): nothing — Serilog is configured via builder. Modify as follows.

After `var builder = WebApplication.CreateBuilder(args);` add:

```csharp
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());
```

(add `using Serilog;` to the usings)

Before `builder.Services.AddControllers();` add:

```csharp
builder.Services.AddCors(o => o.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Default")))
    healthChecks.AddSqlServer(builder.Configuration.GetConnectionString("Default")!);
```

Replace `builder.Services.AddSwaggerGen();` with:

```csharp
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});
```

In the pipeline (order matters), between the exception middleware and `UseAuthentication`:

```csharp
app.UseSerilogRequestLogging();
app.UseCors("Frontend");
```

After `app.MapControllers();` add:

```csharp
app.MapHealthChecks("/health").AllowAnonymous();
```

In `appsettings.json` add:

```json
"Cors": { "AllowedOrigins": ["http://localhost:5173"] }
```

- [ ] **Step 5: Run all tests to verify they pass**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 6: Smoke-test Swagger auth flow**

Run: `dotnet run --project ExpenseTracker.Api`, open `/swagger`: register → copy access token → Authorize button → call `GET /api/boards`.
Expected: 200 with the default "Personal" board. Stop the app.

- [ ] **Step 7: Checkpoint — user commits**

Suggested message: `feat: Serilog, health checks, CORS, Swagger JWT auth`.

---

### Task 10: GitHub Actions CI

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the solution (`expense-tracker-backend/ExpenseTracker.sln`); all tests use InMemory — no DB needed in CI.
- Produces: CI that builds + tests on push/PR.

- [ ] **Step 1: Create the workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore --configuration Release
      - name: Test
        run: dotnet test --no-build --configuration Release --verbosity normal
```

- [ ] **Step 2: Verify locally that the CI commands succeed**

Run: `dotnet restore && dotnet build --no-restore --configuration Release && dotnet test --no-build --configuration Release`
Expected: all three succeed, all tests PASS.

- [ ] **Step 3: Final checkpoint — user commits and pushes**

Suggested message: `ci: build and test on push/PR`. After the user pushes, ask them to confirm the Actions run is green on GitHub.

---

## Plan Self-Review (completed)

- **Spec coverage:** models+migration (T1), auth+refresh rotation (T2–3), profile (T4), boards/members+404-not-403 (T5), categories+in-use protection (T6), expenses+filters/paging (T7), dashboard (T8), ProblemDetails middleware (T3), Serilog/health/CORS/Swagger-JWT (T9), CI (T10). Google OAuth/OCR/forecast/deploy: deferred per spec.
- **Type consistency:** DTO record shapes in Task 3/5/6/7/8 `Interfaces` blocks match implementations; `TestDb.Create()` used across all service tests; `ApiFactory` shared by Tasks 3 and 9.
- **Note:** Task 3 Step 8 documents an intentional intermediate failing state (before middleware lands in Step 9 of the same task).
