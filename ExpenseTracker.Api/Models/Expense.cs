namespace ExpenseTracker.Api.Models;

public class Expense
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board? Board { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public User? CreatedByUser { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
