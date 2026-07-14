namespace ExpenseTracker.Api.DTOs;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record UserDto(string Id, string Email, string DisplayName, string Currency);
public record AuthResponse(
    string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, UserDto User);
