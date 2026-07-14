using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Models;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BoardMember> BoardMemberships { get; set; } = new List<BoardMember>();
}
