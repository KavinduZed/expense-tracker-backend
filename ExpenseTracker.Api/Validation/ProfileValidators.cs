using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
