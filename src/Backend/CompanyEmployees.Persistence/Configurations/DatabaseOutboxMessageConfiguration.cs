using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations;

internal sealed class DatabaseOutboxMessageConfiguration
    : IEntityTypeConfiguration<DatabaseOutboxMessage>
{
    public void Configure(EntityTypeBuilder<DatabaseOutboxMessage> builder)
    {
        // The table is bootstrapped with provider-specific idempotent SQL so an existing
        // PostgreSQL standby can be upgraded without replaying SQL Server migrations.
        builder.ToTable("DatabaseOutbox", table => table.ExcludeFromMigrations());
        builder.HasKey(message => message.Id);
        builder.Property(message => message.SourceProvider).HasMaxLength(32).IsRequired();
        builder.Property(message => message.EntityType).HasMaxLength(512).IsRequired();
        builder.Property(message => message.Operation).HasMaxLength(16).IsRequired();
        builder.Property(message => message.KeyJson).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.CreatedAtUtc });
        builder.HasIndex(message => new { message.BatchId, message.BatchOrder });
    }
}
