using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class CategoryServiceTests
{
    [Fact]
    public async Task GetAll_ReturnsSeededCategories()
    {
        await using var db = TestDb.Create();
        var all = await new CategoryService(db).GetAllAsync(default);
        all.Should().HaveCount(7);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws409()
    {
        await using var db = TestDb.Create();
        var sut = new CategoryService(db);
        var act = () => sut.CreateAsync(new CreateCategoryRequest("Food", null), default);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Create_Update_Delete_Roundtrip()
    {
        await using var db = TestDb.Create();
        var sut = new CategoryService(db);

        var created = await sut.CreateAsync(new CreateCategoryRequest("Pets", "pets"), default);
        created.IsDefault.Should().BeFalse();

        var updated = await sut.UpdateAsync(created.Id, new UpdateCategoryRequest("Pet Care", "pets"), default);
        updated.Name.Should().Be("Pet Care");

        await sut.DeleteAsync(created.Id, default);
        (await db.Categories.AnyAsync(c => c.Id == created.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_CategoryInUse_Throws409()
    {
        await using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", Email = "a@b.com", UserName = "a@b.com", DisplayName = "A" });
        var board = new Board { Name = "B", OwnerId = "u1" };
        db.Boards.Add(board);
        db.Expenses.Add(new Expense
        {
            Board = board, CategoryId = 1, CreatedByUserId = "u1",
            Name = "Lunch", Amount = 5m, Date = new DateOnly(2026, 7, 1)
        });
        await db.SaveChangesAsync();

        var act = () => new CategoryService(db).DeleteAsync(1, default);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Update_UnknownId_Throws404()
    {
        await using var db = TestDb.Create();
        var act = () => new CategoryService(db).UpdateAsync(999, new UpdateCategoryRequest("X", null), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
