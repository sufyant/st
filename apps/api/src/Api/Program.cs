using Api;
using Api.Admin;
using Api.Persistence;
using Api.Tenants;
using ControlPlane.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiPipeline(ControlPlaneModule.HandlerAssembly);
builder.Services.AddControlPlaneModule();

var app = builder.Build();

if (args is [MigrationStep.Command, ..])
{
    await MigrationStep.RunAsync(app.Services, CancellationToken.None);
    return;
}

app.UseApiPipeline();

var v1 = app.MapV1();
var admin = v1.MapAdmin();
v1.MapControlPlaneEndpoints(v1.MapTenant(), admin, admin.MapAdminTenant());

app.Run();
