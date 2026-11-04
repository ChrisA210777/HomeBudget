using Budget.Infrastructure;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Identity;
using Budget.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

// In Development the database is created and migrated on startup. Deployed environments
// apply migrations from the pipeline using the migration bundle instead (see ADR-003).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.ApplyMigrationsAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve the Blazor WebAssembly client (see ADR-001). MapStaticAssets serves the client's
// wwwroot and its _framework files (fingerprinted and compressed), so UseBlazorFrameworkFiles
// is not needed. Adding it breaks _framework requests, because its branch pipeline never
// reaches the endpoint that MapStaticAssets selected.
app.MapStaticAssets();

app.MapHealthChecks(ApiRoutes.Health);

// Feature endpoints are mapped here, one group per feature folder (see ADR-004).

app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Exposed so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
