using Application.Abstractions;
using Application.Behaviors;
using Application.Results;
using Application.Features.Members;
using Application.Features.Provisioning;
using Application.Mediation;
using Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IMediator, Mediator>();

        // Registration order is chain order, outside in. Permission comes before validation so an
        // unauthorised caller never learns the shape of a request it may not send.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));

        services.AddScoped<IRequestHandler<ListMembersQuery, IReadOnlyList<TenantMember>>, ListMembersHandler>();
        services.AddScoped<IRequestHandler<ReplaceMemberRolesCommand, Unit>, ReplaceMemberRolesHandler>();
        services.AddScoped<IRequestHandler<RevokeMemberCommand, Unit>, RevokeMemberHandler>();
        services.AddScoped<IRequestHandler<DisableMemberCommand, Unit>, DisableMemberHandler>();
        services.AddScoped<IRequestHandler<EnableMemberCommand, Unit>, EnableMemberHandler>();

        services.AddScoped<TenantProvisioningHandler>();
        services.AddScoped<IOutboxMessageHandler>(provider =>
            provider.GetRequiredService<TenantProvisioningHandler>());

        return services;
    }
}
