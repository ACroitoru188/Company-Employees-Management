using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CompanyEmployees.Persistence;

public static class PostgreSqlStandbyBootstrapper
{
    private static readonly Guid RomaniaRegionId =
        new("44444444-4444-4444-4444-444444444401");

    public static async Task EnsureReadyAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is not configured.");
        var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new CompanyEmployeesDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (!bool.TryParse(configuration["DatabaseFailover:SeedFallbackAdmin"], out var seed)
            || !seed)
            return;

        var region = await db.Regions.SingleOrDefaultAsync(
            item => item.Id == RomaniaRegionId,
            cancellationToken);
        if (region is null)
        {
            region = new Region
            {
                Id = RomaniaRegionId,
                Name = "Romania",
                Code = "RO",
                IsActive = true
            };
            db.Regions.Add(region);
            await db.SaveChangesAsync(cancellationToken);
        }

        var configuredEmail = configuration["DatabaseFailover:FallbackAdminEmail"]
            ?? "itadmin@siemens.com";
        var password = configuration["DatabaseFailover:FallbackAdminPassword"]
            ?? "User123!";
        var accounts = new[]
        {
            new FallbackAdmin(Guid.NewGuid(), "PostgreSQL Fallback Admin", configuredEmail),
            new FallbackAdmin(
                new Guid("11111111-0000-0000-0000-000000000006"),
                "Paul Rusu",
                "admin.paul@siemens.com")
        };
        var hasher = new PasswordHasher<User>();

        foreach (var account in accounts)
        {
            var normalizedEmail = account.Email.ToUpperInvariant();
            if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
                continue;

            var admin = new User
            {
                Id = account.Id,
                Name = account.Name,
                UserName = account.Email,
                NormalizedUserName = normalizedEmail,
                Email = account.Email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                Role = UserRole.Admin,
                Status = UserStatus.Active,
                RegionId = region.Id,
                SecurityStamp = Guid.NewGuid().ToString("D"),
                ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                LockoutEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, password);
            db.Users.Add(admin);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record FallbackAdmin(Guid Id, string Name, string Email);
}
