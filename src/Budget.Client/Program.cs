using Budget.Client;
using Budget.Client.Account;
using Budget.Client.Accounts;
using Budget.Client.Activity;
using Budget.Client.Budgeting;
using Budget.Client.CashFlow;
using Budget.Client.Households;
using Budget.Client.Income;
using Budget.Client.Layout;
using Budget.Client.Outgoings;
using Budget.Client.People;
using Budget.Client.Transfers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API is served from the same origin as the client (see ADR-001), so the session cookie
// is sent automatically.
builder.Services.AddScoped(_ => new HttpClient(new CsrfHeaderHandler { InnerHandler = new ServerErrorHandler { InnerHandler = new HttpClientHandler() } })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<IAccountApi, AccountApi>();
builder.Services.AddScoped<UserSession>();
builder.Services.AddScoped<IUserSettingsApi, UserSettingsApi>();
builder.Services.AddScoped<IAccountDeletionApi, AccountDeletionApi>();
builder.Services.AddScoped<UserSettingsStore>();
builder.Services.AddScoped<IHouseholdApi, HouseholdApi>();
builder.Services.AddScoped<IIncomeApi, IncomeApi>();
builder.Services.AddScoped<IPeopleApi, PeopleApi>();
builder.Services.AddScoped<ICategoryApi, CategoryApi>();
builder.Services.AddScoped<IOutgoingApi, OutgoingApi>();
builder.Services.AddScoped<IUpcomingPaymentsApi, UpcomingPaymentsApi>();
builder.Services.AddScoped<IAccountsApi, AccountsApi>();
builder.Services.AddScoped<ITransferApi, TransferApi>();
builder.Services.AddScoped<ICashFlowApi, CashFlowApi>();
builder.Services.AddScoped<IInvitationApi, InvitationApi>();
builder.Services.AddScoped<ISharing, Sharing>();
builder.Services.AddScoped<IRenewalApi, RenewalApi>();
builder.Services.AddScoped<IActivityApi, ActivityApi>();
builder.Services.AddScoped<IBudgetApi, BudgetApi>();
builder.Services.AddScoped<Budget.Client.DataExport.IImportApi, Budget.Client.DataExport.ImportApi>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ThemePreference>();
builder.Services.AddScoped<Connectivity>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, BffAuthenticationStateProvider>();

builder.Services.AddMudServices();

await builder.Build().RunAsync();
