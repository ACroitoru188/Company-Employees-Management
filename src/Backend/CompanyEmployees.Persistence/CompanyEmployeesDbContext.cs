using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace CompanyEmployees.Persistence
{
    public class CompanyEmployeesDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
    {
        private readonly DatabaseWriteGate? writeGate;
        private readonly DatabaseRuntimeState? runtimeState;

        public CompanyEmployeesDbContext(
            DbContextOptions<CompanyEmployeesDbContext> options,
            DatabaseWriteGate? writeGate = null,
            DatabaseRuntimeState? runtimeState = null)
        : base(options)
        {
            this.writeGate = writeGate;
            this.runtimeState = runtimeState;
        }

        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<LeaveApproval> LeaveApprovals { get; set; }
        public DbSet<LeaveAllocation> LeaveAllocations { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<ManagerDelegation> ManagerDelegations { get; set; }
        public DbSet<ImpersonationSession> ImpersonationSessions { get; set; }
        public DbSet<DelegatedAction> DelegatedActions { get; set; }
        public DbSet<DatabaseOutboxMessage> DatabaseOutbox { get; set; }

        // Baseline and replication contexts must never generate another outbox event.
        public bool SuppressOutboxCapture { get; set; }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            using var lease = SuppressOutboxCapture ? null : writeGate?.Enter();
            ValidateActiveProvider();
            NormalizePostgreSqlDateTimes();
            var pending = CapturePendingChanges();
            if (pending.Count == 0)
                return base.SaveChanges(acceptAllChangesOnSuccess);

            using var transaction = Database.CurrentTransaction == null
                ? Database.BeginTransaction()
                : null;
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            AddOutboxMessages(pending);
            base.SaveChanges(acceptAllChangesOnSuccess);
            transaction?.Commit();
            return result;
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            using var lease = SuppressOutboxCapture || writeGate == null
                ? null
                : await writeGate.EnterAsync(cancellationToken);
            ValidateActiveProvider();
            NormalizePostgreSqlDateTimes();
            var pending = CapturePendingChanges();
            if (pending.Count == 0)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            await using var transaction = Database.CurrentTransaction == null
                ? await Database.BeginTransactionAsync(cancellationToken)
                : null;
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            AddOutboxMessages(pending);
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }

        private void ValidateActiveProvider()
        {
            if (SuppressOutboxCapture || runtimeState == null)
                return;

            var contextProvider = Database.IsNpgsql()
                ? DatabaseProvider.PostgreSql
                : DatabaseProvider.SqlServer;
            if (contextProvider != runtimeState.ActiveProvider)
            {
                throw new InvalidOperationException(
                    "The active database changed. Reload the application before saving again.");
            }
        }

        private void NormalizePostgreSqlDateTimes()
        {
            if (!Database.IsNpgsql())
                return;

            // SQL Server datetime2 values return with Kind=Unspecified, while Npgsql's
            // timestamp-with-time-zone mapping requires UTC. Normalize during baseline,
            // replication, and ordinary PostgreSQL writes.
            foreach (var entry in ChangeTracker.Entries()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                foreach (var property in entry.Properties)
                {
                    if (property.CurrentValue is not DateTime value)
                        continue;
                    property.CurrentValue = value.Kind switch
                    {
                        DateTimeKind.Utc => value,
                        DateTimeKind.Local => value.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    };
                }
            }
        }

        private List<PendingChange> CapturePendingChanges()
        {
            if (SuppressOutboxCapture)
                return [];

            ChangeTracker.DetectChanges();
            return ChangeTracker.Entries()
                .Where(entry => entry.Entity is not DatabaseOutboxMessage
                    && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new PendingChange(
                    entry.Metadata.Name,
                    entry.State == EntityState.Deleted ? "Delete" : "Upsert",
                    Snapshot(entry, keysOnly: true),
                    entry.State == EntityState.Deleted ? null : Snapshot(entry, keysOnly: false)))
                .ToList();
        }

        private static Dictionary<string, object?> Snapshot(EntityEntry entry, bool keysOnly)
        {
            var keyNames = entry.Metadata.FindPrimaryKey()?.Properties
                .Select(property => property.Name)
                .ToHashSet() ?? [];
            var values = new Dictionary<string, object?>();
            foreach (var property in entry.Properties)
            {
                if (!keysOnly || keyNames.Contains(property.Metadata.Name))
                {
                    values[property.Metadata.Name] = entry.State == EntityState.Deleted
                        ? property.OriginalValue
                        : property.CurrentValue;
                }
            }
            return values;
        }

        private void AddOutboxMessages(IReadOnlyList<PendingChange> pending)
        {
            var batchId = Guid.NewGuid();
            var provider = Database.IsNpgsql() ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer;
            var createdAt = DateTime.UtcNow;
            DatabaseOutbox.AddRange(pending.Select((change, order) => new DatabaseOutboxMessage
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                BatchOrder = order,
                SourceProvider = provider.ToString(),
                EntityType = change.EntityType,
                Operation = change.Operation,
                KeyJson = JsonSerializer.Serialize(change.Key),
                PayloadJson = change.Payload == null ? null : JsonSerializer.Serialize(change.Payload),
                CreatedAtUtc = createdAt
            }));
        }

        private sealed record PendingChange(
            string EntityType,
            string Operation,
            Dictionary<string, object?> Key,
            Dictionary<string, object?>? Payload);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyEmployeesDbContext).Assembly);
        }
    }
}
