using Davetiye.Domain.Modules.IntegrationFoundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

/// <summary>
/// Enforces the inbox's replay-protection guarantee as a real DB constraint: a unique index on
/// (ProviderName, ProviderEventId) rejects inserting the same provider event twice
/// (Postgres 23505 unique_violation) — not just an application-level check, which would race under
/// concurrent requests.
/// </summary>
internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.HasKey(message => message.Id);

        builder.Property(message => message.ProviderName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(message => message.ProviderEventId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(message => message.Payload)
            .IsRequired();

        builder.HasIndex(message => new { message.ProviderName, message.ProviderEventId })
            .IsUnique();

        // Supports the claim query's WHERE clause (unprocessed, not permanently failed, due).
        builder.HasIndex(message => new { message.ProcessedAt, message.FailedPermanently, message.NextAttemptAt });
    }
}
