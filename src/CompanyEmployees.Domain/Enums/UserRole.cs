using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.Enums
{
   public enum UserRole
   {
      Guest = 0, //default
      Employee = 1,
      ProjectManager = 2,
      LineManager = 3, // one per department; HR is a department, not a role
      Admin = 4
   }
}
