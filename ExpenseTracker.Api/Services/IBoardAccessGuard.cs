namespace ExpenseTracker.Api.Services;

public interface IBoardAccessGuard
{
    Task EnsureMemberAsync(int boardId, string userId, CancellationToken ct);
    Task EnsureOwnerAsync(int boardId, string userId, CancellationToken ct);
}
