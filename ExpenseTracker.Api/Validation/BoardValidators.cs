using ExpenseTracker.Api.DTOs;
using FluentValidation;

namespace ExpenseTracker.Api.Validation;

public class CreateBoardRequestValidator : AbstractValidator<CreateBoardRequest>
{
    public CreateBoardRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public class UpdateBoardRequestValidator : AbstractValidator<UpdateBoardRequest>
{
    public UpdateBoardRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
}
