using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class BoardService(AppDbContext db, IBoardAccessGuard guard) : IBoardService
{
    public async Task<IReadOnlyList<BoardDto>> GetBoardsAsync(string userId, CancellationToken ct) =>
        await db.BoardMembers
            .Where(m => m.UserId == userId)
            .Select(m => new BoardDto(
                m.Board!.Id, m.Board.Name, m.Board.OwnerId, m.Board.CreatedAt,
                m.Role.ToString(), m.Board.Members.Count))
            .ToListAsync(ct);

    public async Task<BoardDto> GetBoardAsync(int boardId, string userId, CancellationToken ct)
    {
        var dto = await db.BoardMembers
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => new BoardDto(
                m.Board!.Id, m.Board.Name, m.Board.OwnerId, m.Board.CreatedAt,
                m.Role.ToString(), m.Board.Members.Count))
            .SingleOrDefaultAsync(ct);
        return dto ?? throw new NotFoundException("Board not found.");
    }

    public async Task<BoardDto> CreateBoardAsync(string userId, CreateBoardRequest request, CancellationToken ct)
    {
        var board = new Board { Name = request.Name, OwnerId = userId };
        db.Boards.Add(board);
        db.BoardMembers.Add(new BoardMember { Board = board, UserId = userId, Role = BoardRole.Owner });
        await db.SaveChangesAsync(ct);
        return new BoardDto(board.Id, board.Name, board.OwnerId, board.CreatedAt, "Owner", 1);
    }

    public async Task<BoardDto> UpdateBoardAsync(int boardId, string userId, UpdateBoardRequest request, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var board = await db.Boards.Include(b => b.Members).SingleAsync(b => b.Id == boardId, ct);
        board.Name = request.Name;
        await db.SaveChangesAsync(ct);
        return new BoardDto(board.Id, board.Name, board.OwnerId, board.CreatedAt, "Owner", board.Members.Count);
    }

    public async Task DeleteBoardAsync(int boardId, string userId, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var board = await db.Boards.SingleAsync(b => b.Id == boardId, ct);
        db.Boards.Remove(board); // cascades to members + expenses
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BoardMemberDto>> GetMembersAsync(int boardId, string userId, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        return await db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .Select(m => new BoardMemberDto(
                m.UserId, m.User!.Email!, m.User.DisplayName, m.Role.ToString()))
            .ToListAsync(ct);
    }

    public async Task<BoardMemberDto> AddMemberAsync(int boardId, string userId, AddMemberRequest request, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email, ct)
            ?? throw new NotFoundException("No user with that email.");
        var exists = await db.BoardMembers.AnyAsync(
            m => m.BoardId == boardId && m.UserId == user.Id, ct);
        if (exists) throw new ConflictException("User is already a member of this board.");

        db.BoardMembers.Add(new BoardMember { BoardId = boardId, UserId = user.Id, Role = BoardRole.Member });
        await db.SaveChangesAsync(ct);
        return new BoardMemberDto(user.Id, user.Email!, user.DisplayName, "Member");
    }

    public async Task RemoveMemberAsync(int boardId, string userId, string memberUserId, CancellationToken ct)
    {
        await guard.EnsureOwnerAsync(boardId, userId, ct);
        var membership = await db.BoardMembers.SingleOrDefaultAsync(
            m => m.BoardId == boardId && m.UserId == memberUserId, ct)
            ?? throw new NotFoundException("Member not found on this board.");
        if (membership.Role == BoardRole.Owner)
            throw new ConflictException("The board owner cannot be removed.");
        db.BoardMembers.Remove(membership);
        await db.SaveChangesAsync(ct);
    }
}
