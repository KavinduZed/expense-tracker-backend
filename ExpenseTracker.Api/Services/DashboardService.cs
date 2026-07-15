using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class DashboardService(AppDbContext db, IBoardAccessGuard guard) : IDashboardService
{
    public async Task<IReadOnlyList<CategorySpendDto>> GetSpendByCategoryAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await guard.EnsureMemberAsync(boardId, userId, ct);
        var q = Filter(boardId, from, to);
        var grouped = await q
            .GroupBy(e => new { e.CategoryId, e.Category!.Name })
            .Select(g => new CategorySpendDto(g.Key.CategoryId, g.Key.Name, g.Sum(e => e.Amount)))
            .ToListAsync(ct);
        return grouped
            .OrderByDescending(x => x.Total)
            .ToList();
    }

    public async Task<IReadOnlyList<TimePointDto>> GetSpendOverTimeAsync(
        int boardId, string userId, DateOnly? from, DateOnly? to, string interval, CancellationToken ct)
    {
        Func<DateOnly, DateOnly> bucket = interval switch
        {
            "day" => d => d,
            "week" => d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)), // Monday start
            "month" => d => new DateOnly(d.Year, d.Month, 1),
            _ => throw new BadRequestException("interval must be one of: day, week, month."),
        };
        await guard.EnsureMemberAsync(boardId, userId, ct);

        var rows = await Filter(boardId, from, to)
            .Select(e => new { e.Date, e.Amount })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => bucket(r.Date))
            .Select(g => new TimePointDto(g.Key, g.Sum(r => r.Amount)))
            .OrderBy(p => p.PeriodStart)
            .ToList();
    }

    private IQueryable<Models.Expense> Filter(int boardId, DateOnly? from, DateOnly? to)
    {
        var q = db.Expenses.Where(e => e.BoardId == boardId);
        if (from is { } f) q = q.Where(e => e.Date >= f);
        if (to is { } t) q = q.Where(e => e.Date <= t);
        return q;
    }
}
