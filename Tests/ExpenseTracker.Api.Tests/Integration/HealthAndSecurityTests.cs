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
