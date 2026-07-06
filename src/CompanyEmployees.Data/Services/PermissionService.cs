using CompanyEmployees.Data.Entities;

namespace CompanyEmployees.Data.Services;

public static class PermissionService
{
    /// <summary>
    /// Permisiunile efective = reuniunea (bitwise OR) permisiunilor tuturor rolurilor.
    /// Administrator acordă orice permisiune. Necesită Roles încărcate
    /// (ex. .Include(e => e.Roles)).
    /// </summary>
    public static bool HasPermission(Employee employee, Permission permission)
    {
        var effective = employee.Roles
            .Aggregate(Permission.None, (acc, role) => acc | role.Permissions);

        if (effective.HasFlag(Permission.Administrator))
            return true;

        return (effective & permission) == permission;
    }
}
