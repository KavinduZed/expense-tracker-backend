using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class ExpenseServiceTests
{
    private static ExpenseService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<int> SeedBoardAsync(AppDbContext db, string userId = "u1")
    {
        db.Users.Add(new User { Id = userId, Email = $"{userId}@t.com", UserName = $"{userId}@t.com", DisplayName = userId });
        var board = new Board { Name = "B", OwnerId = userId };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = userId, Role = BoardRole.Owner });
        await db.SaveChangesAsync();
        return board.Id;
    }

    [Fact]
    public async Task Create_ReturnsDtoWithCategoryName()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);

        var dto = await Sut(db).CreateAsync(boardId, "u1",
            new CreateExpenseRequest("Lunch", 12.50m, 1, new DateOnly(2026, 7, 1), null), default);

        dto.CategoryName.Should().Be("Food");
        dto.Amount.Should().Be(12.50m);
    }

    [Fact]
    public async Task Create_UnknownCategory_Throws400()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var act = () => Sut(db).CreateAsync(boardId, "u1",
            new CreateExpenseRequest("X", 1m, 999, new DateOnly(2026, 7, 1), null), default);
        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Create_NonMember_Throws404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        db.Users.Add(new User { Id = "intruder", Email = "i@t.com", UserName = "i@t.com", DisplayName = "I" });
        await db.SaveChangesAsync();

        var act = () => Sut(db).CreateAsync(boardId, "intruder",
            new CreateExpenseRequest("X", 1m, 1, new DateOnly(2026, 7, 1), null), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task List_FiltersByDateAndCategory_AndPages()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var sut = Sut(db);
        for (var day = 1; day <= 10; day++)
            await sut.CreateAsync(boardId, "u1", new CreateExpenseRequest(
                $"e{day}", day, day <= 5 ? 1 : 2, new DateOnly(2026, 7, day), null), default);

        var filtered = await sut.GetExpensesAsync(boardId, "u1",
            new ExpenseListQuery(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 7), 1), default);
        filtered.TotalCount.Should().Be(3); // days 3,4,5 are category 1

        var paged = await sut.GetExpensesAsync(boardId, "u1",
            new ExpenseListQuery(null, null, null, Page: 2, PageSize: 4), default);
        paged.Items.Should().HaveCount(4);
        paged.TotalCount.Should().Be(10);
        // newest-first: page 2 starts at the 5th newest (day 6)
        paged.Items[0].Name.Should().Be("e6");
    }

    [Fact]
    public async Task Update_And_Delete_Work_UnknownId404()
    {
        await using var db = TestDb.Create();
        var boardId = await SeedBoardAsync(db);
        var sut = Sut(db);
        var created = await sut.CreateAsync(boardId, "u1",
            new CreateExpenseRequest("Lunch", 10m, 1, new DateOnly(2026, 7, 1), null), default);

        var updated = await sut.UpdateAsync(created.Id, "u1",
            new UpdateExpenseRequest("Dinner", 20m, 2, new DateOnly(2026, 7, 2), "late"), default);
        updated.Name.Should().Be("Dinner");
        updated.CategoryName.Should().Be("Transport");

        await sut.DeleteAsync(created.Id, "u1", default);
        var act = () => sut.GetExpenseAsync(created.Id, "u1", default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetUpdateDelete_ExpenseOnBoardCallerIsNotMemberOf_Throws404()
    {
        await using var db = TestDb.Create();
        var boardAId = await SeedBoardAsync(db, "u1");
        var boardBId = await SeedBoardAsync(db, "u2");
        var sut = Sut(db);

        var expense = await sut.CreateAsync(boardBId, "u2",
            new CreateExpenseRequest("Groceries", 30m, 1, new DateOnly(2026, 7, 1), null), default);

        var getAct = () => sut.GetExpenseAsync(expense.Id, "u1", default);
        await getAct.Should().ThrowAsync<NotFoundException>();

        var updateAct = () => sut.UpdateAsync(expense.Id, "u1",
            new UpdateExpenseRequest("Hacked", 1m, 1, new DateOnly(2026, 7, 1), null), default);
        await updateAct.Should().ThrowAsync<NotFoundException>();

        var deleteAct = () => sut.DeleteAsync(expense.Id, "u1", default);
        await deleteAct.Should().ThrowAsync<NotFoundException>();
    }
}
