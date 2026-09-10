using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Provisioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provider = scope.ServiceProvider;
                var drainer = new OutboxDrainer(
                    provider.GetRequiredService<ControlPlaneDbContext>(),
                    message => provider.GetRequiredService<TenantProvisioningHandler>()
                        .HandleAsync(message, stoppingToken),
                    timeProvider);
                await drainer.DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox drain failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
