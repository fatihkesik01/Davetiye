using Davetiye.Domain.Modules.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Administration;

internal sealed class AdminAuditRecordConfiguration : IEntityTypeConfiguration<AdminAuditRecord>
{
    public void Configure(EntityTypeBuilder<AdminAuditRecord> builder)
    {
        builder.HasKey(record => record.Id);

        builder.Property(record => record.EventType)
            .HasMaxLength(AdminAuditRecord.MaxEventTypeLength)
            .IsRequired();

        // No actor or subject FK: audit survives removal of the referenced identity/resource.
        builder.HasIndex(record => new { record.SubjectId, record.OccurredAtUtc })
            .HasDatabaseName("ix_admin_audit_records_subject_time");
        builder.HasIndex(record => new { record.ActorId, record.OccurredAtUtc })
            .HasDatabaseName("ix_admin_audit_records_actor_time");
        builder.HasIndex(record => new { record.OccurredAtUtc, record.Id })
            .HasDatabaseName("ix_admin_audit_records_time_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_admin_audit_records_event_type_nonblank",
            "length(btrim(event_type)) BETWEEN 1 AND 100"));
    }
}
