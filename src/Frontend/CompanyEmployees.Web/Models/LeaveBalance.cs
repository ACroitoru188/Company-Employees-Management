namespace CompanyEmployees.Web.Models;

public sealed record AnnualCarryOverPortion(
    int Days,
    DateOnly ExpiryDate,
    int ExpiredDays);

public class LeaveBalance
{
    public LeaveType Type { get; set; }
    public int DaysUsed { get; set; }
    public int DaysTotal { get; set; }
    public List<AnnualCarryOverPortion> CarryOverPortions { get; set; } = [];
    public int CarriedOverDays => CarryOverPortions.Sum(portion => portion.Days);
    public int ExpiredCarriedOverDays => CarryOverPortions.Sum(portion => portion.ExpiredDays);

    public int BaseDays => DaysTotal - CarriedOverDays;
    public int EffectiveDaysTotal => DaysTotal - ExpiredCarriedOverDays;
    public int Remaining => Math.Max(0, EffectiveDaysTotal - DaysUsed);
}
