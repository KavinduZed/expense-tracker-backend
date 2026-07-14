namespace ExpenseTracker.Api.Models;

public enum BoardRole { Owner = 0, Member = 1 }

public class BoardMember
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board? Board { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public BoardRole Role { get; set; } = BoardRole.Member;
}
