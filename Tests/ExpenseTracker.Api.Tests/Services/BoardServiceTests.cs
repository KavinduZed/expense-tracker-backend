using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Tests.Services;

public class BoardServiceTests
{
    private static BoardService Sut(AppDbContext db) => new(db, new BoardAccessGuard(db));

    private static async Task<(string ownerId, string otherId)> SeedUsersAsync(AppDbContext db)
    {
        db.Users.Add(new User { Id = "owner", Email = "o@t.com", UserName = "o@t.com", DisplayName = "Owner" });
        db.Users.Add(new User { Id = "other", Email = "x@t.com", UserName = "x@t.com", DisplayName = "Other" });
        await db.SaveChangesAsync();
        return ("owner", "other");
    }

    [Fact]
    public async Task Create_MakesCallerOwnerMember()
    {
        await using var db = TestDb.Create();
        var (ownerId, _) = await SeedUsersAsync(db);

        var board = await Sut(db).CreateBoardAsync(ownerId, new CreateBoardRequest("Trip"), default);

        board.Name.Should().Be("Trip");
        board.Role.Should().Be("Owner");
        (await db.BoardMembers.CountAsync(m => m.BoardId == board.Id && m.UserId == ownerId))
            .Should().Be(1);
    }

    [Fact]
    public async Task GetBoards_OnlyReturnsBoardsUserBelongsTo()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var sut = Sut(db);
        await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);
        await sut.CreateBoardAsync(otherId, new CreateBoardRequest("Theirs"), default);

        var boards = await sut.GetBoardsAsync(ownerId, default);
        boards.Should().ContainSingle(b => b.Name == "Mine");
    }

    [Fact]
    public async Task GetBoard_NonMember_Throws404()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var board = await Sut(db).CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);

        var act = () => Sut(db).GetBoardAsync(board.Id, otherId, default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_NonOwnerMember_Throws409_Delete_ByOwner_Removes()
    {
        await using var db = TestDb.Create();
        var (ownerId, otherId) = await SeedUsersAsync(db);
        var sut = Sut(db);
        var board = await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);
        await sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);

        var act = () => sut.UpdateBoardAsync(board.Id, otherId, new UpdateBoardRequest("Hijack"), default);
        await act.Should().ThrowAsync<ConflictException>();

        await sut.DeleteBoardAsync(board.Id, ownerId, default);
        (await db.Boards.AnyAsync(b => b.Id == board.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AddMember_UnknownEmail404_Duplicate409_RemoveOwner409()
    {
        await using var db = TestDb.Create();
        var (ownerId, _) = await SeedUsersAsync(db);
        var sut = Sut(db);
        var board = await sut.CreateBoardAsync(ownerId, new CreateBoardRequest("Mine"), default);

        var unknown = () => sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("ghost@t.com"), default);
        await unknown.Should().ThrowAsync<NotFoundException>();

        await sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);
        var dupe = () => sut.AddMemberAsync(board.Id, ownerId, new AddMemberRequest("x@t.com"), default);
        await dupe.Should().ThrowAsync<ConflictException>();

        var removeOwner = () => sut.RemoveMemberAsync(board.Id, ownerId, ownerId, default);
        await removeOwner.Should().ThrowAsync<ConflictException>();
    }
}
