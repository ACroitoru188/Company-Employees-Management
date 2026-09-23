using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Components.Employee;

/// <summary>
/// Single source of truth for contract and request status colors.
/// Returns CSS color variable tokens aligned with the application theme.
/// </summary>
public static class StatusPalette
{
    public static string ForContract(ContractStatus? status) => status switch
    {
        ContractStatus.Active => "var(--success)",
        ContractStatus.Terminated => "var(--error)",
        ContractStatus.Expired => "var(--neutral-foreground-hint)",
        _ => "var(--neutral-foreground-hint)"
    };

    public static string ForRequest(RequestStatus status) => status switch
    {
        RequestStatus.Approved => "var(--success)",
        RequestStatus.Rejected => "var(--error)",
        _ => "var(--warning)"
    };
}
