using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class ProfileService(AppDbContext db) : IProfileService
{
    public async Task<UserDto> GetAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        return new UserDto(user.Id, user.Email!, user.DisplayName, user.Currency);
    }

    public async Task<UserDto> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        user.DisplayName = request.DisplayName;
        user.Currency = request.Currency;
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email!, user.DisplayName, user.Currency);
    }
}
