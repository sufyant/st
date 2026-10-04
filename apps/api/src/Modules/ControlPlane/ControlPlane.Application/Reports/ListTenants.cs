using ControlPlane.Application.Ports;
using FluentValidation;
using SharedKernel;

namespace ControlPlane.Application.Reports;

/// <summary>Every tenant with its status and member count, a page at a time, for system admins (0031, 0034).</summary>
public sealed record ListTenants(int Page, int PageSize);

public sealed class ListTenantsValidator : AbstractValidator<ListTenants>
{
    public ListTenantsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PagedList<TenantSummary>.MaxPageSize);
    }
}

public static class ListTenantsHandler
{
    public static async Task<Result<PagedList<TenantSummary>>> HandleAsync(ListTenants query, ITenantReport report, CancellationToken cancellationToken) =>
        await report.ListTenantsAsync(query.Page, query.PageSize, cancellationToken);
}
