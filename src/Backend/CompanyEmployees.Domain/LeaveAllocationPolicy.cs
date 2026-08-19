using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

public static class LeaveAllocationPolicy
{
    // The company never grants fewer than four working weeks, even where the
    // local statutory minimum is lower or no paid vacation is mandated.
    public const int GlobalCompanyAnnualDays = 20;

    // Kept as the safe fallback for missing/unknown region codes and for the
    // legacy allocation records. Known regions are resolved below.
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
        var code = regionCode?.Trim().ToUpperInvariant();
        var yearsOfService = companyStartDate is { } startDate
            ? CompletedYearsOfService(startDate, new DateOnly(entitlementYear, 1, 1))
            : 0;

        return code switch
        {
            // Company/collectively agreed defaults above the statutory floor.
            "DE" => 30,
            "RO" => RomanianAnnualDays(yearsOfService),

            // Five working weeks.
            "AT" => yearsOfService >= 25 ? 30 : 25,
            "DK" or "FR" or "SE" => 25,
            "FI" => yearsOfService >= 1 ? 25 : GlobalCompanyAnnualDays,

            // Calendar-day entitlements converted to the working-day model
            // used by this application, or local rules above four weeks.
            "BR" or "PT" or "ES" or "AE" => 22,
            "NO" => 21,

            // Service-based local rules that eventually exceed the company floor.
            "MX" => MexicanAnnualDays(yearsOfService),
            "PL" => yearsOfService >= 10 ? 26 : GlobalCompanyAnnualDays,
            "TR" => yearsOfService >= 15 ? 26 : GlobalCompanyAnnualDays,

            // All other seeded countries receive the four-week company floor.
            "AU" or "BE" or "CA" or "CN" or "CZ" or "HU" or "IN" or
            "IE" or "IT" or "JP" or "NL" or "PK" or "SG" or "ZA" or
            "CH" or "GB" or "US" => GlobalCompanyAnnualDays,

            _ => BaseAnnualDays
        };
    }

    private static int RomanianAnnualDays(int yearsOfService) => yearsOfService switch
    {
        >= 15 => 24,
        >= 10 => 23,
        >= 5 => 22,
        _ => 21
    };

    private static int MexicanAnnualDays(int yearsOfService)
    {
        // The statutory scale reaches the 20-day company floor in year five.
        // From years 6-10 onward it rises by two days per five-year band.
        if (yearsOfService <= 5)
            return GlobalCompanyAnnualDays;

        return 22 + ((yearsOfService - 6) / 5 * 2);
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
