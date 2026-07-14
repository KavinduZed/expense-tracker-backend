namespace ExpenseTracker.Api.DTOs;

public record CategoryDto(int Id, string Name, string? Icon, bool IsDefault);
public record CreateCategoryRequest(string Name, string? Icon);
public record UpdateCategoryRequest(string Name, string? Icon);
