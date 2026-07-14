namespace ExpenseTracker.Api.DTOs;

public record ExpenseDto(
    int Id, int BoardId, int CategoryId, string CategoryName, string Name,
    decimal Amount, DateOnly Date, string? Description, string CreatedByUserId, DateTime CreatedAt);
public record CreateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description);
public record UpdateExpenseRequest(string Name, decimal Amount, int CategoryId, DateOnly Date, string? Description);
public record ExpenseListQuery(DateOnly? From = null, DateOnly? To = null, int? CategoryId = null,
    int Page = 1, int PageSize = 20);
public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
