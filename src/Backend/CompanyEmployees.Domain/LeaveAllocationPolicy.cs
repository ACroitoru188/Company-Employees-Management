using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

public static class LeaveAllocationPolicy
{
    public const int MaxAnnualCarryOverDays = 5;

    public static int DefaultDays(LeaveType type) => type switch
    {
        LeaveType.Annual => 21,
        LeaveType.Sick => 10,
        LeaveType.Parental => 10,
        LeaveType.Unpaid => 30,
        _ => 0
    };

    public static int AnnualCarryOverDays(int previousYearTotal, int previousYearUsed)
    {
        var unusedDays = Math.Max(0, previousYearTotal - previousYearUsed);
        return Math.Min(MaxAnnualCarryOverDays, unusedDays);
    }
}
