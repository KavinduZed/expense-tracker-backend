using System.Security.Claims;
using ExpenseTracker.Api.Exceptions;

namespace ExpenseTracker.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedException("Missing user id claim.");
}
