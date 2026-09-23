using System.Security.Claims;
using CompanyEmployees.Web.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CompanyEmployees.Web.Tests;

// Every gated page calls this first, so its three outcomes decide whether anything renders at
// all. It is a plain static function precisely so it can be checked without a renderer.
public class PageGuardTests
{
    [Fact]
    public async Task Does_nothing_at_all_while_the_page_is_still_prerendering()
    {
        // NavigateTo throws NavigationException during the prerender pass, which the root
        // ErrorBoundary would show as a raw error screen instead of a redirect.
        var nav = new RecordingNavigationManager();

        var allowed = await PageGuard.IsAuthenticatedAsync(
            AuthState(authenticated: true), nav, isInteractive: false);

        Assert.False(allowed);
        Assert.Null(nav.LastNavigatedTo);
    }

    [Fact]
    public async Task Lets_an_authenticated_user_through_once_interactive()
    {
        var nav = new RecordingNavigationManager();

        var allowed = await PageGuard.IsAuthenticatedAsync(
            AuthState(authenticated: true), nav, isInteractive: true);

        Assert.True(allowed);
        Assert.Null(nav.LastNavigatedTo);
    }

    [Fact]
    public async Task Sends_an_anonymous_visitor_to_login_carrying_where_they_wanted_to_go()
    {
        var nav = new RecordingNavigationManager("http://localhost/employee/my-requests");

        var allowed = await PageGuard.IsAuthenticatedAsync(
            AuthState(authenticated: false), nav, isInteractive: true);

        Assert.False(allowed);
        Assert.Equal("/?returnUrl=employee%2Fmy-requests", nav.LastNavigatedTo);
    }

    [Fact]
    public async Task Treats_a_missing_auth_state_as_anonymous()
    {
        var nav = new RecordingNavigationManager();

        var allowed = await PageGuard.IsAuthenticatedAsync(null, nav, isInteractive: true);

        Assert.False(allowed);
        Assert.NotNull(nav.LastNavigatedTo);
    }

    private static Task<AuthenticationState> AuthState(bool authenticated)
    {
        // An identity with no authentication type is anonymous to ClaimsIdentity, which is
        // exactly how the real cookie-less case arrives.
        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ion@siemens.com") }, "TestAuth")
            : new ClaimsIdentity();

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager(string uri = "http://localhost/employee/dashboard") =>
            Initialize("http://localhost/", uri);

        public string? LastNavigatedTo { get; private set; }

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            LastNavigatedTo = uri;
    }
}
