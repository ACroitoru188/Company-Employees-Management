using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Persistence.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Persistence;

/// <summary>
/// Rebuilds the secondary (standby) from a consistent primary snapshot.
/// The standby is never modified while it is active; synchronization happens only while
/// the primary is the selected provider.
/// </summary>
public sealed class StandbySynchronizer(
    IDbProviderPlugin primaryPlugin,
    string primaryConnectionString,
    IDbProviderPlugin secondaryPlugin,
    string secondaryConnectionString,
    DatabaseRuntimeState state,
    ILogger<StandbySynchronizer> logger) : IStandbyReplicationService
{
    private readonly SemaphoreSlim synchronizationLock = new(1, 1);

    public DateTimeOffset? LastSuccessfulSynchronizationUtc { get; private set; }

    /// <inheritdoc />
    public bool CanReplicate(string primaryId, string secondaryId) =>
        primaryId == primaryPlugin.Id && secondaryId == secondaryPlugin.Id;

    /// <inheritdoc />
    public async Task SynchronizeAsync(CancellationToken ct = default)
    {
        if (state.ActiveProviderId != state.PrimaryProviderId)
            return;

        await synchronizationLock.WaitAsync(ct);
        try
        {
            if (state.ActiveProviderId != state.PrimaryProviderId)
                return;

            var primaryOptions = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
            primaryPlugin.ConfigureDbContext(primaryOptions, primaryConnectionString);

            var secondaryOptions = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
            secondaryPlugin.ConfigureDbContext(secondaryOptions, secondaryConnectionString);

            DatabaseSnapshot snapshot;
            await using (var primary = new CompanyEmployeesDbContext(primaryOptions.Options))
                snapshot = await ReadSnapshotAsync(primary, ct);

            await using (var secondary = new CompanyEmployeesDbContext(secondaryOptions.Options))
                await ReplaceStandbyAsync(secondary, snapshot, ct);

            // Mark any outbox messages that were already in the primary snapshot as processed
            // so the delta worker starts exactly from this point forward.
            await using (var primary = new CompanyEmployeesDbContext(primaryOptions.Options))
            {
                primary.SuppressOutboxCapture = true;
                await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(primary, ct);
                var pending = await primary.DatabaseOutbox
                    .Where(message => message.ProcessedAtUtc == null)
                    .ToListAsync(ct);
                var completedAt = DateTime.UtcNow;
                foreach (var message in pending)
                    message.ProcessedAtUtc = completedAt;
                await primary.SaveChangesAsync(ct);
                state.UpdateReplication(0, null, completedAt, null);
            }

            LastSuccessfulSynchronizationUtc = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Synchronized the complete {Primary} backend to {Secondary} ({UserCount} users).",
                primaryPlugin.DisplayName,
                secondaryPlugin.DisplayName,
                snapshot.Users.Count);
        }
        finally
        {
            synchronizationLock.Release();
        }
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(
        CompanyEmployeesDbContext db,
        CancellationToken cancellationToken)
    {
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
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Departments and users have FK cycles (department manager, employee manager).
        // Insert base rows first, then restore the FK links in a second pass.
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
