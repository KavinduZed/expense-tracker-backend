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
