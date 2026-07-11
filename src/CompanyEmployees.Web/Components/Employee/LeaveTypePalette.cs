using CompanyEmployees.Web.Models;
using MudBlazor;

namespace CompanyEmployees.Web.Components.Employee;

/// <summary>
/// Single source of truth for the leave-type color map, applied via inline styles
/// because DeepPurple/Gray are not part of the MudBlazor theme Color enum.
/// </summary>
public static class LeaveTypePalette
{
    public static string Hex(LeaveType type) => type switch
    {
        LeaveType.Annual => Colors.Blue.Default,
        LeaveType.Sick => Colors.Orange.Darken1,
        LeaveType.Parental => Colors.DeepPurple.Default,
        LeaveType.Unpaid => Colors.Gray.Darken1,
        _ => Colors.Gray.Darken1
    };

    /// <summary>Hex with alpha suffix for the calendar range highlight (~20% opacity).</summary>
    public static string HighlightHex(LeaveType type) => Hex(type) + "33";

    public static string Label(LeaveType type) => type switch
    {
        LeaveType.Annual => "Annual leave",
        LeaveType.Sick => "Sick leave",
        LeaveType.Parental => "Parental leave",
        LeaveType.Unpaid => "Unpaid leave",
        _ => type.ToString()
    };
}
