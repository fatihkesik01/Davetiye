using Davetiye.Domain.Modules.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Invitations;

internal sealed class PublishedContentConfiguration : IEntityTypeConfiguration<PublishedContent>
{
    public void Configure(EntityTypeBuilder<PublishedContent> builder)
    {
        builder.HasKey(content => content.Id);

        builder.Property(content => content.TemplateKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(content => content.Content)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(content => content.MediaPlacements)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasOne<Invitation>()
            .WithOne()
            .HasForeignKey<PublishedContent>(content => content.InvitationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_published_contents_renderer_version_positive",
                "renderer_version > 0");
            table.HasCheckConstraint(
                "ck_published_contents_schema_version_positive",
                "content_schema_version > 0");
            table.HasCheckConstraint(
                "ck_published_contents_source_revision_non_negative",
                "source_working_revision >= 0");
            table.HasCheckConstraint(
                "ck_published_contents_update_order",
                "updated_at >= published_at");
        });
    }
}
