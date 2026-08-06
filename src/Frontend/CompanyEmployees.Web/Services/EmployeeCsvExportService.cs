using System.Globalization;
using System.Text;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Web.Services;

public sealed class EmployeeCsvExportService
{
    private static readonly string[] Header =
    [
        "employee_id",
        "name",
        "email",
        "username",
        "phone_number",
        "role",
        "employee_status",
        "manager_name",
        "department",
        "contract_type",
        "contract_start_date",
        "contract_end_date"
    ];

    private readonly CompanyEmployeesDbContext _db;

    public EmployeeCsvExportService(CompanyEmployeesDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeCsvExport> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users
            .Include(user => user.Manager)
            .Include(user => user.Department)
            .Include(user => user.Contracts)
            .AsNoTracking()
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        AppendRow(csv, Header);

        foreach (var user in users)
        {
            var contract = GetLatestContract(user);
            AppendRow(csv,
            [
                user.Id.ToString("D"),
                user.Name,
                user.Email,
                user.UserName,
                user.PhoneNumber,
                user.Role.ToString(),
                user.Status.ToString(),
                user.Manager?.Name,
                user.Department?.Name,
                contract?.Type.ToString(),
                contract?.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                contract?.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ]);
        }

        // A UTF-8 BOM makes names with diacritics open correctly in desktop Excel.
        var preamble = Encoding.UTF8.GetPreamble();
        var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
        var content = new byte[preamble.Length + csvBytes.Length];
        preamble.CopyTo(content, 0);
        csvBytes.CopyTo(content, preamble.Length);

        return new EmployeeCsvExport(
            content,
            $"employees-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private static Contract? GetLatestContract(User user) =>
        user.Contracts
            .OrderByDescending(contract => contract.CreatedAt)
            .FirstOrDefault();

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string?> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
                csv.Append(',');

            AppendField(csv, values[index]);
        }

        csv.Append("\r\n");
    }

    private static void AppendField(StringBuilder csv, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var requiresQuotes = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        if (!requiresQuotes)
        {
            csv.Append(value);
            return;
        }

        csv.Append('"');
        csv.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        csv.Append('"');
    }
}

public sealed record EmployeeCsvExport(byte[] Content, string FileName);
