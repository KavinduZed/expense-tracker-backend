using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IExpenseService
{
    Task<PagedResponse<ExpenseDto>> GetExpensesAsync(int boardId, string userId, ExpenseListQuery query, CancellationToken ct);
    Task<ExpenseDto> GetExpenseAsync(int id, string userId, CancellationToken ct);
    Task<ExpenseDto> CreateAsync(int boardId, string userId, CreateExpenseRequest request, CancellationToken ct);
    Task<ExpenseDto> UpdateAsync(int id, string userId, UpdateExpenseRequest request, CancellationToken ct);
    Task DeleteAsync(int id, string userId, CancellationToken ct);
}
