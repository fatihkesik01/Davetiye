using Davetiye.Domain.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Media;

internal sealed class MediaProviderEventConfiguration : IEntityTypeConfiguration<MediaProviderEvent>
{
    public void Configure(EntityTypeBuilder<MediaProviderEvent> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.EventFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => item.EventFingerprint).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_media_provider_events_fingerprint", "length(event_fingerprint) = 64"));
    }
}
