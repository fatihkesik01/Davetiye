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

        // Supports the claim query's WHERE clause (unprocessed, not permanently failed, due).
        builder.HasIndex(message => new { message.ProcessedAt, message.FailedPermanently, message.NextAttemptAt });
    }
}
