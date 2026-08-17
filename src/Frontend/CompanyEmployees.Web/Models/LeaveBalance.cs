namespace CompanyEmployees.Web.Models;

public class LeaveBalance
{
    public LeaveType Type { get; set; }
    public int DaysUsed { get; set; }
    public int DaysTotal { get; set; }
    public int CarriedOverDays { get; set; }
    public int ExpiredCarriedOverDays { get; set; }
    public DateOnly? CarryOverExpiryDate { get; set; }

    public int BaseDays => DaysTotal - CarriedOverDays;
    public int EffectiveDaysTotal => DaysTotal - ExpiredCarriedOverDays;
    public int Remaining => Math.Max(0, EffectiveDaysTotal - DaysUsed);
}
