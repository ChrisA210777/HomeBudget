using Budget.Infrastructure;
using Budget.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

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

// Serve the Blazor WebAssembly client (see ADR-001).
app.UseBlazorFrameworkFiles();
app.MapStaticAssets();

app.MapHealthChecks(ApiRoutes.Health);

// Feature endpoints are mapped here, one group per feature folder (see ADR-004).

app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Exposed so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
