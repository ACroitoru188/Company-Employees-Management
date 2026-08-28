namespace CompanyEmployees.Web.Setup;

/// <summary>
/// Controls access based on database setup completion:
/// - When setup is incomplete: redirects all page requests to /setup.
/// - When setup is complete: redirects /setup requests back to the application.
/// </summary>
public sealed class SetupMiddleware(RequestDelegate next, ISetupStateStore store)
{
    private static readonly HashSet<string> StaticExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".png", ".jpg", ".jpeg", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".json", ".map", ".wasm", ".mp4"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Static files and framework internal endpoints always pass through
        if (IsStaticOrFrameworkAsset(path))
        {
            await next(context);
            return;
        }

        var state = store.Load();

        if (!state.IsComplete)
        {
            // Setup is incomplete: allow /setup, redirect everything else to /setup
            if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            context.Response.Redirect("/setup");
            return;
        }
        else
        {
            // Setup is complete: if user navigates to /setup, redirect to /
            if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/");
                return;
            }

            await next(context);
        }
    }

    private static bool IsStaticOrFrameworkAsset(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && StaticExtensions.Contains(ext);
    }
}
