using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Persistence;

/// <summary>
/// Rebuilds the PostgreSQL standby from a consistent SQL Server snapshot. The standby is
/// never modified while it is active; synchronization happens only while SQL Server is the
/// selected provider.
/// </summary>
public sealed class PostgreSqlStandbySynchronizer(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    ILogger<PostgreSqlStandbySynchronizer> logger)
{
    private readonly SemaphoreSlim synchronizationLock = new(1, 1);

    public DateTimeOffset? LastSuccessfulSynchronizationUtc { get; private set; }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (state.ActiveProvider != DatabaseProvider.SqlServer)
            return;

        await synchronizationLock.WaitAsync(cancellationToken);
        try
        {
            // Check again after waiting: an administrator may have switched providers while
            // another synchronization owned the lock.
            if (state.ActiveProvider != DatabaseProvider.SqlServer)
                return;

            var sqlOptions = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
                .UseSqlServer(RequiredConnectionString("Default"))
                .Options;
            var postgreSqlOptions = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
                .UseNpgsql(RequiredConnectionString("PostgreSql"))
                .Options;

            DatabaseSnapshot snapshot;
            await using (var sql = new CompanyEmployeesDbContext(sqlOptions))
                snapshot = await ReadSnapshotAsync(sql, cancellationToken);

            await using (var postgres = new CompanyEmployeesDbContext(postgreSqlOptions))
                await ReplaceStandbyAsync(postgres, snapshot, cancellationToken);

            // The baseline already contains every change currently in SQL Server. Mark any
            // older envelopes complete so the delta worker starts exactly after the snapshot.
            await using (var sql = new CompanyEmployeesDbContext(sqlOptions))
            {
                sql.SuppressOutboxCapture = true;
                await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(sql, cancellationToken);
                var pending = await sql.DatabaseOutbox
                    .Where(message => message.ProcessedAtUtc == null)
                    .ToListAsync(cancellationToken);
                var completedAt = DateTime.UtcNow;
                foreach (var message in pending)
                    message.ProcessedAtUtc = completedAt;
                await sql.SaveChangesAsync(cancellationToken);
                state.UpdateReplication(0, null, completedAt, null);
            }

            LastSuccessfulSynchronizationUtc = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Synchronized the complete SQL Server backend to PostgreSQL ({UserCount} users).",
                snapshot.Users.Count);
        }
        finally
        {
            synchronizationLock.Release();
        }
    }

    private string RequiredConnectionString(string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.");

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(
        CompanyEmployeesDbContext db,
        CancellationToken cancellationToken)
    {
        // No navigation properties are loaded. Keeping only scalar values prevents EF from
        // accidentally inserting duplicate related rows into the destination.
        return new DatabaseSnapshot(
            await db.Regions.AsNoTracking().ToListAsync(cancellationToken),
            await db.Departments.AsNoTracking().ToListAsync(cancellationToken),
            await db.Users.AsNoTracking().ToListAsync(cancellationToken),
            await db.Roles.AsNoTracking().ToListAsync(cancellationToken),
            await db.Set<IdentityUserRole<Guid>>().AsNoTracking().ToListAsync(cancellationToken),
            await db.Set<IdentityUserClaim<Guid>>().AsNoTracking().ToListAsync(cancellationToken),
            await db.Set<IdentityUserLogin<Guid>>().AsNoTracking().ToListAsync(cancellationToken),
            await db.Set<IdentityUserToken<Guid>>().AsNoTracking().ToListAsync(cancellationToken),
            await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking().ToListAsync(cancellationToken),
            await db.Contracts.AsNoTracking().ToListAsync(cancellationToken),
            await db.LeaveRequests.AsNoTracking().ToListAsync(cancellationToken),
            await db.LeaveApprovals.AsNoTracking().ToListAsync(cancellationToken),
            await db.LeaveAllocations.AsNoTracking().ToListAsync(cancellationToken),
            await db.Notifications.AsNoTracking().ToListAsync(cancellationToken),
            await db.ManagerDelegations.AsNoTracking().ToListAsync(cancellationToken),
            await db.ImpersonationSessions.AsNoTracking().ToListAsync(cancellationToken),
            await db.DelegatedActions.AsNoTracking().ToListAsync(cancellationToken));
    }

    private static async Task ReplaceStandbyAsync(
        CompanyEmployeesDbContext db,
        DatabaseSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        db.SuppressOutboxCapture = true;
        // PostgreSQL is a disposable standby while SQL Server is active. Recreating its
        // schema avoids stale/deleted rows and guarantees that it exactly matches SQL Server.
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Departments and users form two FK cycles (department manager and employee manager).
        // Insert their base rows first and restore those links in a second update.
        var departmentManagers = snapshot.Departments.ToDictionary(x => x.Id, x => x.ManagerId);
        var userManagers = snapshot.Users.ToDictionary(x => x.Id, x => x.ManagerId);
        foreach (var department in snapshot.Departments)
            department.ManagerId = null;
        foreach (var user in snapshot.Users)
            user.ManagerId = null;

        db.Regions.AddRange(snapshot.Regions);
        db.Departments.AddRange(snapshot.Departments);
        await db.SaveChangesAsync(cancellationToken);

        db.Users.AddRange(snapshot.Users);
        db.Roles.AddRange(snapshot.Roles);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var department in snapshot.Departments)
            department.ManagerId = departmentManagers[department.Id];
        foreach (var user in snapshot.Users)
            user.ManagerId = userManagers[user.Id];
        await db.SaveChangesAsync(cancellationToken);

        db.Set<IdentityUserRole<Guid>>().AddRange(snapshot.UserRoles);
        db.Set<IdentityUserClaim<Guid>>().AddRange(snapshot.UserClaims);
        db.Set<IdentityUserLogin<Guid>>().AddRange(snapshot.UserLogins);
        db.Set<IdentityUserToken<Guid>>().AddRange(snapshot.UserTokens);
        db.Set<IdentityRoleClaim<Guid>>().AddRange(snapshot.RoleClaims);
        db.Contracts.AddRange(snapshot.Contracts);
        db.LeaveRequests.AddRange(snapshot.LeaveRequests);
        db.LeaveAllocations.AddRange(snapshot.LeaveAllocations);
        db.Notifications.AddRange(snapshot.Notifications);
        db.ManagerDelegations.AddRange(snapshot.ManagerDelegations);
        await db.SaveChangesAsync(cancellationToken);

        db.LeaveApprovals.AddRange(snapshot.LeaveApprovals);
        db.ImpersonationSessions.AddRange(snapshot.ImpersonationSessions);
        db.DelegatedActions.AddRange(snapshot.DelegatedActions);
        await db.SaveChangesAsync(cancellationToken);
        await ResetPostgreSqlIdentitySequencesAsync(db, cancellationToken);
        db.ChangeTracker.Clear();
    }

    internal static Task ResetPostgreSqlIdentitySequencesAsync(
        CompanyEmployeesDbContext db,
        CancellationToken cancellationToken = default) =>
        db.Database.ExecuteSqlRawAsync("""
            SELECT setval(
                pg_get_serial_sequence('"AspNetUserClaims"', 'Id'),
                COALESCE(MAX("Id"), 1),
                COUNT(*) > 0)
            FROM "AspNetUserClaims";
            SELECT setval(
                pg_get_serial_sequence('"AspNetRoleClaims"', 'Id'),
                COALESCE(MAX("Id"), 1),
                COUNT(*) > 0)
            FROM "AspNetRoleClaims";
            """, cancellationToken);

    private sealed record DatabaseSnapshot(
        List<Region> Regions,
        List<Department> Departments,
        List<User> Users,
        List<IdentityRole<Guid>> Roles,
        List<IdentityUserRole<Guid>> UserRoles,
        List<IdentityUserClaim<Guid>> UserClaims,
        List<IdentityUserLogin<Guid>> UserLogins,
        List<IdentityUserToken<Guid>> UserTokens,
        List<IdentityRoleClaim<Guid>> RoleClaims,
        List<Contract> Contracts,
        List<LeaveRequest> LeaveRequests,
        List<LeaveApproval> LeaveApprovals,
        List<LeaveAllocation> LeaveAllocations,
        List<Notification> Notifications,
        List<ManagerDelegation> ManagerDelegations,
        List<ImpersonationSession> ImpersonationSessions,
        List<DelegatedAction> DelegatedActions);
}
