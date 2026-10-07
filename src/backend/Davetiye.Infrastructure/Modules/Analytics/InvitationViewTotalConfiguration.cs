using Davetiye.Domain.Modules.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Analytics;

internal sealed class InvitationViewTotalConfiguration : IEntityTypeConfiguration<InvitationViewTotal>
{
    public void Configure(EntityTypeBuilder<InvitationViewTotal> builder)
    {
        builder.HasKey(total => total.InvitationId);
        builder.Property(total => total.Total).IsRequired();
        builder.ToTable("invitation_view_totals", table => table.HasCheckConstraint("ck_invitation_view_totals_nonnegative", "total >= 0"));
    }
}
