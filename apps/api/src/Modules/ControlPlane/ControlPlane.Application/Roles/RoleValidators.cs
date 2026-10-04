using FluentValidation;

namespace ControlPlane.Application.Roles;

public sealed class CreateRoleValidator : AbstractValidator<CreateRole>
{
    public CreateRoleValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
        RuleFor(command => command.Permissions).NotNull();
    }
}

public sealed class ChangeRoleValidator : AbstractValidator<ChangeRole>
{
    public ChangeRoleValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
        RuleFor(command => command.Permissions).NotNull();
    }
}
