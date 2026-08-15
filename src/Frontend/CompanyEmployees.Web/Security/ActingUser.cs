using System.Security.Claims;

namespace CompanyEmployees.Web.Security
{
    // Who is acting, and as whom. Resolve is the only place that reads the impersonation
    // claims — everything else asks this type, so "who is really acting" cannot end up
    // answered differently in two places.
    public sealed record ActingUser(
        Guid EffectiveUserId,
        string EffectiveUserName,
        Guid RealUserId,
        string RealUserName,
        Guid? DelegationId)
    {
        public bool IsImpersonating => EffectiveUserId != RealUserId;

        // Null when nobody is signed in, or the principal carries no usable id.
        public static ActingUser? Resolve(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var effectiveId))
                return null;

            var effectiveName = principal.FindFirst("FullName")?.Value ?? "";

            // Present only while an account is borrowed; absent means acting as yourself.
            var hasReal = Guid.TryParse(
                principal.FindFirst(ImpersonationClaims.RealUserId)?.Value, out var realId);

            Guid? delegationId = Guid.TryParse(
                principal.FindFirst(ImpersonationClaims.DelegationId)?.Value, out var parsed)
                ? parsed
                : null;

            return new ActingUser(
                effectiveId,
                effectiveName,
                hasReal ? realId : effectiveId,
                hasReal ? principal.FindFirst(ImpersonationClaims.RealUserName)?.Value ?? "" : effectiveName,
                hasReal ? delegationId : null);
        }
    }
}
