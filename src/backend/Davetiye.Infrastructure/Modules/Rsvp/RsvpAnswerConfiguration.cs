using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpAnswerConfiguration : IEntityTypeConfiguration<RsvpAnswer>
{
    public void Configure(EntityTypeBuilder<RsvpAnswer> builder)
    {
        builder.ToTable("rsvp_answers", table =>
        {
            table.HasCheckConstraint("ck_rsvp_answers_supported_type", "question_type_snapshot IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint(
                "ck_rsvp_answers_semantic_snapshot_type",
                "semantic_role_snapshot IS NULL OR (semantic_role_snapshot = 1 AND question_type_snapshot = 6)");
            table.HasCheckConstraint(
                "ck_rsvp_answers_typed_value_pairing",
                "(question_type_snapshot IN (1, 2) AND text_value IS NOT NULL AND number_value IS NULL AND boolean_value IS NULL) OR " +
                "(question_type_snapshot IN (3, 4) AND text_value IS NULL AND number_value IS NULL AND boolean_value IS NULL) OR " +
                "(question_type_snapshot = 5 AND text_value IS NULL AND number_value IS NULL AND boolean_value IS NOT NULL) OR " +
                "(question_type_snapshot = 6 AND text_value IS NULL AND number_value IS NOT NULL AND boolean_value IS NULL)");
        });

        builder.HasKey(answer => answer.Id);
        builder.Property(answer => answer.QuestionPromptSnapshot).HasColumnType("text").IsRequired();
        builder.Property(answer => answer.TextValue).HasColumnType("text");
        builder.Property(answer => answer.NumberValue).HasColumnType("numeric");
        builder.HasIndex(answer => new { answer.SubmissionId, answer.QuestionId })
            .HasDatabaseName("ux_rsvp_answers_submission_question")
            .IsUnique();

        // Question identity is retained as historical snapshot metadata. The answer is deliberately
        // not FK-coupled to the editable question row; snapshot columns remain authoritative.
        builder.HasMany(answer => answer.SelectedOptions)
            .WithOne()
            .HasForeignKey(option => option.AnswerId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(answer => answer.SelectedOptions).HasField("_selectedOptions");
    }
}
