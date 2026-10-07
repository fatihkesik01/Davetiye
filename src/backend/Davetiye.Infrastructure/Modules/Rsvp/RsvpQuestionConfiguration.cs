using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpQuestionConfiguration : IEntityTypeConfiguration<RsvpQuestion>
{
    public void Configure(EntityTypeBuilder<RsvpQuestion> builder)
    {
        builder.ToTable("rsvp_questions", table =>
        {
            table.HasCheckConstraint("ck_rsvp_questions_supported_type", "type IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint(
                "ck_rsvp_questions_participant_count_type",
                "semantic_role IS NULL OR (semantic_role = 1 AND type = 6)");
            table.HasCheckConstraint("ck_rsvp_questions_nonnegative_sort_order", "sort_order >= 0");
            table.HasCheckConstraint(
                "ck_rsvp_questions_prompt_not_blank",
                "length(btrim(prompt)) > 0");
            table.HasCheckConstraint(
                "ck_rsvp_questions_archive_after_creation",
                "archived_at IS NULL OR archived_at >= created_at");
        });

        builder.HasKey(question => question.Id);
        builder.Property(question => question.Prompt).HasColumnType("text").IsRequired();

        builder.HasIndex(question => new { question.ConfigurationId, question.SortOrder })
            .HasDatabaseName("ux_rsvp_questions_active_configuration_order")
            .IsUnique()
            .HasFilter("archived_at IS NULL");
        builder.HasIndex(question => question.ConfigurationId)
            .HasDatabaseName("ux_rsvp_questions_active_participant_count")
            .IsUnique()
            .HasFilter("archived_at IS NULL AND semantic_role = 1");

        builder.HasMany(question => question.Options)
            .WithOne()
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(question => question.Options).HasField("_options");
    }
}
