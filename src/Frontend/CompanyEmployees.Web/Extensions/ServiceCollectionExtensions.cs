using System.Globalization;
using System.IO;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Persistence;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace CompanyEmployees.Web.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Persists Data Protection keys to a configurable directory so they survive restarts
    /// in both development and containerised production deployments.
    /// Dev  -> DataProtection:KeysPath is a relative path (e.g. ../../../.tmp/...)
    /// Prod -> DataProtection:KeysPath is an absolute path mounted via a Docker volume (e.g. /app/dp-keys)
    /// </summary>
    public static IServiceCollection AddAppDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredPath = configuration["DataProtection:KeysPath"];
        string keyPath;
        if (!string.IsNullOrEmpty(configuredPath))
        {
            keyPath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        }
        else
        {
            keyPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../../.tmp/data-protection-keys"));
        }

        Directory.CreateDirectory(keyPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName("CompanyEmployees");

        return services;
    }

    /// <summary>
    /// Configures ASP.NET Core Identity with entity framework stores, claims principal factory,
    /// and application auth cookies keyed by the active database provider.
    /// </summary>
    public static IServiceCollection AddAppIdentity(
        this IServiceCollection services,
        DatabaseRuntimeState databaseState)
    {
        services.AddIdentity<User, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CompanyEmployeesDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(24);
        });

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = $"CompanyEmployees.Auth.{databaseState.ActiveProviderId}";
            options.LoginPath = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(5);
            options.SlidingExpiration = true;
        });

        return services;
    }

    /// <summary>
    /// Configures cookie-based request localization using the supported languages dictionary.
    /// </summary>
    public static IApplicationBuilder UseAppLocalization(this IApplicationBuilder app)
    {
        var supportedCultures = SupportedLanguages.All
            .Select(language => new CultureInfo(language.Culture))
            .ToArray();

        return app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedLanguages.DefaultCulture),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures,
            RequestCultureProviders =
            [
                new CookieRequestCultureProvider()
            ]
        });
    }

    /// <summary>
    /// Configures Serilog request logging with verbose noise filtering for static assets, blazor negotiate, and health checks.
    /// </summary>
    public static IApplicationBuilder UseAppSerilogRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, ex) =>
            {
                if (ex != null || httpContext.Response.StatusCode >= 500)
                    return LogEventLevel.Error;

                var path = httpContext.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/_content")
                    || path.StartsWith("/_framework")
                    || path.StartsWith("/_blazor/negotiate")
                    || path.StartsWith("/js")
                    || path.StartsWith("/css")
                    || path.StartsWith("/media")
                    || path.EndsWith(".js")
                    || path.EndsWith(".css")
                    || path.EndsWith(".mp4")
                    || path.EndsWith(".png")
                    || path.EndsWith(".svg")
                    || path.EndsWith(".ico")
                    || path.StartsWith("/api/health"))
                {
                    return LogEventLevel.Verbose;
                }

                return LogEventLevel.Information;
            };
        });
    }
}
