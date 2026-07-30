using System.Runtime.InteropServices;

namespace CompanyEmployees.Domain.Enums;

public enum AuditAction
{
    Create,
    Update,
    Delete,
    Impersonate,
    Promote
}