namespace CompanyEmployees.Persistence.Configurations;

using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class MessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body)
            .IsRequired()
            .HasMaxLength(2000);

        // NoAction on both: a user is soft-deleted (Status = Inactive), never removed, and two
        // cascade paths into the same table would be a multiple-cascade-path error on SQL
        // Server anyway.
        builder.HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(m => m.Recipient)
            .WithMany()
            .HasForeignKey(m => m.RecipientId)
            .OnDelete(DeleteBehavior.NoAction);

        // Every read is "the thread between these two, newest first", so the pair leads and
        // the timestamp orders within it.
        builder.HasIndex(m => new { m.SenderId, m.RecipientId, m.CreatedAt });

        // The unread count reads the other direction: what was sent to me and not opened.
        builder.HasIndex(m => new { m.RecipientId, m.ReadAt });
    }
}
