using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;

namespace ExpenseTracker.Api.Tests.Services;

public class DashboardServiceTests
{
    private static DashboardService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<int> SeedAsync(AppDbContext db)
    {
        db.Users.Add(new User { Id = "u1", Email = "a@t.com", UserName = "a@t.com", DisplayName = "A" });
        var board = new Board { Name = "B", OwnerId = "u1" };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = "u1", Role = BoardRole.Owner });
        // Food: 10 + 20 (Jul 1, Jul 2) ; Transport: 5 (Aug 1)
        db.Expenses.AddRange(
            new Expense { Board = board, CategoryId = 1, CreatedByUserId = "u1", Name = "a", Amount = 10, Date = new DateOnly(2026, 7, 1) },
            new Expense { Board = board, CategoryId = 1, CreatedByUserId = "u1", Name = "b", Amount = 20, Date = new DateOnly(2026, 7, 2) },
            new Expense { Board = board, CategoryId = 2, CreatedByUserId = "u1", Name = "c", Amount = 5, Date = new DateOnly(2026, 8, 1) });
        await db.SaveChangesAsync();
        return board.Id;
    }

    [Fact]
    public async Task SpendByCategory_SumsAndOrdersDescending()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var result = await Sut(db).GetSpendByCategoryAsync(boardId, "u1", null, null, default);

        result.Should().HaveCount(2);
        result[0].CategoryName.Should().Be("Food");
        result[0].Total.Should().Be(30);
        result[1].Total.Should().Be(5);
    }

    [Fact]
    public async Task SpendByCategory_RespectsDateRange()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var result = await Sut(db).GetSpendByCategoryAsync(
            boardId, "u1", new DateOnly(2026, 8, 1), null, default);

        result.Should().ContainSingle(r => r.CategoryName == "Transport");
    }

    [Fact]
    public async Task SpendOverTime_MonthBucketsAndDayBuckets()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);
        var sut = Sut(db);

        var monthly = await sut.GetSpendOverTimeAsync(boardId, "u1", null, null, "month", default);
        monthly.Should().HaveCount(2);
        monthly[0].PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        monthly[0].Total.Should().Be(30);

        var daily = await sut.GetSpendOverTimeAsync(boardId, "u1", null, null, "day", default);
        daily.Should().HaveCount(3);
    }

    [Fact]
    public async Task SpendOverTime_InvalidInterval_Throws400_NonMember404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedAsync(db);

        var badInterval = () => Sut(db).GetSpendOverTimeAsync(boardId, "u1", null, null, "year", default);
        await badInterval.Should().ThrowAsync<BadRequestException>();

        db.Users.Add(new User { Id = "x", Email = "x@t.com", UserName = "x@t.com", DisplayName = "X" });
        await db.SaveChangesAsync();
        var nonMember = () => Sut(db).GetSpendByCategoryAsync(boardId, "x", null, null, default);
        await nonMember.Should().ThrowAsync<NotFoundException>();
    }
}
