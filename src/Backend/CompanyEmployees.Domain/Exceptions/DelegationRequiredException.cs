namespace CompanyEmployees.Domain.Exceptions
{
    // Its own type so the calendar can offer to create the delegation instead of only
    // showing the message. The rule itself stays in EmployeeContext — the UI reacts to it
    // rather than re-implementing the check.
    public class DelegationRequiredException : Exception
    {
        public DelegationRequiredException(string message) : base(message)
        {
        }
    }
}
