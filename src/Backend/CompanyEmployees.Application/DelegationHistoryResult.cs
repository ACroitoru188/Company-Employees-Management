using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Application
{
    public enum DelegationHistoryScope
    {
        // Things other people did while covering for this account.
        DoneInMyName,

        // Things this person did while covering for someone else.
        DoneByMe
    }

    // Flattened for the page, like the dashboard results: the Web layer gets names, not
    // entities with navigations it would have to know how to walk.
    public class DelegationHistoryResult
    {
        public List<DelegationHistoryEntry> Items { get; set; } = new();
        public int Total { get; set; }
    }

    public class DelegationHistoryEntry
    {
        public DateTime When { get; set; }
        public string RealUserName { get; set; } = "";
        public string ActedAsName { get; set; } = "";
        public string TargetName { get; set; } = "";
        public DelegatedActionType ActionType { get; set; }
        public string? Details { get; set; }
    }
}
