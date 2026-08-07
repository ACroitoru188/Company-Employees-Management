using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

public static class LeaveAllocationPolicy
{
    public static int DefaultDays(LeaveType type) => type switch
    {
        LeaveType.Annual => 21,
        LeaveType.Sick => 10,
        LeaveType.Parental => 10,
        LeaveType.Unpaid => 30,
        _ => 0
    };
}
