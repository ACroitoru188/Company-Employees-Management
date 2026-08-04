using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

            // HR is a department, not a UserRole, so the role claim alone can't tell
            // HR staff apart. UserManager loads the user without its navigations, so
            // the department name needs its own lookup (sign-in only, not per request).
            var department = await UserManager.Users
                .Where(u => u.Id == user.Id)
                .Select(u => u.Department != null ? u.Department.Name : null)
                .FirstOrDefaultAsync();
            if (department != null)
                identity.AddClaim(new Claim("Department", department));

            return identity;
        }
    }
}
