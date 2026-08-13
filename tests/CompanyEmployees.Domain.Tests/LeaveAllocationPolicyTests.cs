using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Tests;

public class LeaveAllocationPolicyTests
{
    [Theory]
    [InlineData(LeaveType.Annual, 21)]
    [InlineData(LeaveType.Sick, 10)]
    [InlineData(LeaveType.Parental, 10)]
    [InlineData(LeaveType.Unpaid, 30)]
    public void DefaultDays_returns_expected_allocation(LeaveType type, int expectedDays)
    {
        var actualDays = LeaveAllocationPolicy.DefaultDays(type);

        Assert.Equal(expectedDays, actualDays);
    }

    [Theory]
    [InlineData("RO", 2022, 1, 1, 2026, 21)]
    [InlineData("RO", 2021, 1, 1, 2026, 22)]
    [InlineData("ro", 2016, 1, 1, 2026, 23)]
    [InlineData("RO", 2016, 1, 2, 2026, 22)]
    [InlineData("RO", 2011, 1, 1, 2026, 24)]
    [InlineData("RO", 2006, 1, 1, 2026, 24)]
    [InlineData("DE", 2006, 1, 1, 2026, 21)]
    public void AnnualDaysForRegion_applies_company_seniority_only_in_Romania(
        string regionCode,
        int startYear,
        int startMonth,
        int startDay,
        int entitlementYear,
        int expectedDays)
    {
        var days = LeaveAllocationPolicy.AnnualDaysForRegion(
            regionCode,
            new DateOnly(startYear, startMonth, startDay),
            entitlementYear);

        Assert.Equal(expectedDays, days);
    }

    [Fact]
    public void AnnualDaysForRegion_without_a_contract_start_uses_base_entitlement()
    {
        Assert.Equal(21, LeaveAllocationPolicy.AnnualDaysForRegion("RO", null, 2026));
    }

    [Theory]
    [InlineData(21, 21, 0)]
    [InlineData(21, 19, 2)]
    [InlineData(21, 16, 5)]
    [InlineData(21, 5, 16)]
    [InlineData(21, 0, 21)]
    [InlineData(21, 25, 0)]
    public void AnnualCarryOverDays_returns_all_unused_days(
        int previousYearTotal,
        int previousYearUsed,
        int expectedCarryOver)
    {
        var carryOver = LeaveAllocationPolicy.AnnualCarryOverDays(
            previousYearTotal,
            previousYearUsed);

        Assert.Equal(expectedCarryOver, carryOver);
    }

    [Fact]
    public void AnnualCarryOverExpiryDate_is_eighteen_months_after_entitlement_starts()
    {
        Assert.Equal(new DateOnly(2027, 6, 30),
            LeaveAllocationPolicy.AnnualCarryOverExpiryDate(2027));
    }

    [Theory]
    [InlineData(2027, 6, 30, 16, 5, 0)]
    [InlineData(2027, 7, 1, 16, 5, 11)]
    [InlineData(2027, 7, 1, 16, 16, 0)]
    public void ExpiredAnnualCarryOverDays_expires_only_the_unused_remainder(
        int year,
        int month,
        int day,
        int carriedOver,
        int usedBeforeExpiry,
        int expectedExpired)
    {
        var expired = LeaveAllocationPolicy.ExpiredAnnualCarryOverDays(
            carriedOver,
            usedBeforeExpiry,
            2027,
            new DateOnly(year, month, day));

        Assert.Equal(expectedExpired, expired);
    }
}
