namespace CompanyEmployees.Domain.Tests;

public class TeamAvailabilityPolicyTests
{
    [Theory]
    [InlineData(5, 2, false)] // exactly 60% available is allowed
    [InlineData(5, 3, true)]  // 40% available triggers a warning
    [InlineData(2, 1, true)]  // 50% available triggers a warning
    [InlineData(0, 0, false)]
    public void IsBelowMinimum_uses_strict_sixty_percent_threshold(
        int teamSize,
        int unavailableMembers,
        bool expected)
    {
        Assert.Equal(expected,
            TeamAvailabilityPolicy.IsBelowMinimum(teamSize, unavailableMembers));
    }

    [Fact]
    public void AvailabilityPercent_returns_team_percentage()
    {
        Assert.Equal(40, TeamAvailabilityPolicy.AvailabilityPercent(5, 3));
    }
}
