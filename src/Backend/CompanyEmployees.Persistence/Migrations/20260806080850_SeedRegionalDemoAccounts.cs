using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedRegionalDemoAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @PasswordHash nvarchar(max) = N'AQAAAAIAAYagAAAAEK/AUDv2jqzDc8ShnwWJAcDDkk+1v1OqmB9hAqjMXqaMweVqoRTVkOeMPIlsKFCZ0g==';
                DECLARE @HrDepartmentId uniqueidentifier = '22222222-0000-0000-0000-000000000003';

                DECLARE @RegionalAccounts TABLE
                (
                    RegionId uniqueidentifier NOT NULL,
                    RegionName nvarchar(100) NOT NULL,
                    RegionCode nvarchar(8) NOT NULL,
                    AdminId uniqueidentifier NOT NULL,
                    ManagerId uniqueidentifier NOT NULL,
                    HrId uniqueidentifier NOT NULL,
                    AdminEmail nvarchar(256) NOT NULL,
                    ManagerEmail nvarchar(256) NOT NULL,
                    HrEmail nvarchar(256) NOT NULL
                );

                INSERT INTO @RegionalAccounts
                    (RegionId, RegionName, RegionCode, AdminId, ManagerId, HrId, AdminEmail, ManagerEmail, HrEmail)
                SELECT
                    region.Id,
                    region.Name,
                    region.Code,
                    COALESCE(existingAdmin.Id, NEWID()),
                    COALESCE(existingManager.Id, NEWID()),
                    COALESCE(existingHr.Id, NEWID()),
                    emails.AdminEmail,
                    emails.ManagerEmail,
                    emails.HrEmail
                FROM Regions region
                CROSS APPLY
                (
                    SELECT
                        CONCAT(N'admin.', LOWER(region.Code), N'@siemens.com') AS AdminEmail,
                        CONCAT(N'lm.', LOWER(region.Code), N'@siemens.com') AS ManagerEmail,
                        CONCAT(N'hr.', LOWER(region.Code), N'@siemens.com') AS HrEmail
                ) emails
                LEFT JOIN AspNetUsers existingAdmin ON existingAdmin.Email = emails.AdminEmail
                LEFT JOIN AspNetUsers existingManager ON existingManager.Email = emails.ManagerEmail
                LEFT JOIN AspNetUsers existingHr ON existingHr.Email = emails.HrEmail
                WHERE region.IsActive = 1;

                INSERT INTO AspNetUsers
                    (Id, Name, Role, Status, ManagerId, DepartmentId, RegionId, CreatedAt, UpdatedAt,
                     UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                     SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
                     LockoutEnd, LockoutEnabled, AccessFailedCount)
                SELECT
                    accounts.AdminId,
                    CONCAT(accounts.RegionName, N' Regional Admin'),
                    4, 0, NULL, NULL, accounts.RegionId, SYSUTCDATETIME(), SYSUTCDATETIME(),
                    accounts.AdminEmail, UPPER(accounts.AdminEmail), accounts.AdminEmail, UPPER(accounts.AdminEmail),
                    1, @PasswordHash, CONVERT(nvarchar(36), NEWID()), CONVERT(nvarchar(36), NEWID()),
                    NULL, 0, 0, NULL, 1, 0
                FROM @RegionalAccounts accounts
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers userAccount WHERE userAccount.Id = accounts.AdminId);

                INSERT INTO AspNetUsers
                    (Id, Name, Role, Status, ManagerId, DepartmentId, RegionId, CreatedAt, UpdatedAt,
                     UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                     SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
                     LockoutEnd, LockoutEnabled, AccessFailedCount)
                SELECT
                    accounts.ManagerId,
                    CONCAT(accounts.RegionName, N' Line Manager'),
                    3, 0, NULL, NULL, accounts.RegionId, SYSUTCDATETIME(), SYSUTCDATETIME(),
                    accounts.ManagerEmail, UPPER(accounts.ManagerEmail), accounts.ManagerEmail, UPPER(accounts.ManagerEmail),
                    1, @PasswordHash, CONVERT(nvarchar(36), NEWID()), CONVERT(nvarchar(36), NEWID()),
                    NULL, 0, 0, NULL, 1, 0
                FROM @RegionalAccounts accounts
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers userAccount WHERE userAccount.Id = accounts.ManagerId);

                INSERT INTO AspNetUsers
                    (Id, Name, Role, Status, ManagerId, DepartmentId, RegionId, CreatedAt, UpdatedAt,
                     UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                     SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
                     LockoutEnd, LockoutEnabled, AccessFailedCount)
                SELECT
                    accounts.HrId,
                    CONCAT(accounts.RegionName, N' HR'),
                    1, 0, accounts.ManagerId, @HrDepartmentId, accounts.RegionId, SYSUTCDATETIME(), SYSUTCDATETIME(),
                    accounts.HrEmail, UPPER(accounts.HrEmail), accounts.HrEmail, UPPER(accounts.HrEmail),
                    1, @PasswordHash, CONVERT(nvarchar(36), NEWID()), CONVERT(nvarchar(36), NEWID()),
                    NULL, 0, 0, NULL, 1, 0
                FROM @RegionalAccounts accounts
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers userAccount WHERE userAccount.Id = accounts.HrId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally additive-only. Removing seeded accounts during a downgrade could
            // destroy contracts, leave history, notifications, or reporting relationships
            // created after deployment.
        }
    }
}
