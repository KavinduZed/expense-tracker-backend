using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct) =>
        await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Icon, c.IsDefault))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var duplicate = await db.Categories.AnyAsync(c => c.Name == request.Name, ct);
        if (duplicate) throw new ConflictException("A category with that name already exists.");

        var category = new Category { Name = request.Name, Icon = request.Icon, IsDefault = false };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Icon, category.IsDefault);
    }

    public async Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        var duplicate = await db.Categories.AnyAsync(c => c.Name == request.Name && c.Id != id, ct);
        if (duplicate) throw new ConflictException("A category with that name already exists.");

        category.Name = request.Name;
        category.Icon = request.Icon;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Icon, category.IsDefault);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        var inUse = await db.Expenses.AnyAsync(e => e.CategoryId == id, ct);
        if (inUse) throw new ConflictException("Category is in use by expenses and cannot be deleted.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}
