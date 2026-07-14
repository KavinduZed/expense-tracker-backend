using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/boards")]
public class BoardsController(IBoardService boardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BoardDto>>> GetAll(CancellationToken ct) =>
        Ok(await boardService.GetBoardsAsync(User.GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BoardDto>> Get(int id, CancellationToken ct) =>
        Ok(await boardService.GetBoardAsync(id, User.GetUserId(), ct));

    [HttpPost]
    public async Task<ActionResult<BoardDto>> Create(CreateBoardRequest request, CancellationToken ct)
    {
        var board = await boardService.CreateBoardAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = board.Id }, board);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BoardDto>> Update(int id, UpdateBoardRequest request, CancellationToken ct) =>
        Ok(await boardService.UpdateBoardAsync(id, User.GetUserId(), request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await boardService.DeleteBoardAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet("{id:int}/members")]
    public async Task<ActionResult<IReadOnlyList<BoardMemberDto>>> GetMembers(int id, CancellationToken ct) =>
        Ok(await boardService.GetMembersAsync(id, User.GetUserId(), ct));

    [HttpPost("{id:int}/members")]
    public async Task<ActionResult<BoardMemberDto>> AddMember(int id, AddMemberRequest request, CancellationToken ct)
    {
        var member = await boardService.AddMemberAsync(id, User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetMembers), new { id }, member);
    }

    [HttpDelete("{id:int}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int id, string userId, CancellationToken ct)
    {
        await boardService.RemoveMemberAsync(id, User.GetUserId(), userId, ct);
        return NoContent();
    }
}
