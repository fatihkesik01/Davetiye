using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpSubmissionConfiguration : IEntityTypeConfiguration<RsvpSubmission>
{
    public void Configure(EntityTypeBuilder<RsvpSubmission> builder)
    {
        builder.ToTable("rsvp_submissions", table =>
        {
            table.HasCheckConstraint("ck_rsvp_submissions_revision_nonnegative", "revision >= 0");
            table.HasCheckConstraint("ck_rsvp_submissions_updated_after_submitted", "updated_at >= submitted_at");
        });

        builder.HasKey(submission => submission.Id);
        builder.HasIndex(submission => new { submission.InvitationId, submission.SubmittedAt })
            .HasDatabaseName("ix_rsvp_submissions_invitation_submitted_at");
        builder.HasMany(submission => submission.Answers)
            .WithOne()
            .HasForeignKey(answer => answer.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(submission => submission.Answers).HasField("_answers");
    }
}
