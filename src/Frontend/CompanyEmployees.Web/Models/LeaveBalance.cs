namespace CompanyEmployees.Web.Models;

public class LeaveBalance
{
    public LeaveType Type { get; set; }
    public int DaysUsed { get; set; }
    public int DaysTotal { get; set; }
    public int CarriedOverDays { get; set; }

    public int BaseDays => DaysTotal - CarriedOverDays;
    public int Remaining => DaysTotal - DaysUsed;
}
