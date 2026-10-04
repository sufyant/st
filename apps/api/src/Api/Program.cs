using Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiPipeline();

var app = builder.Build();
app.UseApiPipeline();
app.MapV1();

app.Run();
