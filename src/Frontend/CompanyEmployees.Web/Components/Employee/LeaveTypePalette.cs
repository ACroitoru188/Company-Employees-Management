using CompanyEmployees.Web.Models;
using MudBlazor;

namespace CompanyEmployees.Web.Components.Employee;

/// <summary>
/// Single source of truth for the leave-type color map, applied via inline styles
/// because DeepPurple/Gray are not part of the MudBlazor theme Color enum.
/// </summary>
public static class LeaveTypePalette
{
    public static string Hex(LeaveType type, bool isDarkMode = false) => (type, isDarkMode) switch
    {
        (LeaveType.Annual, false) => Colors.Blue.Default,
        (LeaveType.Annual, true) => Colors.Blue.Lighten2,
        (LeaveType.Sick, false) => Colors.Orange.Darken1,
        (LeaveType.Sick, true) => Colors.Orange.Lighten1,
        (LeaveType.Parental, false) => Colors.DeepPurple.Default,
        (LeaveType.Parental, true) => Colors.DeepPurple.Lighten2,
        (LeaveType.Unpaid, false) => Colors.Gray.Darken1,
        (LeaveType.Unpaid, true) => Colors.Gray.Lighten1,
        (_, false) => Colors.Gray.Darken1,
        (_, true) => Colors.Gray.Lighten1
    };

    /// <summary>Hex with alpha suffix for the calendar range highlight (~20% opacity).</summary>
    public static string HighlightHex(LeaveType type, bool isDarkMode = false) => Hex(type, isDarkMode) + "33";

    public static string Label(LeaveType type) => type switch
    {
        LeaveType.Annual => "Annual leave",
        LeaveType.Sick => "Sick leave",
        LeaveType.Parental => "Parental leave",
        LeaveType.Unpaid => "Unpaid leave",
        _ => type.ToString()
    };
}
