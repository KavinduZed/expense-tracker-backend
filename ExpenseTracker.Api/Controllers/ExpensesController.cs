using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
public class ExpensesController(IExpenseService expenseService) : ControllerBase
{
    [HttpGet("api/boards/{boardId:int}/expenses")]
    public async Task<ActionResult<PagedResponse<ExpenseDto>>> List(
        int boardId, [FromQuery] ExpenseListQuery query, CancellationToken ct) =>
        Ok(await expenseService.GetExpensesAsync(boardId, User.GetUserId(), query, ct));

    [HttpPost("api/boards/{boardId:int}/expenses")]
    public async Task<ActionResult<ExpenseDto>> Create(
        int boardId, CreateExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenseService.CreateAsync(boardId, User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = expense.Id }, expense);
    }

    [HttpGet("api/expenses/{id:int}")]
    public async Task<ActionResult<ExpenseDto>> Get(int id, CancellationToken ct) =>
        Ok(await expenseService.GetExpenseAsync(id, User.GetUserId(), ct));

    [HttpPut("api/expenses/{id:int}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        int id, UpdateExpenseRequest request, CancellationToken ct) =>
        Ok(await expenseService.UpdateAsync(id, User.GetUserId(), request, ct));

    [HttpDelete("api/expenses/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await expenseService.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}
