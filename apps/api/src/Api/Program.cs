using Api;
using Api.SystemAdmins;
using Api.Persistence;
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
v1.MapControlPlaneEndpoints(v1.MapSystem());

app.Run();
