using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.People;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Income;

/// <summary>A bUnit context for the income pages, on 5 Nov 2026.</summary>
public abstract class IncomeTestContext : IDisposable
{
    protected IncomeTestContext()
    {
        Context.Services.AddMudServices();
        Context.Services.AddSingleton<IIncomeApi>(Api);
        Context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        Context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        Context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Admin"));
        Context.Services.AddScoped<UserSession>();
        Context.Services.AddSingleton<IUserSettingsApi>(Settings);
        Context.Services.AddScoped<UserSettingsStore>();
        Context.Services.AddSingleton<Budget.Client.Accounts.IAccountsApi>(new StaticAccountsApi());
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
        Context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    protected TestContext Context { get; } = new();

    protected FakeIncomeApi Api { get; } = new();

    protected FakeUserSettingsApi Settings { get; } = new();

    /// <summary>Sign in with a different role; call before rendering.</summary>
    protected void UseRole(string role) => Context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role));

    public void Dispose()
    {
        Context.Dispose();
        GC.SuppressFinalize(this);
    }
}
