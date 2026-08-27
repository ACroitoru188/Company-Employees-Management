using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CompanyEmployees.Persistence;

public sealed class DatabaseReplicationCoordinator(
    DatabaseRuntimeState state,
    IDbProviderPlugin primaryPlugin,
    string primaryConnectionString,
    IDbProviderPlugin secondaryPlugin,
    string secondaryConnectionString,
    ILogger<DatabaseReplicationCoordinator> logger)
{
    private readonly SemaphoreSlim replicationLock = new(1, 1);

    public async Task<int> ReplicateNextBatchAsync(CancellationToken cancellationToken = default)
    {
        // Source is whichever provider is currently active; target is the other one.
        var sourceIsSecondary = state.ActiveProviderId == state.SecondaryProviderId;
        var (sourcePlugin, sourceCs, targetPlugin, targetCs) = sourceIsSecondary
            ? (secondaryPlugin, secondaryConnectionString, primaryPlugin, primaryConnectionString)
            : (primaryPlugin, primaryConnectionString, secondaryPlugin, secondaryConnectionString);
        return await ReplicateNextBatchAsync(sourcePlugin, sourceCs, targetPlugin, targetCs, cancellationToken);
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (await ReplicateNextBatchAsync(cancellationToken) > 0)
        {
        }

        if (state.PendingReplicationChanges != 0)
            throw new InvalidOperationException(
                $"Cannot switch databases while {state.PendingReplicationChanges} changes remain unsynchronized.");
    }

    public async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var activeIsSecondary = state.ActiveProviderId == state.SecondaryProviderId;
        var (plugin, cs) = activeIsSecondary
            ? (secondaryPlugin, secondaryConnectionString)
            : (primaryPlugin, primaryConnectionString);
        await using var source = CreateContext(plugin, cs);
        source.SuppressOutboxCapture = true;
        await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(source, cancellationToken);
        var pending = await source.DatabaseOutbox.CountAsync(
            message => message.ProcessedAtUtc == null,
            cancellationToken);
        var oldest = await source.DatabaseOutbox
            .Where(message => message.ProcessedAtUtc == null)
            .OrderBy(message => message.CreatedAtUtc)
            .Select(message => (DateTime?)message.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        state.UpdateReplication(pending, oldest, state.LastSynchronizedUtc, state.ReplicationError);
    }

    private async Task<int> ReplicateNextBatchAsync(
        IDbProviderPlugin sourcePlugin,
        string sourceConnectionString,
        IDbProviderPlugin targetPlugin,
        string targetConnectionString,
        CancellationToken cancellationToken)
    {
        await replicationLock.WaitAsync(cancellationToken);
        try
        {
            await using var source = CreateContext(sourcePlugin, sourceConnectionString);
            await using var target = CreateContext(targetPlugin, targetConnectionString);
            source.SuppressOutboxCapture = true;
            target.SuppressOutboxCapture = true;
            await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(source, cancellationToken);
            await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(target, cancellationToken);

            var first = await source.DatabaseOutbox
                .Where(message => message.ProcessedAtUtc == null)
                .OrderBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.BatchOrder)
                .FirstOrDefaultAsync(cancellationToken);
            if (first == null)
            {
                state.UpdateReplication(0, null, state.LastSynchronizedUtc, null);
                return 0;
            }

            var batch = await source.DatabaseOutbox
                .Where(message => message.BatchId == first.BatchId
                                  && message.ProcessedAtUtc == null)
                .OrderBy(message => message.BatchOrder)
                .ToListAsync(cancellationToken);

            try
            {
                await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken);
                var recoveredPrincipals = new HashSet<string>(StringComparer.Ordinal);
                foreach (var message in batch)
                    await ApplyAsync(source, target, message, recoveredPrincipals, cancellationToken);
                await target.SaveChangesAsync(cancellationToken);
                if (targetPlugin.Id == "postgresql")
                    await StandbySynchronizer.ResetPostgreSqlIdentitySequencesAsync(
                        target,
                        cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var completedAt = DateTime.UtcNow;
                foreach (var message in batch)
                {
                    message.ProcessedAtUtc = completedAt;
                    message.LastError = null;
                }
                await source.SaveChangesAsync(cancellationToken);

                var remaining = await source.DatabaseOutbox.CountAsync(
                    message => message.ProcessedAtUtc == null,
                    cancellationToken);
                var oldest = await source.DatabaseOutbox
                    .Where(message => message.ProcessedAtUtc == null)
                    .OrderBy(message => message.CreatedAtUtc)
                    .Select(message => (DateTime?)message.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                state.UpdateReplication(remaining, oldest, completedAt, null);
                return batch.Count;
            }
            catch (Exception ex)
            {
                var detailedError = ex.GetBaseException().Message;
                foreach (var message in batch)
                {
                    message.AttemptCount++;
                    message.LastError = detailedError.Length <= 2000
                        ? detailedError
                        : detailedError[..2000];
                }
                await source.SaveChangesAsync(cancellationToken);
                state.UpdateReplication(
                    batch.Count,
                    first.CreatedAtUtc,
                    state.LastSynchronizedUtc,
                    detailedError);
                logger.LogError(
                    ex,
                    "Failed to replicate database outbox batch {BatchId} from {Source} to {Target}.",
                    first.BatchId,
                    sourcePlugin.Id,
                    targetPlugin.Id);
                throw;
            }
        }
        finally
        {
            replicationLock.Release();
        }
    }

    private static async Task ApplyAsync(
        CompanyEmployeesDbContext source,
        CompanyEmployeesDbContext target,
        DatabaseOutboxMessage message,
        HashSet<string> recoveredPrincipals,
        CancellationToken cancellationToken)
    {
        var entityType = target.Model.FindEntityType(message.EntityType)
            ?? throw new InvalidOperationException($"Unknown replicated entity type '{message.EntityType}'.");
        if (entityType.ClrType == typeof(DatabaseOutboxMessage))
            return;

        var keyPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.KeyJson)
            ?? throw new InvalidOperationException("The replicated entity key is invalid.");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity '{message.EntityType}' has no primary key.");
        var keyValues = primaryKey.Properties
            .Select(property => Deserialize(keyPayload[property.Name], property.ClrType))
            .ToArray();
        var entity = await target.FindAsync(entityType.ClrType, keyValues, cancellationToken);
        var preserveTargetPrimaryKey = false;

        if (message.Operation == "Delete")
        {
            if (entity != null)
                target.Remove(entity);
            return;
        }

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            message.PayloadJson ?? throw new InvalidOperationException("An upsert has no payload."))
            ?? throw new InvalidOperationException("The replicated entity payload is invalid.");
        if (entity == null && entityType.ClrType == typeof(User))
        {
            entity = await FindMatchingUserAsync(target, payload, cancellationToken);
            preserveTargetPrimaryKey = entity != null;
        }

        if (entity == null && entityType.ClrType == typeof(LeaveAllocation))
        {
            // Default allocations can be created independently on each provider and therefore
            // have different GUID primary keys. Their business key is unique in both schemas;
            // update that row instead of attempting a duplicate insert.
            var sourceUserId = (Guid)Deserialize(
                payload[nameof(LeaveAllocation.UserId)],
                typeof(Guid))!;
            var userId = await ResolveUserIdAsync(
                source,
                target,
                sourceUserId,
                cancellationToken);
            var leaveType = (Domain.Enums.LeaveType)Deserialize(
                payload[nameof(LeaveAllocation.LeaveType)],
                typeof(Domain.Enums.LeaveType))!;
            var year = (int)Deserialize(payload[nameof(LeaveAllocation.Year)], typeof(int))!;
            entity = await target.LeaveAllocations.FirstOrDefaultAsync(
                allocation => allocation.UserId == userId
                              && allocation.LeaveType == leaveType
                              && allocation.Year == year,
                cancellationToken);
            preserveTargetPrimaryKey = entity != null;
        }

        if (entity == null)
        {
            await EnsureRequiredPrincipalsAsync(
                source,
                target,
                entityType,
                payload,
                recoveredPrincipals,
                cancellationToken);
            entity = Activator.CreateInstance(entityType.ClrType)
                ?? throw new InvalidOperationException($"Cannot construct '{message.EntityType}'.");
            target.Add(entity);
        }

        var entry = target.Entry(entity);
        var primaryKeyNames = primaryKey.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var property in entityType.GetProperties())
        {
            if ((!preserveTargetPrimaryKey || !primaryKeyNames.Contains(property.Name))
                && payload.TryGetValue(property.Name, out var value))
            {
                var deserialized = Deserialize(value, property.ClrType);
                var referencesUser = entityType.GetForeignKeys().Any(foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType == typeof(User)
                    && foreignKey.Properties.Contains(property));
                if (referencesUser && deserialized is Guid sourceUserId)
                {
                    deserialized = await ResolveUserIdAsync(
                        source,
                        target,
                        sourceUserId,
                        cancellationToken);
                }

                entry.Property(property.Name).CurrentValue = deserialized;
            }
        }
    }

    private static async Task<User?> FindMatchingUserAsync(
        CompanyEmployeesDbContext target,
        IReadOnlyDictionary<string, JsonElement> payload,
        CancellationToken cancellationToken)
    {
        var normalizedUserName = payload.TryGetValue(nameof(User.NormalizedUserName), out var userName)
            ? Deserialize(userName, typeof(string)) as string
            : null;
        var normalizedEmail = payload.TryGetValue(nameof(User.NormalizedEmail), out var email)
            ? Deserialize(email, typeof(string)) as string
            : null;
        if (normalizedUserName == null && normalizedEmail == null)
            return null;

        return await target.Users.FirstOrDefaultAsync(
            user => (normalizedUserName != null && user.NormalizedUserName == normalizedUserName)
                    || (normalizedEmail != null && user.NormalizedEmail == normalizedEmail),
            cancellationToken);
    }

    private static async Task<Guid> ResolveUserIdAsync(
        CompanyEmployeesDbContext source,
        CompanyEmployeesDbContext target,
        Guid sourceUserId,
        CancellationToken cancellationToken)
    {
        if (await target.Users.FindAsync([sourceUserId], cancellationToken) is { } sameUser)
            return sameUser.Id;

        var sourceUser = await source.Users.FindAsync([sourceUserId], cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cannot map user '{sourceUserId}' because it is missing from the source database.");
        var matchingTarget = await target.Users.FirstOrDefaultAsync(
            user => (sourceUser.NormalizedUserName != null
                     && user.NormalizedUserName == sourceUser.NormalizedUserName)
                    || (sourceUser.NormalizedEmail != null
                        && user.NormalizedEmail == sourceUser.NormalizedEmail),
            cancellationToken);
        return matchingTarget?.Id ?? sourceUserId;
    }

    private static async Task EnsureRequiredPrincipalsAsync(
        CompanyEmployeesDbContext source,
        CompanyEmployeesDbContext target,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType dependentType,
        IReadOnlyDictionary<string, JsonElement> dependentPayload,
        HashSet<string> recoveredPrincipals,
        CancellationToken cancellationToken)
    {
        foreach (var foreignKey in dependentType.GetForeignKeys().Where(key => key.IsRequired))
        {
            var principalKey = foreignKey.PrincipalKey;
            var keyValues = new object?[foreignKey.Properties.Count];
            var hasNull = false;
            for (var index = 0; index < foreignKey.Properties.Count; index++)
            {
                if (!dependentPayload.TryGetValue(foreignKey.Properties[index].Name, out var value)
                    || value.ValueKind == JsonValueKind.Null)
                {
                    hasNull = true;
                    break;
                }

                keyValues[index] = Deserialize(value, principalKey.Properties[index].ClrType);
            }

            if (hasNull || await target.FindAsync(
                    foreignKey.PrincipalEntityType.ClrType,
                    keyValues,
                    cancellationToken) != null)
                continue;

            var recoveryKey = $"{foreignKey.PrincipalEntityType.Name}:{string.Join('|', keyValues)}";
            if (!recoveredPrincipals.Add(recoveryKey))
                continue;

            var sourcePrincipal = await source.FindAsync(
                foreignKey.PrincipalEntityType.ClrType,
                keyValues,
                cancellationToken);
            if (sourcePrincipal == null)
            {
                throw new InvalidOperationException(
                    $"Cannot replicate '{dependentType.Name}' because its required " +
                    $"'{foreignKey.PrincipalEntityType.Name}' row is missing from the source database.");
            }


            if (foreignKey.PrincipalEntityType.ClrType == typeof(User))
            {
                var sourceUser = (User)sourcePrincipal;
                var matchingTarget = await target.Users.FirstOrDefaultAsync(
                    user => (sourceUser.NormalizedUserName != null
                             && user.NormalizedUserName == sourceUser.NormalizedUserName)
                            || (sourceUser.NormalizedEmail != null
                                && user.NormalizedEmail == sourceUser.NormalizedEmail),
                    cancellationToken);
                if (matchingTarget != null)
                    continue;
            }

            var sourceEntry = source.Entry(sourcePrincipal);
            var principalPayload = foreignKey.PrincipalEntityType.GetProperties()
                .ToDictionary(
                    property => property.Name,
                    property => JsonSerializer.SerializeToElement(sourceEntry.Property(property.Name).CurrentValue));

            await EnsureRequiredPrincipalsAsync(
                source,
                target,
                foreignKey.PrincipalEntityType,
                principalPayload,
                recoveredPrincipals,
                cancellationToken);

            var targetPrincipal = Activator.CreateInstance(foreignKey.PrincipalEntityType.ClrType)
                ?? throw new InvalidOperationException(
                    $"Cannot construct '{foreignKey.PrincipalEntityType.Name}'.");
            target.Add(targetPrincipal);
            var targetEntry = target.Entry(targetPrincipal);
            foreach (var property in foreignKey.PrincipalEntityType.GetProperties())
                targetEntry.Property(property.Name).CurrentValue =
                    sourceEntry.Property(property.Name).CurrentValue;
        }
    }

    private static CompanyEmployeesDbContext CreateContext(IDbProviderPlugin plugin, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
        plugin.ConfigureDbContext(builder, connectionString);
        return new CompanyEmployeesDbContext(builder.Options);
    }

    private static object? Deserialize(JsonElement value, Type type) =>
        value.ValueKind == JsonValueKind.Null
            ? null
            : JsonSerializer.Deserialize(value.GetRawText(), type);
}
