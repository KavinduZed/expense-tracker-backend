namespace ExpenseTracker.Api.Models;

public class Board
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public User? Owner { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BoardMember> Members { get; set; } = new List<BoardMember>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
