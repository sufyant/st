using Api;
using Api.Persistence;
using ControlPlane.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiPipeline();
builder.Services.AddControlPlaneModule();

var app = builder.Build();

if (args is [MigrationStep.Command, ..])
{
    await MigrationStep.RunAsync(app.Services, CancellationToken.None);
    return;
}

app.UseApiPipeline();
app.MapV1();

app.Run();
