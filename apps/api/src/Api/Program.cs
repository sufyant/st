using Api;
using Api.Admin;
using Api.Persistence;
using Api.Tenants;
using Audit.Api;
using ControlPlane.Api;
using Notifications.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiPipeline(ControlPlaneModule.HandlerAssembly, NotificationsModule.HandlerAssembly, AuditModule.HandlerAssembly);
builder.Services.AddControlPlaneModule();
builder.Services.AddNotificationsModule();
builder.Services.AddAuditModule();

var app = builder.Build();

if (args is [MigrationStep.Command, ..])
{
    await MigrationStep.RunAsync(app.Services, CancellationToken.None);
    return;
}

app.UseApiPipeline();

var v1 = app.MapV1();
var admin = v1.MapAdmin();
var tenant = v1.MapTenant();
v1.MapControlPlaneEndpoints(tenant, admin, admin.MapAdminTenant());

app.Run();
