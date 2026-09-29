using Davetiye.Domain.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Enforces "at most one Account per IdentityUser" as a real DB constraint (a unique index on the
/// FK, which EF Core also creates automatically for a required one-to-one relationship) — not just
/// an application-level check, which would race under concurrent requests.
/// </summary>
internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(account => account.Id);

        builder.Property(account => account.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(account => account.AccountType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(account => account.IdentityUserId)
            .IsUnique();

        // Same-module reference (ApplicationUser also lives under the IdentityAndAccounts module,
        // just in Infrastructure rather than Domain), so this is not a cross-module EF navigation.
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Account>(account => account.IdentityUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
