using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId:int}/dashboard")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("spend-by-category")]
    public async Task<ActionResult<IReadOnlyList<CategorySpendDto>>> SpendByCategory(
        int boardId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await dashboardService.GetSpendByCategoryAsync(boardId, User.GetUserId(), from, to, ct));

    [HttpGet("spend-over-time")]
    public async Task<ActionResult<IReadOnlyList<TimePointDto>>> SpendOverTime(
        int boardId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string interval = "month", CancellationToken ct = default) =>
        Ok(await dashboardService.GetSpendOverTimeAsync(boardId, User.GetUserId(), from, to, interval, ct));
}
