namespace CompanyEmployees.Data.Entities;

[Flags]
public enum Permission : long
{
    None = 0,
    ViewEmployees = 1 << 0,
    EditEmployees = 1 << 1,
    DeleteEmployees = 1 << 2,
    ViewSalaries = 1 << 3,
    EditSalaries = 1 << 4,
    ManageDepartments = 1 << 5,
    ManageRoles = 1 << 6,
    Administrator = 1 << 7,  // override — toate permisiunile
    RequestLeave = 1 << 8,   // employee can submit/view own leave requests
    ApproveLeave = 1 << 9    // manager can approve/reject subordinates' leave requests
}
