using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResetAccountsAndLeaveData : Migration
    {
        // The account/leave-data changes live in Migrations/SeedData/ResetAccountsAndLeaveData.sql
        // (embedded resource) rather than in C#, matching SeedDemoData's convention.
        private const string SeedScriptResource =
            "CompanyEmployees.Persistence.Migrations.SeedData.ResetAccountsAndLeaveData.sql";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReadSeedScript());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The wiped LeaveRequests/LeaveApprovals cannot be restored (that data is gone by
            // design). This only reverses the account changes: drop the 68 added users (their
            // LeaveAllocations cascade-delete with them) and restore the 5 original accounts.
            migrationBuilder.Sql(@"
DELETE FROM [AspNetUsers] WHERE CAST([Id] AS char(36)) LIKE '11111111-%' AND CAST(RIGHT(CAST([Id] AS char(36)), 12) AS bigint) >= 38;

INSERT INTO [AspNetUsers] ([Id],[Name],[Role],[Status],[ManagerId],[DepartmentId],[CreatedAt],[UpdatedAt],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('11111111-0000-0000-0000-000000000001', N'Demo Admin', 4, 0, NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME(), N'itadmin@siemens.com', N'ITADMIN@SIEMENS.COM', N'itadmin@siemens.com', N'ITADMIN@SIEMENS.COM', 1, N'AQAAAAIAAYagAAAAEL1IjzbTEOHKv5/RethDUOuUyCg3qGzvKldy/LoHp5GX+kcp54DOwlEnndbjf8JZog==', '66666666-0000-0000-0000-000000000001', '77777777-0000-0000-0000-000000000001', NULL, 0, 0, NULL, 1, 0);
INSERT INTO [AspNetUsers] ([Id],[Name],[Role],[Status],[ManagerId],[DepartmentId],[CreatedAt],[UpdatedAt],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('11111111-0000-0000-0000-000000000002', N'Demo Line Manager', 3, 0, '11111111-0000-0000-0000-000000000001', '22222222-0000-0000-0000-000000000001', SYSUTCDATETIME(), SYSUTCDATETIME(), N'linemanager@siemens.com', N'LINEMANAGER@SIEMENS.COM', N'linemanager@siemens.com', N'LINEMANAGER@SIEMENS.COM', 1, N'AQAAAAIAAYagAAAAEL1IjzbTEOHKv5/RethDUOuUyCg3qGzvKldy/LoHp5GX+kcp54DOwlEnndbjf8JZog==', '66666666-0000-0000-0000-000000000002', '77777777-0000-0000-0000-000000000002', NULL, 0, 0, NULL, 1, 0);
INSERT INTO [AspNetUsers] ([Id],[Name],[Role],[Status],[ManagerId],[DepartmentId],[CreatedAt],[UpdatedAt],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('11111111-0000-0000-0000-000000000003', N'Demo Project Manager', 2, 0, '11111111-0000-0000-0000-000000000002', NULL, SYSUTCDATETIME(), SYSUTCDATETIME(), N'projectmanager@siemens.com', N'PROJECTMANAGER@SIEMENS.COM', N'projectmanager@siemens.com', N'PROJECTMANAGER@SIEMENS.COM', 1, N'AQAAAAIAAYagAAAAEL1IjzbTEOHKv5/RethDUOuUyCg3qGzvKldy/LoHp5GX+kcp54DOwlEnndbjf8JZog==', '66666666-0000-0000-0000-000000000003', '77777777-0000-0000-0000-000000000003', NULL, 0, 0, NULL, 1, 0);
INSERT INTO [AspNetUsers] ([Id],[Name],[Role],[Status],[ManagerId],[DepartmentId],[CreatedAt],[UpdatedAt],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('11111111-0000-0000-0000-000000000004', N'Demo Employee', 1, 0, '11111111-0000-0000-0000-000000000003', '22222222-0000-0000-0000-000000000001', SYSUTCDATETIME(), SYSUTCDATETIME(), N'employee@siemens.com', N'EMPLOYEE@SIEMENS.COM', N'employee@siemens.com', N'EMPLOYEE@SIEMENS.COM', 1, N'AQAAAAIAAYagAAAAEL1IjzbTEOHKv5/RethDUOuUyCg3qGzvKldy/LoHp5GX+kcp54DOwlEnndbjf8JZog==', '66666666-0000-0000-0000-000000000004', '77777777-0000-0000-0000-000000000004', NULL, 0, 0, NULL, 1, 0);
INSERT INTO [AspNetUsers] ([Id],[Name],[Role],[Status],[ManagerId],[DepartmentId],[CreatedAt],[UpdatedAt],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('11111111-0000-0000-0000-000000000005', N'Demo Colleague', 1, 0, '11111111-0000-0000-0000-000000000003', '22222222-0000-0000-0000-000000000001', SYSUTCDATETIME(), SYSUTCDATETIME(), N'colleague@siemens.com', N'COLLEAGUE@SIEMENS.COM', N'colleague@siemens.com', N'COLLEAGUE@SIEMENS.COM', 1, N'AQAAAAIAAYagAAAAEL1IjzbTEOHKv5/RethDUOuUyCg3qGzvKldy/LoHp5GX+kcp54DOwlEnndbjf8JZog==', '66666666-0000-0000-0000-000000000005', '77777777-0000-0000-0000-000000000005', NULL, 0, 0, NULL, 1, 0);
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
