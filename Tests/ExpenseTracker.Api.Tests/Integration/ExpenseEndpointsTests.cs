using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseTracker.Api.DTOs;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Integration;

public class ExpenseEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public ExpenseEndpointsTests(ApiFactory factory) => _client = factory.CreateClient();

    private static RegisterRequest NewUser() => new(
        $"{Guid.NewGuid():N}@test.com", "Passw0rd!x", "Test User");

    private async Task<(string AccessToken, int BoardId)> RegisterAndGetDefaultBoardAsync()
    {
        var register = await _client.PostAsJsonAsync("/api/auth/register", NewUser());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();

        var boardsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        boardsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var boardsResponse = await _client.SendAsync(boardsRequest);
        boardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var boards = await boardsResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        var personalBoard = boards!.Single(b => b.Name == "Personal");

        return (auth.AccessToken, personalBoard.Id);
    }

    private static HttpRequestMessage AuthedJsonRequest(HttpMethod method, string url, string accessToken, object body)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task Create_ValidExpense_Returns201_WithFoodCategoryName()
    {
        var (accessToken, boardId) = await RegisterAndGetDefaultBoardAsync();

        var request = AuthedJsonRequest(HttpMethod.Post, $"/api/boards/{boardId}/expenses", accessToken,
            new CreateExpenseRequest("Lunch", 12.50m, 1, new DateOnly(2026, 7, 1), null));
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ExpenseDto>();
        dto!.CategoryName.Should().Be("Food");
    }

    [Fact]
    public async Task List_AfterCreate_ReturnsCreatedExpense()
    {
        var (accessToken, boardId) = await RegisterAndGetDefaultBoardAsync();

        var createRequest = AuthedJsonRequest(HttpMethod.Post, $"/api/boards/{boardId}/expenses", accessToken,
            new CreateExpenseRequest("Groceries", 45.00m, 1, new DateOnly(2026, 7, 2), null));
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ExpenseDto>();

        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/boards/{boardId}/expenses");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var listResponse = await _client.SendAsync(listRequest);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResponse<ExpenseDto>>();
        page!.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        page.Items.Should().Contain(e => e.Id == created!.Id);
    }

    [Fact]
    public async Task Create_InvalidAmount_Returns400()
    {
        var (accessToken, boardId) = await RegisterAndGetDefaultBoardAsync();

        var request = AuthedJsonRequest(HttpMethod.Post, $"/api/boards/{boardId}/expenses", accessToken,
            new CreateExpenseRequest("Free Sample", 0m, 1, new DateOnly(2026, 7, 1), null));
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/boards/1/expenses",
            new CreateExpenseRequest("Lunch", 12.50m, 1, new DateOnly(2026, 7, 1), null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
