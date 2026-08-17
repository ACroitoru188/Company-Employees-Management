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

    public static int AnnualCarryOverDays(int previousYearTotal, int previousYearUsed) =>
        Math.Max(0, previousYearTotal - previousYearUsed);

    // Carry-over becomes available on January 1 of carryStartYear and remains
    // available for 18 months, through June 30 of the following year.
    public static DateOnly AnnualCarryOverExpiryDate(int carryStartYear) =>
        new(carryStartYear + 1, 6, 30);

    public static int ExpiredAnnualCarryOverDays(
        int carriedOverDays,
        int carriedOverDaysUsed,
        int carryStartYear,
        DateOnly asOf)
    {
        if (asOf <= AnnualCarryOverExpiryDate(carryStartYear))
            return 0;

        return Math.Max(0, carriedOverDays - carriedOverDaysUsed);
    }
}
