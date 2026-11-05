using Microsoft.AspNetCore.HttpOverrides;

namespace Budget.Server.Hosting;

/// <summary>
/// Trusts the X-Forwarded-* headers from a proxy in front of the app, such as a dev tunnel used to
/// try the app on a phone. Without this the app thinks it's on http://localhost, so the Microsoft
/// sign-in redirect URI and the secure session cookie are wrong.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddForwardedHeadersFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // The tunnel connects from localhost, which is trusted by default. Hosts are limited to
            // the tunnel domains so a request can't claim to be any site it likes.
            var allowedHosts = section.GetSection("AllowedHosts").Get<string[]>() ?? [];
            foreach (var host in allowedHosts)
            {
                options.AllowedHosts.Add(host);
            }
        });

        return services;
    }

    public static WebApplication UseForwardedHeadersIfEnabled(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Configuration.GetValue<bool>($"{SectionName}:Enabled"))
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
