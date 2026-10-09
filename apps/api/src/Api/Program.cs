using Api;
using Api.Hosting;
using Api.SystemAdmins;
using Api.Tenants;
using Api.Persistence;
using Audit.Api;
using Audit.Infrastructure;
using ControlPlane.Api;
using ControlPlane.Infrastructure;
using Notifications.Api;
using Notifications.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var role = builder.AddApiPipeline(ControlPlaneModule.HandlerAssembly, NotificationsModule.HandlerAssembly, AuditModule.HandlerAssembly);
builder.Services.AddControlPlaneInfrastructure();
builder.Services.AddNotificationsInfrastructure();
builder.Services.AddAuditInfrastructure();

var app = builder.Build();

if (args is [MigrationStep.Command, ..])
{
    await MigrationStep.RunAsync(app.Services, CancellationToken.None);
    return;
}

app.UseApiPipeline();

// A worker answers only the health endpoints (section 1).
if (role.ServesTheApi())
{
    var v1 = app.MapV1();
    v1.MapSignedIn().MapControlPlaneEndpoints(v1.MapSystem(), v1.MapTenant());
    app.MapApiDocuments();
}

app.Run();
