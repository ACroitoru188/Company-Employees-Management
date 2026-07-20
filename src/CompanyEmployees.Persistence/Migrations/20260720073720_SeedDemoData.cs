using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDemoData : Migration
    {
        // The demo accounts and their leave data live in Migrations/SeedData/SeedDemoData.sql
        // (embedded resource) rather than in C#, so editing the dataset means editing SQL only.
        private const string SeedScriptResource =
            "CompanyEmployees.Persistence.Migrations.SeedData.SeedDemoData.sql";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReadSeedScript());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Seed rows all carry a fixed GUID prefix per table, so they can be removed
            // without touching anything created through the app. Children first, then the
            // circular Users <-> Departments references are broken before deleting either.
            migrationBuilder.Sql(@"
DELETE FROM [LeaveApprovals]   WHERE CAST([Id] AS char(36)) LIKE '55555555-%';
DELETE FROM [LeaveRequests]    WHERE CAST([Id] AS char(36)) LIKE '44444444-%';
DELETE FROM [LeaveAllocations] WHERE CAST([Id] AS char(36)) LIKE '33333333-%';
UPDATE [Departments] SET [ManagerId] = NULL WHERE CAST([Id] AS char(36)) LIKE '22222222-%';
UPDATE [AspNetUsers] SET [ManagerId] = NULL, [DepartmentId] = NULL WHERE CAST([Id] AS char(36)) LIKE '11111111-%';
DELETE FROM [AspNetUsers]      WHERE CAST([Id] AS char(36)) LIKE '11111111-%';
DELETE FROM [Departments]      WHERE CAST([Id] AS char(36)) LIKE '22222222-%';
");
        }

        private static string ReadSeedScript()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(SeedScriptResource)
                ?? throw new InvalidOperationException(
                    $"Embedded seed script '{SeedScriptResource}' was not found. " +
                    "Check the EmbeddedResource entry in CompanyEmployees.Persistence.csproj.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
