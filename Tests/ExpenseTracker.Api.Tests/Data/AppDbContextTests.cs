using ExpenseTracker.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task SeedsSevenDefaultCategories()
    {
        await using var db = TestDb.Create();
        var categories = await db.Categories.ToListAsync();
        categories.Should().HaveCount(7);
        categories.Should().OnlyContain(c => c.IsDefault);
        categories.Select(c => c.Name).Should().Contain(new[] { "Food", "Transport", "Other" });
    }

    [Fact]
    public async Task CanPersistFullObjectGraph()
    {
        await using var db = TestDb.Create();
        var user = new User { Id = "u1", UserName = "a@b.com", Email = "a@b.com", DisplayName = "Alice" };
        var board = new Board { Name = "Personal", OwnerId = "u1" };
        db.Users.Add(user);
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = "u1", Role = BoardRole.Owner });
        db.Expenses.Add(new Expense
        {
            Board = board, CategoryId = 1, CreatedByUserId = "u1",
            Name = "Lunch", Amount = 12.50m, Date = new DateOnly(2026, 7, 1)
        });
        await db.SaveChangesAsync();

        var expense = await db.Expenses.Include(e => e.Category).SingleAsync();
        expense.Amount.Should().Be(12.50m);
        expense.Category!.Name.Should().Be("Food");
    }
}
