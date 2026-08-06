using System.Collections.Concurrent;
using System.Globalization;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// Nationwide non-working days from Article 139 of the Romanian Labour Code.
/// Movable Christian holidays follow the Romanian Orthodox calendar used by the
/// shared company calendar. Faith-specific days are handled by the employer.
/// </summary>
public static class RomanianPublicHolidays
{
    private static readonly JulianCalendar JulianCalendar = new();
    private static readonly ConcurrentDictionary<int, IReadOnlyDictionary<DateOnly, string>> Cache = new();

    public static bool IsHoliday(DateOnly date) =>
        ForYear(date.Year).ContainsKey(date);

    public static string? GetName(DateOnly date) =>
        ForYear(date.Year).GetValueOrDefault(date);

    public static IReadOnlyDictionary<DateOnly, string> ForMonth(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        return ForYear(month.Year)
            .Where(holiday => holiday.Key >= first && holiday.Key <= last)
            .ToDictionary();
    }

    private static IReadOnlyDictionary<DateOnly, string> ForYear(int year) =>
        Cache.GetOrAdd(year, BuildYear);

    private static IReadOnlyDictionary<DateOnly, string> BuildYear(int year)
    {
        var holidays = new Dictionary<DateOnly, string>();

        AddFixed(1, 1, "New Year's Day");
        AddFixed(1, 2, "Second Day of New Year");
        AddFixed(1, 6, "Epiphany");
        AddFixed(1, 7, "St John the Baptist Day");
        AddFixed(1, 24, "Unification Day");
        AddFixed(5, 1, "Labour Day");
        AddFixed(6, 1, "Children's Day");
        AddFixed(8, 15, "Dormition of the Mother of God");
        AddFixed(11, 30, "St Andrew's Day");
        AddFixed(12, 1, "Great Union Day");
        AddFixed(12, 25, "Christmas Day");
        AddFixed(12, 26, "Second Day of Christmas");

        var easter = OrthodoxEasterSunday(year);
        AddHoliday(easter.AddDays(-2), "Good Friday");
        AddHoliday(easter, "Orthodox Easter Sunday");
        AddHoliday(easter.AddDays(1), "Orthodox Easter Monday");
        AddHoliday(easter.AddDays(49), "Orthodox Pentecost Sunday");
        AddHoliday(easter.AddDays(50), "Orthodox Pentecost Monday");

        return holidays;

        void AddFixed(int month, int day, string name) =>
            AddHoliday(new DateOnly(year, month, day), name);

        void AddHoliday(DateOnly date, string name)
        {
            holidays[date] = holidays.TryGetValue(date, out var existing)
                ? $"{existing} / {name}"
                : name;
        }
    }

    private static DateOnly OrthodoxEasterSunday(int year)
    {
        // Meeus' Julian-calendar computus; JulianCalendar converts the result
        // to the corresponding Gregorian DateTime used by DateOnly.
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = (d + e + 114) % 31 + 1;

        return DateOnly.FromDateTime(
            JulianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0));
    }
}
