using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

/// <summary>Public projection intentionally excludes every reservation and guest identity field.</summary>
public sealed class PublicGiftRegistryService(DavetiyeDbContext dbContext,
    IGiftGuestInvitationAccessReader invitationAccess, IEffectiveEntitlementResolver entitlements,
    IOptions<GiftCapabilityOptions> capabilityOptions, IClock clock) : IPublicGiftRegistryService
{
    public async Task<PublicGiftRegistryResult> GetAsync(string publicCode, CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicGiftRegistryOutcome.NotFound);
        var result = await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.GiftReservation), cancellationToken);
        if (!result.IsGranted || result.Entitlements?.GiftRegistryEnabled != true)
            return new(PublicGiftRegistryOutcome.NotFound);
        var items = await dbContext.GiftItems.AsNoTracking().Where(item => item.InvitationId == access.InvitationId)
            .OrderBy(item => item.Ordinal).Select(item => new { item.Id, item.Name, item.RequestedQuantity, item.Ordinal })
            .ToListAsync(cancellationToken);
        var quantities = await dbContext.GiftReservations.AsNoTracking().Where(value => value.InvitationId == access.InvitationId)
            .GroupBy(value => value.GiftItemId).Select(group => new { Id = group.Key, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);
        return new(PublicGiftRegistryOutcome.Available, new PublicGiftRegistry(items.Select(item =>
            new PublicGiftRegistryItem(item.Id, item.Name, item.RequestedQuantity,
                Math.Max(0, item.RequestedQuantity - quantities.GetValueOrDefault(item.Id)), item.Ordinal)).ToArray()));
    }

    public async Task<PublicGiftReservationResult> ReserveAsync(string publicCode, ReservePublicGiftRequest request,
        string? sessionToken, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email?.Trim();
        if (request.ItemId == Guid.Empty || request.Quantity <= 0 || string.IsNullOrWhiteSpace(request.FullName) ||
            request.FullName.Trim().Length > 200 || normalizedEmail is { Length: < 3 or > 320 } || request.Phone?.Trim().Length > 32)
            return Invalid("reservation", "Enter a name, valid quantity, and contact details within the allowed lengths.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicGiftRegistryOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return new(PublicGiftRegistryOutcome.NotFound);
        var entitlement = await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.GiftReservation), cancellationToken);
        if (!entitlement.IsGranted || entitlement.Entitlements?.GiftRegistryEnabled != true)
            return new(PublicGiftRegistryOutcome.NotFound);
        var item = await dbContext.GiftItems.SingleOrDefaultAsync(value => value.Id == request.ItemId && value.InvitationId == access.InvitationId, cancellationToken);
        if (item is null) return new(PublicGiftRegistryOutcome.NotFound);
        var reserved = await dbContext.GiftReservations.Where(value => value.GiftItemId == item.Id).SumAsync(value => (int?)value.Quantity, cancellationToken) ?? 0;
        if (request.Quantity > item.RequestedQuantity - reserved) return Invalid("quantity", "Requested quantity exceeds the remaining amount.");
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var token = TryDecodeToken(sessionToken, out _) ? sessionToken! : string.Empty;
        var session = await FindSessionAsync(access.InvitationId, key, version, token, now, cancellationToken);
        if (session is null)
        {
            // A revoked capability digest is retained under a unique index. Always rotate on a miss,
            // including a syntactically valid stale cookie, instead of retrying its old digest.
            token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            session = GuestGiftSession.Create(Guid.NewGuid(), access.InvitationId, GuestGiftSession.RequiredPurpose, version,
                ComputeDigest(key, access.InvitationId, token), now);
            dbContext.GuestGiftSessions.Add(session);
        }
        var reservation = GiftReservation.Create(Guid.NewGuid(), access.InvitationId, item.Id, session.Id, request.Quantity,
            request.FullName!, request.Email, request.Phone, now);
        dbContext.GiftReservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicGiftRegistryOutcome.Available, reservation.Id, token, access.WindowEndsAt);
    }

    public async Task<PublicGiftGuestReservationsResult> ListMyReservationsAsync(string publicCode, string? sessionToken,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null || !TryDecodeToken(sessionToken, out _)) return new(PublicGiftRegistryOutcome.NotFound);
        var entitlement = await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.GiftReservation), cancellationToken);
        if (!entitlement.IsGranted || entitlement.Entitlements?.GiftRegistryEnabled != true)
            return new(PublicGiftRegistryOutcome.NotFound);
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var session = await FindSessionAsync(access.InvitationId, key, version, sessionToken!, clock.UtcNow.ToUniversalTime(), cancellationToken);
        if (session is null) return new(PublicGiftRegistryOutcome.NotFound);
        var reservations = await (from reservation in dbContext.GiftReservations.AsNoTracking()
                                  join item in dbContext.GiftItems.AsNoTracking() on reservation.GiftItemId equals item.Id
                                  where reservation.InvitationId == access.InvitationId && reservation.GuestGiftSessionId == session.Id
                                  orderby reservation.CreatedAt
                                  select new PublicGiftGuestReservation(reservation.Id, item.Id, item.Name, reservation.Quantity))
            .ToListAsync(cancellationToken);
        return new(PublicGiftRegistryOutcome.Available, reservations);
    }

    public async Task<PublicGiftRegistryOutcome> CancelAsync(string publicCode, Guid reservationId, string? sessionToken,
        CancellationToken cancellationToken)
    {
        if (reservationId == Guid.Empty || !TryDecodeToken(sessionToken, out _)) return PublicGiftRegistryOutcome.NotFound;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return PublicGiftRegistryOutcome.NotFound;
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var now = clock.UtcNow.ToUniversalTime();
        var session = await FindSessionAsync(access.InvitationId, key, version, sessionToken!, now, cancellationToken);
        if (session is null) return PublicGiftRegistryOutcome.NotFound;
        var reservation = await dbContext.GiftReservations.SingleOrDefaultAsync(value => value.Id == reservationId &&
            value.InvitationId == access.InvitationId && value.GuestGiftSessionId == session.Id, cancellationToken);
        if (reservation is null) return PublicGiftRegistryOutcome.NotFound;
        dbContext.GiftReservations.Remove(reservation);
        if (!await dbContext.GiftReservations.AnyAsync(value => value.GuestGiftSessionId == session.Id && value.Id != reservationId, cancellationToken))
            session.Revoke(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PublicGiftRegistryOutcome.Available;
    }

    private async Task<GuestGiftSession?> FindSessionAsync(Guid invitationId, byte[] key, int version, string token,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!TryDecodeToken(token, out _)) return null;
        var candidates = await dbContext.GuestGiftSessions.Where(value => value.InvitationId == invitationId &&
            value.Purpose == GuestGiftSession.RequiredPurpose && value.HmacKeyVersion == version && value.RevokedAt == null)
            .ToListAsync(cancellationToken);
        return candidates.SingleOrDefault(value => CryptographicOperations.FixedTimeEquals(value.HmacDigest,
            ComputeDigest(key, invitationId, token)));
    }

    private static (byte[] Key, int Version) ReadSigningKey(GiftCapabilityOptions options)
    {
        if (options.HmacKeyVersion <= 0 || string.IsNullOrWhiteSpace(options.HmacKeyBase64))
            throw new InvalidOperationException("GiftCapabilities:HmacKeyBase64 and a positive HmacKeyVersion are required.");
        byte[] key;
        try { key = Convert.FromBase64String(options.HmacKeyBase64); }
        catch (FormatException exception) { throw new InvalidOperationException("GiftCapabilities:HmacKeyBase64 must be base64.", exception); }
        if (key.Length < 32) throw new InvalidOperationException("Gift capability HMAC key must contain at least 256 bits.");
        return (key, options.HmacKeyVersion);
    }

    private static byte[] ComputeDigest(byte[] key, Guid invitationId, string token) => HMACSHA256.HashData(key,
        Encoding.UTF8.GetBytes($"{GuestGiftSession.RequiredPurpose}\\n{invitationId:D}\\n{token}"));

    private static bool TryDecodeToken(string? token, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(token)) return false;
        try { bytes = WebEncoders.Base64UrlDecode(token); return bytes.Length == 32 && WebEncoders.Base64UrlEncode(bytes) == token; }
        catch (FormatException) { return false; }
    }

    private static PublicGiftReservationResult Invalid(string key, string message) => new(PublicGiftRegistryOutcome.Invalid,
        Errors: new Dictionary<string, string[]> { [key] = [message] });
}

public sealed class GiftCapabilityOptions
{
    public const string SectionName = "GiftCapabilities";
    public string HmacKeyBase64 { get; init; } = string.Empty;
    public int HmacKeyVersion { get; init; } = 1;
}
