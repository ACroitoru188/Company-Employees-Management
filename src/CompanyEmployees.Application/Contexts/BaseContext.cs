using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Contexts
{
    // ponytail: contexts reach data only through Gateway interfaces (per the layering spec),
    // so no DbContext here — Application must not know about Persistence.
    public abstract class BaseContext
    {
        protected readonly ILogger _logger;

        protected BaseContext(ILogger logger)
        {
            _logger = logger;
        }
    }
}
