using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Api.IntegrationTests;

// The composed application, as Program builds it, in development and against the given database.
internal sealed class ApiFactory(string pooledConnectionString, string? migrationsConnectionString = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Pooled", pooledConnectionString);
        builder.UseSetting("ConnectionStrings:Migrations", migrationsConnectionString);
    }
}
