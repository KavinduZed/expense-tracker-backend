using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<CategorySpendDto>> GetSpendByCategoryAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<TimePointDto>> GetSpendOverTimeAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, string interval, CancellationToken ct);
}
