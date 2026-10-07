using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpAnswerOptionConfiguration : IEntityTypeConfiguration<RsvpAnswerOption>
{
    public void Configure(EntityTypeBuilder<RsvpAnswerOption> builder)
    {
        builder.ToTable("rsvp_answer_options", table =>
            table.HasCheckConstraint("ck_rsvp_answer_options_nonnegative_order_snapshot", "sort_order_snapshot >= 0"));

        builder.HasKey(option => option.Id);
        builder.Property(option => option.LabelSnapshot).HasColumnType("text").IsRequired();
        builder.HasOne<RsvpQuestionOption>()
            .WithMany()
            .HasForeignKey(option => option.OptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasIndex(option => new { option.AnswerId, option.OptionId })
            .HasDatabaseName("ux_rsvp_answer_options_answer_option")
            .IsUnique();
    }
}
