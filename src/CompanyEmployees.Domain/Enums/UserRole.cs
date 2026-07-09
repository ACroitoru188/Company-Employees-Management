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
      HR = 3,
      Admin = 4
   }
}
