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
