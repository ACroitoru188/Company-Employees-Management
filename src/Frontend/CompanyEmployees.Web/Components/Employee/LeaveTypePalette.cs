using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Components.Employee;

/// <summary>
/// Single source of truth for the leave-type color map. Literal hex rather than design
/// tokens: Fluent derives its palette from one accent colour, so it has no slot for four
/// unrelated category colours.
/// </summary>
public static class LeaveTypePalette
{
    public static string Hex(LeaveType type, bool isDarkMode = false) => (type, isDarkMode) switch
    {
        (LeaveType.Annual, false) => "#2196F3",
        (LeaveType.Annual, true) => "#90CAF9",
        (LeaveType.Sick, false) => "#FB8C00",
        (LeaveType.Sick, true) => "#FFB74D",
        (LeaveType.Parental, false) => "#673AB7",
        (LeaveType.Parental, true) => "#B39DDB",
        (LeaveType.Unpaid, false) => "#757575",
        (LeaveType.Unpaid, true) => "#BDBDBD",
        (_, false) => "#757575",
        (_, true) => "#BDBDBD"
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
