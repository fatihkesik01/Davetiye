using Davetiye.Domain.Modules.IntegrationFoundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(message => message.Id);

        builder.Property(message => message.MessageType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(message => message.Payload)
            .IsRequired();

        // Scalar-only by ADR-0001: the generic Integration Foundation queue does not own Accounts.
        builder.HasIndex(message => new { message.OwnerAccountId, message.ProcessedAt })
            .HasDatabaseName("ix_outbox_messages_owner_account_processed")
            .HasFilter("owner_account_id IS NOT NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_outbox_messages_dispatch_started_range",
                "dispatch_started_at_utc IS NULL OR dispatch_started_at_utc >= created_at");
            table.HasCheckConstraint("ck_outbox_messages_dispatch_processed_order",
                "processed_at IS NULL OR dispatch_started_at_utc IS NULL OR processed_at >= dispatch_started_at_utc");
        });

        // Supports the claim query's WHERE clause (unprocessed, not permanently failed, due).
        builder.HasIndex(message => new { message.ProcessedAt, message.FailedPermanently, message.NextAttemptAt });
    }
}
