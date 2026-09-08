using FluentValidation;

namespace Api.Application.Tenants;

public sealed class RenameTenantCommandValidator : AbstractValidator<RenameTenantCommand>
{
    public RenameTenantCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.NewName).NotEmpty();
    }
}
