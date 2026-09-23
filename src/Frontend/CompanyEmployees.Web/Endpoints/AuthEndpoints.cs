using System.Security.Claims;
using CompanyEmployees.Application;
using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            HttpContext context,
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] Guid? regionId,
            [FromForm] string? returnUrl,
            [FromServices] SignInManager<User> signInManager,
            [FromServices] UserManager<User> userManager) =>
        {
            var account = await userManager.FindByNameAsync(email);
            var result = account == null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await signInManager.CheckPasswordSignInAsync(account, password, lockoutOnFailure: false);

            var regionMatches = result.Succeeded
                && regionId.HasValue
                && account!.RegionId == regionId.Value
                && await userManager.Users.AnyAsync(user =>
                    user.Id == account.Id && user.RegionId == regionId.Value && user.Region.IsActive);

            if (regionMatches)
            {
                // A login POST may be used to switch demo accounts. Explicitly remove the
                // previous principal before issuing the cookie for the selected account.
                await signInManager.SignOutAsync();
                await signInManager.SignInAsync(account!, isPersistent: true);

                // The saved preference follows the account, so signing in on another machine restores
                // the employee's language instead of whatever that browser last used.
                var culture = SupportedLanguages.Normalize(account!.PreferredCulture);
                context.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax
                    });

                var destination = await userManager.Users
                    .Where(u => u.NormalizedUserName == email.ToUpperInvariant())
                    .Select(u => new
                    {
                        Department = u.Department != null ? u.Department.Name : null,
                        u.Role
                    })
                    .FirstOrDefaultAsync();

                if (returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
                    return Results.Redirect(returnUrl);

                return Results.Redirect(HomeRouteResolver.Resolve(destination?.Role, destination?.Department));
            }
            else
            {
                return Results.Redirect("/?error=InvalidCredentials");
            }
        }).DisableAntiforgery();

        // Sign-out has to be a plain HTTP request too: an interactive circuit can't
        // touch the auth cookie (same reason the login form posts here).
        app.MapGet("/api/auth/logout", async (
            HttpContext context,
            ImpersonationContext impersonation,
            SignInManager<User> signInManager) =>
        {
            // Close any borrowed session first: a row left open here is indistinguishable from one
            // still in use, and used to block the next switch for good.
            var acting = ActingUser.Resolve(context.User);
            if (acting is not null)
                await impersonation.EndOpenSessionAsync(acting.RealUserId);

            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        });

        // A provider change requires a new HTTP scope so every repository receives a DbContext built
        // for the newly selected provider. It also clears the old database's identity cookie.
        app.MapGet("/api/auth/database-switched", async (SignInManager<User> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        }).RequireAuthorization();

        // Borrowing an account swaps the auth cookie, so it has to happen over a plain request
        // like login does. The rules live in ImpersonationContext; this only maps them to a
        // redirect. Both endpoints require an already-signed-in user.
        app.MapPost("/api/auth/impersonate", async (
            HttpContext context,
            [FromForm] Guid delegationId,
            ImpersonationContext impersonation,
            SignInManager<User> signInManager,
            ILoggerFactory loggerFactory) =>
        {
            var acting = ActingUser.Resolve(context.User);
            if (acting is null)
                return Results.Redirect("/");

            // No chaining, decided from the cookie rather than from an open session row: the row
            // survives a sign-out or an expired cookie, the claim does not.
            if (acting.IsImpersonating)
                return Results.Redirect("/employee/dashboard?error=DelegationUnavailable");

            try
            {
                var target = await impersonation.StartAsync(
                    acting.RealUserId, delegationId, context.Connection.RemoteIpAddress?.ToString());

                await signInManager.SignInWithClaimsAsync(target, isPersistent: true, new[]
                {
                    new Claim(ImpersonationClaims.RealUserId, acting.RealUserId.ToString("D")),
                    new Claim(ImpersonationClaims.RealUserName, acting.RealUserName),
                    new Claim(ImpersonationClaims.DelegationId, delegationId.ToString("D"))
                });

                // Same landing rule as login: /manager/team bounces anyone who is not a
                // LineManager, and admins are delegated from too.
                return Results.Redirect(HomeRouteResolver.Resolve(target.Role, target.Department?.Name));
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("AuthEndpoints");
                logger.LogWarning(ex, "Impersonation refused for user {RealUserId}.", acting.RealUserId);
                return Results.Redirect("/employee/dashboard?error=DelegationUnavailable");
            }
        }).DisableAntiforgery();

        // Unlike a delegation, an admin preview is inspection-only. It swaps the claims to the selected
        // account so page guards, navigation and read queries all show that account's perspective; the
        // explicit marker lets the layout make the application surface inert.
        app.MapPost("/api/auth/preview/start", async (
            HttpContext context,
            [FromForm] Guid userId,
            UserManager<User> userManager,
            SignInManager<User> signInManager) =>
        {
            var acting = ActingUser.Resolve(context.User);
            if (acting is null || acting.IsImpersonating)
                return Results.Redirect("/employee/dashboard?error=PreviewUnavailable");

            var realUser = await userManager.FindByIdAsync(acting.RealUserId.ToString());
            var target = await userManager.Users
                .Include(user => user.Department)
                .FirstOrDefaultAsync(user => user.Id == userId);
            if (realUser?.Role != UserRole.Admin || target is null || target.Id == realUser.Id || target.Status != UserStatus.Active)
                return Results.Redirect("/admin/users?error=PreviewUnavailable");

            await signInManager.SignInWithClaimsAsync(target, isPersistent: true, new[]
            {
                new Claim(ImpersonationClaims.RealUserId, realUser.Id.ToString("D")),
                new Claim(ImpersonationClaims.RealUserName, realUser.Name),
                new Claim(ImpersonationClaims.ReadOnlyPreview, "true"),
                new Claim(ImpersonationClaims.PreviewUserId, target.Id.ToString("D")),
                new Claim(ImpersonationClaims.PreviewUserName, target.Name)
            });

            return Results.Redirect(HomeRouteResolver.Resolve(target.Role, target.Department?.Name));
        }).RequireAuthorization().DisableAntiforgery();

        // GET so the banner's exit can be a plain link, like logout: a forced request only ever
        // returns someone to their own account, so there is nothing here worth a CSRF token.
        app.MapGet("/api/auth/impersonate/stop", async (
            HttpContext context,
            ImpersonationContext impersonation,
            SignInManager<User> signInManager) =>
        {
            var acting = ActingUser.Resolve(context.User);
            if (acting is null)
                return Results.Redirect("/");

            var realUser = await impersonation.StopAsync(acting.RealUserId);
            await signInManager.SignInAsync(realUser, isPersistent: true);

            return Results.Redirect("/employee/dashboard");
        });

        return app;
    }
}
