using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpConfigurationConfiguration : IEntityTypeConfiguration<RsvpConfiguration>
{
    public void Configure(EntityTypeBuilder<RsvpConfiguration> builder)
    {
        builder.ToTable("rsvp_configurations", table =>
        {
            table.HasCheckConstraint("ck_rsvp_configurations_revision_nonnegative", "revision >= 0");
            table.HasCheckConstraint("ck_rsvp_configurations_updated_after_created", "updated_at >= created_at");
        });

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Revision).IsConcurrencyToken();
        builder.HasIndex(configuration => configuration.InvitationId)
            .HasDatabaseName("ux_rsvp_configurations_invitation")
            .IsUnique();

        builder.HasMany(configuration => configuration.Questions)
            .WithOne()
            .HasForeignKey(question => question.ConfigurationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(configuration => configuration.Questions).HasField("_questions");
    }
}
