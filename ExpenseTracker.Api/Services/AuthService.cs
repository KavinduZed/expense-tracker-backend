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
            var isDuplicate = result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            if (isDuplicate)
                throw new ConflictException(errors);
            throw new BadRequestException(errors);
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
