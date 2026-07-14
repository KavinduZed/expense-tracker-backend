namespace ExpenseTracker.Api.DTOs;

public record BoardDto(int Id, string Name, string OwnerId, DateTime CreatedAt, string Role, int MemberCount);
public record CreateBoardRequest(string Name);
public record UpdateBoardRequest(string Name);
public record BoardMemberDto(string UserId, string Email, string DisplayName, string Role);
public record AddMemberRequest(string Email);
