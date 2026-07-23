using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CompanyEmployees.Web.Security;

// Pages using InteractiveServer render mode with prerendering run OnInitializedAsync twice:
// once during the static prerender pass, once again once the circuit connects. NavigateTo
// throws NavigationException during that first pass, which the app's root <ErrorBoundary>
// (App.razor) would otherwise catch and show as a raw error screen instead of a redirect.
// Waiting for `isInteractive` sidesteps that and skips a redundant prerender DB round-trip.
public static class PageGuard
{
    public static async Task<bool> IsAuthenticatedAsync(
        Task<AuthenticationState>? authStateTask, NavigationManager nav, bool isInteractive)
    {
        if (!isInteractive)
            return false;

        if (authStateTask is null || (await authStateTask).User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Uri.EscapeDataString(nav.ToBaseRelativePath(nav.Uri));
            nav.NavigateTo($"/?returnUrl={returnUrl}");
            return false;
        }

        return true;
    }
}
