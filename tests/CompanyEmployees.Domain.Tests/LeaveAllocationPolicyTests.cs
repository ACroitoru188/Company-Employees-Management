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
}
