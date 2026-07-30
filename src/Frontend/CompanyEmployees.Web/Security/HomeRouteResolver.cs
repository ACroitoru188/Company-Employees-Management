using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Web.Security;

// Role wins over department: an HR line manager lands on her team and reaches
// HR via the drawer link. Keep in sync with EmployeeLayout's nav gating.
public static class HomeRouteResolver
{
    public const string HrDepartmentName = "HR";

    public static string Resolve(UserRole? role, string? department)
    {
        if (role == UserRole.LineManager)
            return "/manager/team";

        if (department == HrDepartmentName)
            return "/hr/dashboard";

        return "/employee/dashboard";
    }
}
