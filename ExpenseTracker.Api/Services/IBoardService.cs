using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IBoardService
{
    Task<IReadOnlyList<BoardDto>> GetBoardsAsync(string userId, CancellationToken ct);
    Task<BoardDto> GetBoardAsync(int boardId, string userId, CancellationToken ct);
    Task<BoardDto> CreateBoardAsync(string userId, CreateBoardRequest request, CancellationToken ct);
    Task<BoardDto> UpdateBoardAsync(int boardId, string userId, UpdateBoardRequest request, CancellationToken ct);
    Task DeleteBoardAsync(int boardId, string userId, CancellationToken ct);
    Task<IReadOnlyList<BoardMemberDto>> GetMembersAsync(int boardId, string userId, CancellationToken ct);
    Task<BoardMemberDto> AddMemberAsync(int boardId, string userId, AddMemberRequest request, CancellationToken ct);
    Task RemoveMemberAsync(int boardId, string userId, string memberUserId, CancellationToken ct);
}
