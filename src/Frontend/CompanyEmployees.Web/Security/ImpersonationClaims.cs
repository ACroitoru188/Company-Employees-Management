namespace CompanyEmployees.Web.Security
{
    // Present on the cookie only while an account is being borrowed. Nothing outside
    // ActingContext should read these directly — see the note there.
    public static class ImpersonationClaims
    {
        public const string RealUserId = "RealUserId";
        public const string RealUserName = "RealUserName";
        public const string DelegationId = "DelegationId";
    }
}
