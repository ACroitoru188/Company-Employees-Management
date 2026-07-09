using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Application
{
    public class LoginResult
    {
        public int EmployeeId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int RoleId { get; set; }
        public string SessionToken { get; set; } = string.Empty;
    }
}
