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
        Guid? DelegationId,
        bool IsReadOnlyPreview)
    {
        public bool IsImpersonating => EffectiveUserId != RealUserId;

        // Null when nobody is signed in, or the principal carries no usable id.
        public static ActingUser? Resolve(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var identityId))
                return null;

            var isReadOnlyPreview = string.Equals(
                principal.FindFirst(ImpersonationClaims.ReadOnlyPreview)?.Value,
                "true", StringComparison.OrdinalIgnoreCase);

            // PreviewUserId is deliberately authoritative for a preview. This avoids a stale
            // circuit principal resolving "my team" and other account data against the admin
            // who opened the preview rather than the person selected for inspection.
            var previewUserId = Guid.Empty;
            var hasPreviewTarget = isReadOnlyPreview
                && Guid.TryParse(principal.FindFirst(ImpersonationClaims.PreviewUserId)?.Value,
                    out previewUserId);
            var effectiveId = hasPreviewTarget ? previewUserId : identityId;
            var effectiveName = hasPreviewTarget
                ? principal.FindFirst(ImpersonationClaims.PreviewUserName)?.Value
                    ?? principal.FindFirst("FullName")?.Value
                    ?? ""
                : principal.FindFirst("FullName")?.Value ?? "";

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
                hasReal ? delegationId : null,
                isReadOnlyPreview);
        }
    }
}
