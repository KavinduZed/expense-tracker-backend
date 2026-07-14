using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}
