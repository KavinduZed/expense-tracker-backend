using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
