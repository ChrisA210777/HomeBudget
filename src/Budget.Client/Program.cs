using Budget.Client;
using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.People;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API is served from the same origin as the client (see ADR-001), so the session cookie
// is sent automatically.
builder.Services.AddScoped(_ => new HttpClient(new CsrfHeaderHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<IAccountApi, AccountApi>();
builder.Services.AddScoped<UserSession>();
builder.Services.AddScoped<IIncomeApi, IncomeApi>();
builder.Services.AddScoped<IPeopleApi, PeopleApi>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, BffAuthenticationStateProvider>();

builder.Services.AddMudServices();

await builder.Build().RunAsync();
