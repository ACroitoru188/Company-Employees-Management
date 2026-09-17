using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Contexts
{
    // ponytail: contexts reach data only through Gateway interfaces (per the layering spec),
    // so no DbContext here — Application must not know about Persistence.
    public abstract class BaseContext
    {
        protected readonly ILogger _logger;
        protected readonly IPublicHolidayProvider? _holidayProvider;

        protected BaseContext(ILogger logger)
        {
            _logger = logger;
        }

        protected BaseContext(ILogger logger, IPublicHolidayProvider holidayProvider)
        {
            _logger = logger;
            _holidayProvider = holidayProvider;
        }

        protected async Task EnsureWorkingDayAsync(User user, DateOnly day)
        {
            if (_holidayProvider == null)
                throw new InvalidOperationException("Holiday provider not available.");

            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                throw new InvalidOperationException("Leave must start and end on a working day.");

            var holidays = await _holidayProvider.GetHolidaysAsync(user.Region.Code, day.Year);
            var holiday = holidays.FirstOrDefault(candidate => candidate.Date == day);
            if (holiday != null)
                throw new InvalidOperationException($"{day:MMM d} is {holiday.Name} in {user.Region.Name}.");
        }

        protected async Task<int> CountWorkingDaysAsync(User user, DateOnly start, DateOnly end)
        {
            if (_holidayProvider == null)
                throw new InvalidOperationException("Holiday provider not available.");

            var holidays = new HashSet<DateOnly>();
            for (var year = start.Year; year <= end.Year; year++)
            {
                foreach (var holiday in await _holidayProvider.GetHolidaysAsync(user.Region.Code, year))
                    holidays.Add(holiday.Date);
            }

            return CountWorkingDays(start, end, holidays);
        }

        protected static int CountWorkingDays(DateOnly start, DateOnly end, HashSet<DateOnly> holidays)
        {
            var count = 0;
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                    && !holidays.Contains(day))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
