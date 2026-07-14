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
