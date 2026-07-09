using System;

namespace CompanyEmployees.Domain.Enums
{
    [Flags]
    public enum Permission
    {
        None = 0,
        ViewEmployees = 1 << 0,
        EditEmployees = 1 << 1,
        DeleteEmployees = 1 << 2,
        ViewSalaries = 1 << 3,
        EditSalaries = 1 << 4,
        ManageDepartments = 1 << 5,
        ManageRoles = 1 << 6,
        Administrator = 1 << 7,
        RequestLeave = 1 << 8,
        ApproveLeave = 1 << 9
    }
}