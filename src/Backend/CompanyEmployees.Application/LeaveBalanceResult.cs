using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Application
{
    public sealed record AnnualCarryOverPortionResult(
        int Days,
        DateOnly ExpiryDate,
        int ExpiredDays);

    // What EmployeeContext computes for one leave type: granted vs consumed days.
    public class LeaveBalanceResult
    {
        public LeaveType Type { get; set; }
        public int DaysTotal { get; set; }
        public int DaysUsed { get; set; }
        public List<AnnualCarryOverPortionResult> CarryOverPortions { get; set; } = [];
        public int CarriedOverDays => CarryOverPortions.Sum(portion => portion.Days);
        public int ExpiredCarriedOverDays => CarryOverPortions.Sum(portion => portion.ExpiredDays);
        public int DaysRemaining => Math.Max(0, DaysTotal - DaysUsed - ExpiredCarriedOverDays);
    }
}
