using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Persistence.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CompanyEmployees.Persistence;

public static class StandbyBootstrapper
{
    private static readonly Guid RomaniaRegionId =
        new("44444444-4444-4444-4444-444444444401");

    public static async Task EnsureReadyAsync(
        IDbProviderPlugin secondaryPlugin,
        string secondaryConnectionString,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
        secondaryPlugin.ConfigureDbContext(optionsBuilder, secondaryConnectionString);

        await using var db = new CompanyEmployeesDbContext(optionsBuilder.Options);
        db.SuppressOutboxCapture = true;
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, cancellationToken);

        // Emergency accounts created while the primary is unavailable are real business data.
        // Capture them in the standby outbox so they can be copied back before an admin fails back.
        db.SuppressOutboxCapture = false;

        if (!bool.TryParse(configuration["DatabaseFailover:SeedFallbackAdmin"], out var seed) || !seed)
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
            new FallbackAdmin(Guid.NewGuid(), "Standby Fallback Admin", configuredEmail),
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
