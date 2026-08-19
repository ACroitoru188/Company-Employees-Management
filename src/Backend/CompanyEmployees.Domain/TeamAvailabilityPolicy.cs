namespace CompanyEmployees.Domain;

public static class TeamAvailabilityPolicy
{
    public const int MinimumAvailabilityPercent = 60;

    public static bool IsBelowMinimum(int teamSize, int unavailableMembers)
    {
        if (teamSize <= 0)
            return false;

        var availableMembers = Math.Max(0, teamSize - unavailableMembers);
        return availableMembers * 100 < teamSize * MinimumAvailabilityPercent;
    }

    public static int AvailabilityPercent(int teamSize, int unavailableMembers)
    {
        if (teamSize <= 0)
            return 100;

        var availableMembers = Math.Max(0, teamSize - unavailableMembers);
        return (int)Math.Round(availableMembers * 100m / teamSize);
    }
}
