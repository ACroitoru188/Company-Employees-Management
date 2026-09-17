namespace CompanyEmployees.Application
{
    // Everything the HR dashboard shows, gathered in one call so the page
    // doesn't fire several queries at the same scoped DbContext.
    public class HrDashboardResult
    {
        public int ActiveEmployees { get; set; }
        public int NewEmployees { get; set; }      // joined in the last 30 days
        public int PendingRequests { get; set; }
        public int OnLeaveToday { get; set; }

        public List<HrPendingRequest> Pending { get; set; } = new();

        // Approved leave whose owner has asked HR to undo it, oldest request first.
        public List<HrCancellationRequest> CancellationRequests { get; set; } = new();

        public List<HrDepartmentCount> Departments { get; set; } = new();

        // Pending requests that have been waiting longer than a week.
        public int StaleRequests { get; set; }
    }

    public class HrPendingRequest
    {
        public Guid RequestId { get; set; }
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Type { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int Days { get; set; }
        public int WaitingDays { get; set; }

        public string Role { get; set; } = "";
        public string? Reason { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

    // Deliberately not HrPendingRequest: this one is approved leave the employee wants undone,
    // so it carries their justification and how long HR has been sitting on it instead of the
    // original submission's waiting time.
    public class HrCancellationRequest
    {
        public Guid RequestId { get; set; }
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Type { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int Days { get; set; }

        public string Role { get; set; } = "";

        // The reason given on the original leave request.
        public string? Reason { get; set; }

        // Why the employee wants it undone — required when they ask, so never empty here.
        public string? CancellationReason { get; set; }

        public DateTime RequestedAt { get; set; }

        // True once the period has begun: part of the leave is already taken, so approving
        // returns days the employee has in fact used.
        public bool InProgress { get; set; }
    }

    public class HrDepartmentCount
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
