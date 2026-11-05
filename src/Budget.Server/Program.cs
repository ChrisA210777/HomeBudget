using Budget.Infrastructure;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Account;
using Budget.Server.Features.Households;
using Budget.Server.Features.Income;
using Budget.Server.Features.People;
using Budget.Server.Identity;
using Budget.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddInfrastructure();
builder.Services.AddBudgetAuthentication(builder.Configuration);
builder.Services.AddHouseholdAuthorization();
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

app.UseAuthentication();
app.UseAuthorization();
app.UseCsrfHeaderCheck();

// Serve the Blazor WebAssembly client (see ADR-001). MapStaticAssets serves the client's
// wwwroot and its _framework files (fingerprinted and compressed), so UseBlazorFrameworkFiles
// is not needed. Adding it breaks _framework requests, because its branch pipeline never
// reaches the endpoint that MapStaticAssets selected.
app.MapStaticAssets();

app.MapHealthChecks(ApiRoutes.Health);

// Feature endpoints, one group per feature folder (see ADR-004).
app.MapAccountEndpoints();
app.MapHouseholdEndpoints();
app.MapIncomeStreamEndpoints();
app.MapPeopleEndpoints();

// Unknown API routes are 404s, not the client app's page.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/bff/{**path}", () => Results.NotFound());

// Any other GET is a client-side route: serve the Blazor app and let its router handle it.
app.MapFallbackToFile("index.html")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));

await app.RunAsync();

/// <summary>Exposed so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
