using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace WolverineRlsSpike;

public static class SpikeHost
{
    // Only the "red" run turns the interceptor off.
    public static bool UseInterceptor { get; set; } =
        Environment.GetEnvironmentVariable("SPIKE_NO_INTERCEPTOR") is null;

    public static async Task<IHost> StartAsync(string appConnectionString, int? maxPoolSize = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(appConnectionString);
        if (maxPoolSize is { } max)
        {
            builder.MaxPoolSize = max;
        }

        var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        var interceptor = new TenantTransactionInterceptor();

        var host = Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ApplicationAssembly = typeof(SpikeHost).Assembly;

                opts.PersistMessagesWithPostgresql(appConnectionString, "wolverine");
                opts.UseEntityFrameworkCoreTransactions();
                opts.Policies.AutoApplyTransactions();
                opts.Policies.UseDurableLocalQueues();
                opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

                // Test 9: stock Wolverine does not retry a saga version conflict on its own.
                opts.OnException<SagaConcurrencyException>().RetryTimes(3);

                opts.Services.AddSingleton(dataSource);
                opts.Services.AddDbContextWithWolverineIntegration<SpikeDbContext>((sp, o) =>
                {
                    o.UseNpgsql(dataSource);
                    if (UseInterceptor)
                    {
                        o.AddInterceptors(interceptor);
                    }
                }, "wolverine");
                opts.Services.AddDbContextWithWolverineIntegration<AuditDbContext>((sp, o) =>
                {
                    o.UseNpgsql(dataSource);
                    if (UseInterceptor)
                    {
                        o.AddInterceptors(interceptor);
                    }
                }, "wolverine");
            })
            .Build();

        await host.StartAsync();
        return host;
    }
}
