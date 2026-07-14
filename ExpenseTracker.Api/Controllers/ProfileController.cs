using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct) =>
        Ok(await profileService.GetAsync(User.GetUserId(), ct));

    [HttpPut]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profileService.UpdateAsync(User.GetUserId(), request, ct));
}
