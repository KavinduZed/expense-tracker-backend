using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
