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
    public async Task Register_WeakPassword_Returns400()
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@test.com", "aaaaaaaa", "Test User");
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);
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
