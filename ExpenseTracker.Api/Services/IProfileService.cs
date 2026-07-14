using ExpenseTracker.Api.DTOs;

namespace ExpenseTracker.Api.Services;

public interface IProfileService
{
    Task<UserDto> GetAsync(string userId, CancellationToken ct);
    Task<UserDto> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken ct);
}
