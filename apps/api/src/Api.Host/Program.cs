using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<Api.Infrastructure.AdminDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AdminDb"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Api.Infrastructure.AdminDbContext.SchemaName)));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = builder.Configuration["Clerk:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Clerk:Authority"],
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/v1/whoami", (HttpContext context) =>
{
    var userId = context.User.FindFirstValue("sub");
    return Results.Ok(new { userId });
}).RequireAuthorization();

app.Run();

public partial class Program;
