namespace CompanyEmployees.Web.Security
{
    // Present on the cookie only while an account is being borrowed. Nothing outside
    // ActingContext should read these directly — see the note there.
    public static class ImpersonationClaims
    {
        public const string RealUserId = "RealUserId";
        public const string RealUserName = "RealUserName";
        public const string DelegationId = "DelegationId";
        // This claim marks an admin's inspection session. It intentionally has no delegation:
        // it may show any active account, but the UI must never enable that account's actions.
        public const string ReadOnlyPreview = "ReadOnlyPreview";
        // Kept separately from Identity's NameIdentifier. A preview must always read from the
        // selected account even while a long-lived Blazor circuit still carries the original
        // authentication identity.
        public const string PreviewUserId = "PreviewUserId";
        public const string PreviewUserName = "PreviewUserName";
    }
}
