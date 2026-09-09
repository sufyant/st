using Serilog;
using Serilog.Events;

namespace Api.Host;

public static class SerilogConfigurationExtensions
{
    /// <summary>
    /// Applies the minimum-level policy every host running this app must use, most importantly
    /// suppressing ASP.NET Core's own Information-level request logging (which otherwise writes
    /// full request URLs — including the JWT SignalR clients pass via <c>?access_token=</c> — to
    /// Console/Seq). Program.cs and CustomWebApplicationFactory.CreateHost both call into
    /// UseSerilog, and Serilog.AspNetCore's bootstrap-logger mechanism means whichever call runs
    /// last wins outright, silently discarding the other's configuration. Routing both through
    /// this one method is what keeps them from drifting apart again.
    /// </summary>
    public static LoggerConfiguration ApplyStandardMinimumLevel(this LoggerConfiguration configuration) =>
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);
}
