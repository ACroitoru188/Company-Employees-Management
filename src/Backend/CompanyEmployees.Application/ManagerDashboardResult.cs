using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Application
{
    // Everything the manager dashboard shows, gathered in one call so the page
    // doesn't fire several queries at the same scoped DbContext.
    public class ManagerDashboardResult
    {
        public int TeamSize { get; set; }
        public int PendingRequests { get; set; }

        // Distinct people out today, not requests: someone with two overlapping
        // approved requests is still one person away.
        public int OnLeaveToday { get; set; }

        // Pending requests that have been waiting longer than a week.
        public int StaleRequests { get; set; }

        public List<ManagerPendingRequest> Pending { get; set; } = new();
        public List<ManagerTeamMember> Team { get; set; } = new();
        public List<ManagerDelegation> ActiveDelegationsGiven { get; set; } = new();
        public List<ManagerDelegation> ActiveDelegationsReceived { get; set; } = new();
    }

    public class ManagerPendingRequest
    {
        // The approve/decline flow needs the id; HrPendingRequest has no equivalent,
        // which is why the HR dashboard can only list its rows and not act on them.
        public Guid RequestId { get; set; }

        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Type { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int Days { get; set; }
        public int WaitingDays { get; set; }

        public bool IsDelegated { get; set; }
        public string? DelegatedFromManagerName { get; set; }
    }

    public class ManagerTeamMember
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string Department { get; set; } = "";
        public bool OnLeaveToday { get; set; }

        // Contract details
        public Guid? ContractId { get; set; }
        public ContractType? ContractType { get; set; }
        public ContractStatus? ContractStatus { get; set; }
        public DateOnly? ContractStartDate { get; set; }
        public DateOnly? ContractEndDate { get; set; }
    }
}
