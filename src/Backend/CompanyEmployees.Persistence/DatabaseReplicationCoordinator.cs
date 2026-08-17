using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CompanyEmployees.Persistence;

public sealed class DatabaseReplicationCoordinator(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    ILogger<DatabaseReplicationCoordinator> logger)
{
    private readonly SemaphoreSlim replicationLock = new(1, 1);

    public async Task<int> ReplicateNextBatchAsync(CancellationToken cancellationToken = default)
    {
        var source = state.ActiveProvider;
        var target = source == DatabaseProvider.SqlServer
            ? DatabaseProvider.PostgreSql
            : DatabaseProvider.SqlServer;
        return await ReplicateNextBatchAsync(source, target, cancellationToken);
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
        await using var source = CreateContext(state.ActiveProvider);
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
        DatabaseProvider sourceProvider,
        DatabaseProvider targetProvider,
        CancellationToken cancellationToken)
    {
        await replicationLock.WaitAsync(cancellationToken);
        try
        {
            await using var source = CreateContext(sourceProvider);
            await using var target = CreateContext(targetProvider);
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
                foreach (var message in batch)
                    await ApplyAsync(target, message, cancellationToken);
                await target.SaveChangesAsync(cancellationToken);
                if (target.Database.IsNpgsql())
                    await PostgreSqlStandbySynchronizer.ResetPostgreSqlIdentitySequencesAsync(
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
                foreach (var message in batch)
                {
                    message.AttemptCount++;
                    message.LastError = ex.Message.Length <= 2000
                        ? ex.Message
                        : ex.Message[..2000];
                }
                await source.SaveChangesAsync(cancellationToken);
                state.UpdateReplication(batch.Count, first.CreatedAtUtc, state.LastSynchronizedUtc, ex.Message);
                logger.LogError(
                    ex,
                    "Failed to replicate database outbox batch {BatchId} from {Source} to {Target}.",
                    first.BatchId,
                    sourceProvider,
                    targetProvider);
                throw;
            }
        }
        finally
        {
            replicationLock.Release();
        }
    }

    private static async Task ApplyAsync(
        CompanyEmployeesDbContext target,
        DatabaseOutboxMessage message,
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

        if (message.Operation == "Delete")
        {
            if (entity != null)
                target.Remove(entity);
            return;
        }

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            message.PayloadJson ?? throw new InvalidOperationException("An upsert has no payload."))
            ?? throw new InvalidOperationException("The replicated entity payload is invalid.");
        if (entity == null)
        {
            entity = Activator.CreateInstance(entityType.ClrType)
                ?? throw new InvalidOperationException($"Cannot construct '{message.EntityType}'.");
            target.Add(entity);
        }

        var entry = target.Entry(entity);
        foreach (var property in entityType.GetProperties())
        {
            if (payload.TryGetValue(property.Name, out var value))
                entry.Property(property.Name).CurrentValue = Deserialize(value, property.ClrType);
        }
    }

    private CompanyEmployeesDbContext CreateContext(DatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
        if (provider == DatabaseProvider.PostgreSql)
            builder.UseNpgsql(RequiredConnectionString("PostgreSql"));
        else
            builder.UseSqlServer(RequiredConnectionString("Default"));
        return new CompanyEmployeesDbContext(builder.Options);
    }

    private string RequiredConnectionString(string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.");

    private static object? Deserialize(JsonElement value, Type type) =>
        value.ValueKind == JsonValueKind.Null
            ? null
            : JsonSerializer.Deserialize(value.GetRawText(), type);
}
