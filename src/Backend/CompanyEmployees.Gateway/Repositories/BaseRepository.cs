using CompanyEmployees.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Gateway.Repositories
{
    public abstract class BaseRepository
    {
        protected readonly CompanyEmployeesDbContext _context;

        protected BaseRepository(CompanyEmployeesDbContext context)
        {
            _context = context;
        }
    }
}
