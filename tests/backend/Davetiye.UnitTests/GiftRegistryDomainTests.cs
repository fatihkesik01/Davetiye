using Davetiye.Domain.Modules.GiftRegistry;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class GiftRegistryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gift_item_normalizes_name_and_updates_requested_quantity_and_order()
    {
        var item = GiftItem.Create(Guid.NewGuid(), Guid.NewGuid(), "  Kahve fincanı  ", 6, 0, Now);
        item.Update("Fincan", 8, 2, Now.AddMinutes(1));

        Assert.Equal("Fincan", item.Name);
        Assert.Equal(8, item.RequestedQuantity);
        Assert.Equal(2, item.Ordinal);
        Assert.Equal(1, item.Revision);
    }

    [Fact]
    public void Reservation_requires_full_name_and_accepts_email_and_phone_independently()
    {
        var invitationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var emailOnly = CreateReservation(invitationId, itemId, sessionId, "  Ada Lovelace ", " ada@example.test ", null);
        var phoneOnly = CreateReservation(invitationId, itemId, sessionId, "Ada Lovelace", null, " +90 555 000 0000 ");
        var neither = CreateReservation(invitationId, itemId, sessionId, "Ada Lovelace", null, null);

        Assert.Equal("Ada Lovelace", emailOnly.GuestFullName);
        Assert.Equal("ada@example.test", emailOnly.Email);
        Assert.Null(emailOnly.Phone);
        Assert.Null(phoneOnly.Email);
        Assert.Equal("+90 555 000 0000", phoneOnly.Phone);
        Assert.Null(neither.Email);
        Assert.Null(neither.Phone);
    }

    [Fact]
    public void Reservation_rejects_empty_name_nonpositive_quantity_and_non_utc_time()
    {
        var invitationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => GiftReservation.Create(Guid.NewGuid(), invitationId, itemId,
            sessionId, 1, "  ", null, null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => GiftReservation.Create(Guid.NewGuid(), invitationId, itemId,
            sessionId, 0, "Ada Lovelace", null, null, Now));
        Assert.Throws<ArgumentException>(() => GiftReservation.Create(Guid.NewGuid(), invitationId, itemId,
            sessionId, 1, "Ada Lovelace", null, null, Now.ToOffset(TimeSpan.FromHours(3))));
    }

    [Fact]
    public void Guest_session_stores_a_defensive_copy_of_digest_and_can_be_revoked_without_time_expiry()
    {
        var digest = new byte[GuestGiftSession.HmacSha256DigestLength];
        digest[0] = 31;
        var session = GuestGiftSession.Create(Guid.NewGuid(), Guid.NewGuid(), GuestGiftSession.RequiredPurpose,
            2, digest, Now);
        digest[0] = 90;
        var returnedDigest = session.HmacDigest;
        returnedDigest[0] = 3;
        session.Revoke(Now.AddHours(1));

        Assert.Equal(31, session.HmacDigest[0]);
        Assert.Equal(Now.AddHours(1), session.RevokedAt);
        Assert.Throws<ArgumentException>(() => GuestGiftSession.Create(Guid.NewGuid(), Guid.NewGuid(),
            "wrong-purpose", 2, new byte[32], Now));
        Assert.Throws<ArgumentException>(() => GuestGiftSession.Create(Guid.NewGuid(), Guid.NewGuid(),
            GuestGiftSession.RequiredPurpose, 2, new byte[31], Now));
    }

    private static GiftReservation CreateReservation(Guid invitationId, Guid itemId, Guid sessionId,
        string name, string? email, string? phone) =>
        GiftReservation.Create(Guid.NewGuid(), invitationId, itemId, sessionId, 2, name, email, phone, Now);
}
