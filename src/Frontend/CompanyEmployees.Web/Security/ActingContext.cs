using Microsoft.AspNetCore.Components.Authorization;

namespace CompanyEmployees.Web.Security
{
    // The circuit-side way to reach ActingUser. Minimal-API endpoints resolve the same type
    // straight from HttpContext.User instead — there is no AuthenticationStateProvider there.
    public class ActingContext
    {
        private readonly AuthenticationStateProvider _authStateProvider;

        public ActingContext(AuthenticationStateProvider authStateProvider)
        {
            _authStateProvider = authStateProvider;
        }

        public async Task<ActingUser?> GetAsync()
        {
            var state = await _authStateProvider.GetAuthenticationStateAsync();
            return ActingUser.Resolve(state.User);
        }
    }
}
