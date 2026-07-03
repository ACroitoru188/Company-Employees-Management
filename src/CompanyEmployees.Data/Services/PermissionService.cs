using CompanyEmployees.Data.Entities;

namespace CompanyEmployees.Data.Services;

public static class PermissionService
{
    /// <summary>
    /// Permisiunile efective = reuniunea (bitwise OR) permisiunilor tuturor rolurilor.
    /// Administrator acordă orice permisiune. Necesită EmployeeRoles + Role încărcate
    /// (ex. .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role)).
    /// </summary>
    public static bool HasPermission(Employee employee, Permission permission)
    {
        var effective = employee.EmployeeRoles
            .Aggregate(Permission.None, (acc, er) => acc | er.Role.Permissions);

        if (effective.HasFlag(Permission.Administrator))
            return true;

        return (effective & permission) == permission;
    }
}
