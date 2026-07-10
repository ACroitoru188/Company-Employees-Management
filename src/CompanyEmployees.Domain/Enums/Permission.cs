using System;

namespace CompanyEmployees.Domain.Enums
{
    public enum Permission
    {
        None,
        ViewEmployees,
        EditEmployees,
        DeleteEmployees,
        ViewSalaries,
        EditSalaries,
        ManageDepartments,
        ManageRoles,
        Administrator,
        RequestLeave,
        ApproveLeave
    }
}