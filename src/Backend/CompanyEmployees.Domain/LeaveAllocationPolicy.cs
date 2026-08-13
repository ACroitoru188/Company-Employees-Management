using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

public static class LeaveAllocationPolicy
{
    public const int BaseAnnualDays = 21;

    public static int DefaultDays(LeaveType type) => type switch
    {
        LeaveType.Annual => BaseAnnualDays,
        LeaveType.Sick => 10,
        LeaveType.Parental => 10,
        LeaveType.Unpaid => 30,
        _ => 0
    };

    public static int AnnualDaysForRegion(
        string? regionCode,
        DateOnly? companyStartDate,
        int entitlementYear)
    {
        if (!string.Equals(regionCode, "RO", StringComparison.OrdinalIgnoreCase)
            || companyStartDate is null)
            return BaseAnnualDays;

        var yearsOfService = CompletedYearsOfService(
            companyStartDate.Value,
            new DateOnly(entitlementYear, 1, 1));

        return yearsOfService switch
        {
            >= 15 => BaseAnnualDays + 3,
            >= 10 => BaseAnnualDays + 2,
            >= 5 => BaseAnnualDays + 1,
            _ => BaseAnnualDays
        };
    }

    private static int CompletedYearsOfService(DateOnly startDate, DateOnly asOf)
    {
        if (startDate > asOf)
            return 0;

        var years = asOf.Year - startDate.Year;
        if (startDate.AddYears(years) > asOf)
            years--;

        return years;
    }

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
