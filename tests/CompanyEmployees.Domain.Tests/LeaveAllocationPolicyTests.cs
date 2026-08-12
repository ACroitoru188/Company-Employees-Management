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
    [InlineData(21, 21, 0)]
    [InlineData(21, 19, 2)]
    [InlineData(21, 16, 5)]
    [InlineData(21, 0, 5)]
    [InlineData(21, 25, 0)]
    public void AnnualCarryOverDays_caps_unused_days_at_five(
        int previousYearTotal,
        int previousYearUsed,
        int expectedCarryOver)
    {
        var carryOver = LeaveAllocationPolicy.AnnualCarryOverDays(
            previousYearTotal,
            previousYearUsed);

        Assert.Equal(expectedCarryOver, carryOver);
    }
}
