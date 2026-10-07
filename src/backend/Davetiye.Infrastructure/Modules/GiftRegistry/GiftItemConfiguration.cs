using Davetiye.Domain.Modules.GiftRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

internal sealed class GiftItemConfiguration : IEntityTypeConfiguration<GiftItem>
{
    public void Configure(EntityTypeBuilder<GiftItem> builder)
    {
        builder.ToTable("gift_items", table =>
        {
            table.HasCheckConstraint("ck_gift_items_name", "char_length(name) BETWEEN 1 AND 200 AND btrim(name) <> ''");
            table.HasCheckConstraint("ck_gift_items_requested_quantity_positive", "requested_quantity > 0");
            table.HasCheckConstraint("ck_gift_items_ordinal_nonnegative", "ordinal >= 0");
            table.HasCheckConstraint("ck_gift_items_revision_nonnegative", "revision >= 0");
            table.HasCheckConstraint("ck_gift_items_updated_after_created", "updated_at >= created_at");
        });

        builder.HasKey(item => item.Id);
        builder.HasAlternateKey(item => new { item.Id, item.InvitationId })
            .HasName("ak_gift_items_id_invitation");
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Revision).IsConcurrencyToken();
        builder.HasIndex(item => new { item.InvitationId, item.Ordinal })
            .HasDatabaseName("ux_gift_items_invitation_ordinal").IsUnique();
    }
}
