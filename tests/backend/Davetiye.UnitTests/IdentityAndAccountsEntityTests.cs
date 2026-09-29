using Davetiye.Domain.Modules.IdentityAndAccounts;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class IdentityAndAccountsEntityTests
{
    [Fact]
    public void Account_Create_rejects_an_empty_identity_user_id()
    {
        Assert.Throws<ArgumentException>(() => Account.Create(
            Guid.NewGuid(),
            Guid.Empty,
            AccountType.Individual,
            "Ada Lovelace",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Account_Create_rejects_a_blank_display_name()
    {
        Assert.Throws<ArgumentException>(() => Account.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AccountType.Individual,
            "   ",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Account_Create_builds_a_valid_account()
    {
        var identityUserId = Guid.NewGuid();

        var account = Account.Create(
            Guid.NewGuid(),
            identityUserId,
            AccountType.Organization,
            "  Acme Events  ",
            DateTimeOffset.UtcNow);

        Assert.Equal(identityUserId, account.IdentityUserId);
        Assert.Equal(AccountType.Organization, account.AccountType);
        Assert.Equal("Acme Events", account.DisplayName);
    }

    [Fact]
    public void BanRecord_Revoke_is_rejected_once_already_revoked()
    {
        var banRecord = BanRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Abuse report",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        banRecord.Revoke(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => banRecord.Revoke(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BanRecord_Create_rejects_a_blank_reason()
    {
        Assert.Throws<ArgumentException>(() => BanRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            string.Empty,
            DateTimeOffset.UtcNow,
            Guid.NewGuid()));
    }
}
