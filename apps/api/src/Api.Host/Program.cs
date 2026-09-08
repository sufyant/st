using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<Api.Infrastructure.AdminDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AdminDb"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Api.Infrastructure.AdminDbContext.SchemaName)));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
