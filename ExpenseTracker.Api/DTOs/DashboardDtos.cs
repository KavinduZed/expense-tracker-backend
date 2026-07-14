namespace ExpenseTracker.Api.DTOs;

public record CategorySpendDto(int CategoryId, string CategoryName, decimal Total);
public record TimePointDto(DateOnly PeriodStart, decimal Total);
