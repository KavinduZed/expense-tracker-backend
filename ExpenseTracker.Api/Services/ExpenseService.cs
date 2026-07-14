using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class ExpenseService(AppDbContext db, IBoardAccessGuard guard) : IExpenseService
{
    public async Task<PagedResponse<ExpenseDto>> GetExpensesAsync(
        int boardId, string userId, ExpenseListQuery query, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(query.Page, 1);

        var q = db.Expenses.Include(e => e.Category).Where(e => e.BoardId == boardId);
        if (query.From is { } from) q = q.Where(e => e.Date >= from);
        if (query.To is { } to) q = q.Where(e => e.Date <= to);
        if (query.CategoryId is { } categoryId) q = q.Where(e => e.CategoryId == categoryId);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => ToDtoExpression(e))
            .ToListAsync(ct);
        return new PagedResponse<ExpenseDto>(items, page, pageSize, total);
    }

    public async Task<ExpenseDto> GetExpenseAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        return ToDto(expense);
    }

    public async Task<ExpenseDto> CreateAsync(
        int boardId, string userId, CreateExpenseRequest request, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new BadRequestException("Unknown category.");

        var expense = new Expense
        {
            BoardId = boardId,
            CategoryId = category.Id,
            CreatedByUserId = userId,
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date,
            Description = request.Description,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        expense.Category = category;
        return ToDto(expense);
    }

    public async Task<ExpenseDto> UpdateAsync(
        int id, string userId, UpdateExpenseRequest request, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new BadRequestException("Unknown category.");

        expense.Name = request.Name;
        expense.Amount = request.Amount;
        expense.CategoryId = category.Id;
        expense.Category = category;
        expense.Date = request.Date;
        expense.Description = request.Description;
        await db.SaveChangesAsync(ct);
        return ToDto(expense);
    }

    public async Task DeleteAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await FindAccessibleAsync(id, userId, ct);
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Expense> FindAccessibleAsync(int id, string userId, CancellationToken ct)
    {
        var expense = await db.Expenses.Include(e => e.Category)
            .SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Expense not found.");
        await guard.EnsureMemberAsync(expense.BoardId, userId, ct); // non-member -> 404
        return expense;
    }

    private static ExpenseDto ToDtoExpression(Expense e) =>
        new(e.Id, e.BoardId, e.CategoryId, e.Category!.Name, e.Name,
            e.Amount, e.Date, e.Description, e.CreatedByUserId, e.CreatedAt);

    private static ExpenseDto ToDto(Expense e) => ToDtoExpression(e);
}
