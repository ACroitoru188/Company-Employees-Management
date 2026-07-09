using CompanyEmployees.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Application.Contexts
{
    public abstract class BaseContext
    {
        protected readonly CompanyEmployeesDbContext _dbContext;
        protected readonly ILogger _logger;

        protected BaseContext(CompanyEmployeesDbContext dbContext, ILogger logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }
    }
}
