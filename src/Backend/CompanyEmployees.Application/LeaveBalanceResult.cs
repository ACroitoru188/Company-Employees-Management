using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Application
{
    // What EmployeeContext computes for one leave type: granted vs consumed days.
    public class LeaveBalanceResult
    {
        public LeaveType Type { get; set; }
        public int DaysTotal { get; set; }
        public int DaysUsed { get; set; }
        public int CarriedOverDays { get; set; }
        public int ExpiredCarriedOverDays { get; set; }
        public DateOnly? CarryOverExpiryDate { get; set; }
        public int DaysRemaining => Math.Max(0, DaysTotal - DaysUsed - ExpiredCarriedOverDays);
    }
}
