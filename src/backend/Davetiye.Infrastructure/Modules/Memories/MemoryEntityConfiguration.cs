using Davetiye.Domain.Modules.Memories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Memories;

internal sealed class MemoryEntityConfiguration : IEntityTypeConfiguration<Memory>
{
    public void Configure(EntityTypeBuilder<Memory> builder)
    {
        builder.ToTable("memories", table =>
        {
            table.HasCheckConstraint("ck_memories_state",
                "state IN ('PendingMedia', 'Published', 'Hidden', 'Abandoned')");
            table.HasCheckConstraint("ck_memories_display_name_length",
                "display_name IS NULL OR (char_length(display_name) BETWEEN 1 AND 60 AND btrim(display_name) <> '')");
            table.HasCheckConstraint("ck_memories_text_length",
                "text IS NULL OR (char_length(text) BETWEEN 1 AND 500 AND btrim(text) <> '')");
            table.HasCheckConstraint("ck_memories_emoji_length",
                "emoji IS NULL OR (char_length(emoji) BETWEEN 1 AND 32 AND btrim(emoji) <> '')");
            table.HasCheckConstraint("ck_memories_finalized_state",
                "(state IN ('Published', 'Hidden')) = (finalized_at IS NOT NULL)");
            table.HasCheckConstraint("ck_memories_hidden_state",
                "(state = 'Hidden') = (hidden_at IS NOT NULL)");
            table.HasCheckConstraint("ck_memories_timestamps_ordered",
                "(finalized_at IS NULL OR finalized_at >= created_at) AND (hidden_at IS NULL OR hidden_at >= finalized_at)");
            table.HasCheckConstraint("ck_memories_revision_nonnegative", "revision >= 0");
        });

        builder.HasKey(memory => memory.Id);
        builder.Property(memory => memory.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(memory => memory.DisplayName).HasMaxLength(60);
        builder.Property(memory => memory.Text).HasMaxLength(500);
        builder.Property(memory => memory.Emoji).HasMaxLength(32);
        builder.Property(memory => memory.Revision).IsConcurrencyToken();
        builder.HasIndex(memory => new { memory.InvitationId, memory.State, memory.CreatedAt })
            .HasDatabaseName("ix_memories_invitation_state_created_at");
        builder.HasMany(memory => memory.Media)
            .WithOne()
            .HasForeignKey(media => media.MemoryId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(memory => memory.Media).HasField("_media");
    }
}
