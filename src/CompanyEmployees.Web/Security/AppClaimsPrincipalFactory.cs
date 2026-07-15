using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace CompanyEmployees.Web.Security
{
    // Identity's default factory emits only id/username/security-stamp claims.
    // Our role lives on the User.Role enum (we don't use Identity's role tables),
    // so it must be added to the cookie here for [Authorize(Roles = ...)] and
    // <AuthorizeView Roles="..."> to see it. Runs once per sign-in — a role change
    // takes effect at the next login.
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<User>
    {
        public AppClaimsPrincipalFactory(
            UserManager<User> userManager,
            IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, optionsAccessor)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            identity.AddClaim(new Claim("FullName", user.Name));
            return identity;
        }
    }
}
