using Azure.Monitor.OpenTelemetry.AspNetCore;
using Budget.Infrastructure;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.SignIn;
using Budget.Server.Features.Accounts;
using Budget.Server.Features.Activity;
using Budget.Server.Features.Budgeting;
using Budget.Server.Features.DataExport;
using Budget.Server.Features.Households;
using Budget.Server.Features.Income;
using Budget.Server.Features.Outgoings;
using Budget.Server.Features.CashFlow;
using Budget.Server.Features.People;
using Budget.Server.Features.Transfers;
using Budget.Server.Hosting;
using Budget.Server.Identity;
using Budget.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<ExportBuilder>();
builder.Services.AddHostedService<ActivityCleanup>();
builder.Services.AddInfrastructure();
builder.Services.AddBudgetAuthentication(builder.Configuration);
builder.Services.AddHouseholdAuthorization();
builder.Services.AddProblemDetailsWithCorrelationId();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [DatabaseHealthCheck.ReadyTag]);
builder.Services.AddForwardedHeadersFromConfiguration(builder.Configuration);
builder.Services.AddBudgetRateLimits();

// Requests, dependencies, logs and exceptions go to Application Insights in Azure (BUD-12).
// The connection string is only set there, so nothing is sent when running locally or in tests.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

// First, so everything after sees the original scheme and host (e.g. a dev tunnel's https URL).
app.UseForwardedHeadersIfEnabled();
app.UseCorrelationIdHeader();
app.UseSecurityHeaders(app.Environment);

// In Development (and the end-to-end Testing environment) the database is created and migrated on startup. Deployed environments
// apply migrations from the pipeline using the migration bundle instead (see ADR-003).
if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment(TestSignInEndpoint.EnvironmentName))
    && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
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

// After authentication, so limits are per person; before authorisation, so signed-out requests count too (BUD-144).
app.UseRateLimiter();
app.UseRequestSizeLimits();
app.UseAuthorization();
app.UseCsrfHeaderCheck();

// Serve the Blazor WebAssembly client (see ADR-001). MapStaticAssets serves the client's
// wwwroot and its _framework files (fingerprinted and compressed), so UseBlazorFrameworkFiles
// is not needed. Adding it breaks _framework requests, because its branch pipeline never
// reaches the endpoint that MapStaticAssets selected.
app.MapStaticAssets();

app.MapHealthChecks(ApiRoutes.Health, DatabaseHealthCheck.Liveness);
app.MapHealthChecks(ApiRoutes.Ready, DatabaseHealthCheck.Readiness);

// Feature endpoints, one group per feature folder (see ADR-004).
app.MapAccountEndpoints();
if (TestSignInEndpoint.IsEnabled(app.Environment, app.Configuration))
{
    app.MapTestSignIn();
}
app.MapHouseholdEndpoints();
app.MapIncomeStreamEndpoints();
app.MapPeopleEndpoints();
app.MapOutgoingCategoryEndpoints();
app.MapOutgoingEndpoints();
app.MapTransferEndpoints();
app.MapCashFlowEndpoints();
app.MapInvitationEndpoints();
app.MapHouseholdAccountEndpoints();
app.MapBudgetEndpoints();
app.MapDataExportEndpoints();
app.MapActivityEndpoints();

// Unknown server routes are 404s, not the client app's page.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/bff/{**path}", () => Results.NotFound());
app.MapFallback("/account/{**path}", () => Results.NotFound());

// Any other GET is a client-side route: serve the Blazor app and let its router handle it.
app.MapFallbackToFile("index.html")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));

await app.RunAsync();

/// <summary>Exposed so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
