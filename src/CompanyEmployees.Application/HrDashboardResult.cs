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
        public List<HrDepartmentCount> Departments { get; set; } = new();

        // Pending requests that have been waiting longer than a week.
        public int StaleRequests { get; set; }
    }

    public class HrPendingRequest
    {
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Type { get; set; } = "";
        public int Days { get; set; }
        public int WaitingDays { get; set; }
    }

    public class HrDepartmentCount
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
