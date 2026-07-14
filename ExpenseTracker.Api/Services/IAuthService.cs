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
