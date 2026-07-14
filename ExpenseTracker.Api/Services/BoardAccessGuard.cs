using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class BoardAccessGuard(AppDbContext db) : IBoardAccessGuard
{
    public async Task EnsureMemberAsync(int boardId, string userId, CancellationToken ct)
    {
        var isMember = await db.BoardMembers.AnyAsync(
            m => m.BoardId == boardId && m.UserId == userId, ct);
        if (!isMember) throw new NotFoundException("Board not found.");
    }

    public async Task EnsureOwnerAsync(int boardId, string userId, CancellationToken ct)
    {
        var membership = await db.BoardMembers.SingleOrDefaultAsync(
            m => m.BoardId == boardId && m.UserId == userId, ct)
            ?? throw new NotFoundException("Board not found.");
        if (membership.Role != BoardRole.Owner)
            throw new ConflictException("Only the board owner can do this.");
    }
}
