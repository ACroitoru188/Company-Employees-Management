using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

public static class LeaveAllocationPolicy
{
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
        return Math.Max(0, previousYearTotal - previousYearUsed);
    }

    // Annual entitlement starts on January 1. Carried days therefore reach their
    // 18-month lifetime at the end of June in the following balance year.
    public static DateOnly AnnualCarryOverExpiryDate(int balanceYear) =>
        new(balanceYear, 6, 30);

    public static int ExpiredAnnualCarryOverDays(
        int carriedOverDays,
        int carriedOverDaysUsed,
        int balanceYear,
        DateOnly asOf)
    {
        if (asOf <= AnnualCarryOverExpiryDate(balanceYear))
            return 0;

        return Math.Max(0, carriedOverDays - carriedOverDaysUsed);
    }
}
