using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpQuestionOptionConfiguration : IEntityTypeConfiguration<RsvpQuestionOption>
{
    public void Configure(EntityTypeBuilder<RsvpQuestionOption> builder)
    {
        builder.ToTable("rsvp_question_options", table =>
        {
            table.HasCheckConstraint("ck_rsvp_question_options_nonnegative_sort_order", "sort_order >= 0");
            table.HasCheckConstraint(
                "ck_rsvp_question_options_label_not_blank",
                "length(btrim(label)) > 0");
            table.HasCheckConstraint(
                "ck_rsvp_question_options_archive_after_creation",
                "archived_at IS NULL OR archived_at >= created_at");
        });

        builder.HasKey(option => option.Id);
        builder.Property(option => option.Label).HasColumnType("text").IsRequired();
        builder.HasIndex(option => new { option.QuestionId, option.SortOrder })
            .HasDatabaseName("ux_rsvp_question_options_active_question_order")
            .IsUnique()
            .HasFilter("archived_at IS NULL");
    }
}
