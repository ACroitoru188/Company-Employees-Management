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
       // 2 is intentionally unused: ProjectManager was folded into LineManager
       // (RemoveProjectManagerRole migration, 2026-07-28) — don't reuse this value.
       LineManager = 3, // one per department; HR is a department, not a role
       Admin = 4,
       CountryManager = 5
    }
}
