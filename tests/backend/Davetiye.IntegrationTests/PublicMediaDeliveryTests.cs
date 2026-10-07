using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PublicMediaDeliveryTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_current_snapshot_ready_image_issues_short_lived_capability()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Image);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot);
        var gateway = new FakeGateway(Now.AddSeconds(50));
        var service = CreateService(db, invitations, gateway);

        var result = await service.CreateAsync(invitation.PublicCode, asset.Id, default);

        Assert.Equal("Succeeded", result.Outcome);
        Assert.Equal("image", result.MediaKind);
        Assert.Equal(Now.AddSeconds(50), result.ExpiresAt);
        Assert.Equal(1, gateway.ImageCalls);
        Assert.Equal(2, invitations.Reads);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("expired")]
    [InlineData("banned")]
    [InlineData("trashed")]
    public async Task Closed_current_access_denies_delivery_before_provider_call(string reason)
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Image);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot) { Closed = true, ClosedReason = reason };
        var gateway = new FakeGateway(Now.AddSeconds(50));
        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);
        Assert.Equal("NotFound", result.Outcome);
        Assert.Equal(0, gateway.ImageCalls);
        Assert.Equal(reason, invitations.ClosedReason);
    }

    [Fact]
    public async Task Asset_must_be_in_the_current_published_snapshot()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Image);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot with { Media = [] });
        var gateway = new FakeGateway(Now.AddSeconds(50));
        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);
        Assert.Equal("NotFound", result.Outcome);
        Assert.Equal(0, gateway.ImageCalls);
    }

    [Fact]
    public async Task Access_closing_while_provider_issues_a_session_does_not_return_it()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot);
        var gateway = new FakeGateway(Now.AddMinutes(30)) { OnVideoIssue = () => invitations.Closed = true };
        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);
        Assert.Equal("NotFound", result.Outcome);
        Assert.Equal(1, gateway.VideoCalls);
        Assert.Equal(2, invitations.Reads);
    }

    [Fact]
    public async Task Current_snapshot_changing_while_provider_issues_a_session_does_not_return_the_old_asset()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot);
        var gateway = new FakeGateway(Now.AddMinutes(30))
        {
            OnVideoIssue = () => invitations.CurrentSnapshot = snapshot with { Media = [] },
        };

        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);

        Assert.Equal("NotFound", result.Outcome);
        Assert.Null(result.Url);
        Assert.Equal(1, gateway.VideoCalls);
        Assert.Equal(2, invitations.Reads);
    }

    [Fact]
    public async Task Video_broker_transport_failure_returns_unavailable_without_leaking_an_exception()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot);
        var gateway = new FakeGateway(Now.AddMinutes(30))
        {
            OnVideoIssue = () => throw new HttpRequestException("simulated broker transport failure"),
        };

        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);

        Assert.Equal("Unavailable", result.Outcome);
        Assert.Null(result.Url);
        Assert.Equal(1, invitations.Reads);
        Assert.Equal(1, gateway.VideoCalls);
    }

    [Fact]
    public async Task Video_broker_timeout_returns_unavailable_when_request_token_is_not_cancelled()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        var gateway = new FakeGateway(Now.AddMinutes(30))
        {
            OnVideoIssueAsync = _ => Task.FromException<(Uri Url, DateTimeOffset ExpiresAt)?>(new TaskCanceledException("simulated HttpClient timeout")),
        };

        var result = await CreateService(db, new FakeInvitationService(invitation, snapshot), gateway)
            .CreateAsync(invitation.PublicCode, asset.Id, CancellationToken.None);

        Assert.Equal("Unavailable", result.Outcome);
        Assert.Equal(1, gateway.VideoCalls);
    }

    [Fact]
    public async Task Caller_cancellation_during_video_session_issuance_is_propagated()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        using var cancellation = new CancellationTokenSource();
        var gateway = new FakeGateway(Now.AddMinutes(30))
        {
            OnVideoIssueAsync = _ =>
            {
                cancellation.Cancel();
                return Task.FromException<(Uri Url, DateTimeOffset ExpiresAt)?>(new OperationCanceledException(cancellation.Token));
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(db,
            new FakeInvitationService(invitation, snapshot), gateway)
            .CreateAsync(invitation.PublicCode, asset.Id, cancellation.Token));
        Assert.Equal(1, gateway.VideoCalls);
    }

    [Fact]
    public async Task Concurrent_asset_delete_after_provider_issue_denies_the_new_session()
    {
        var (connection, invitation, asset, snapshot) = await SeedAsync(MediaKind.Video);
        await using var db = Context(connection);
        var invitations = new FakeInvitationService(invitation, snapshot);
        var gateway = new FakeGateway(Now.AddMinutes(30))
        {
            OnVideoIssue = () =>
            {
                db.MediaAssets.Single(item => item.Id == asset.Id).RequestDeletion(Now.AddSeconds(1));
                db.SaveChanges();
            }
        };
        var result = await CreateService(db, invitations, gateway).CreateAsync(invitation.PublicCode, asset.Id, default);
        Assert.Equal("NotFound", result.Outcome);
        Assert.Equal(MediaAssetState.PendingDeletion, (await db.MediaAssets.AsNoTracking().SingleAsync(item => item.Id == asset.Id)).State);
    }

    private async Task<(string Connection, Invitation Invitation, MediaAsset Asset, PublicInvitationActive Snapshot)> SeedAsync(MediaKind kind)
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', PublicInvitationCode.EncodedLength), Now);
        var asset = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, kind, Now);
        var evidence = kind == MediaKind.Image
            ? new NormalizedImageVerificationEvidence("private-object", "image/webp", 128)
            : null;
        asset.BeginProcessing(kind == MediaKind.Image ? "private-object" : "stream-uid");
        if (evidence is not null) asset.MarkReady(evidence, Now.AddSeconds(1));
        else asset.MarkReady(new MediaVerificationEvidence("stream-uid", "video/mp4", 512, 60), Now.AddSeconds(1));
        var role = MediaPresentationRole.Gallery;
        var placement = asset.Place(Guid.NewGuid(), role, 0, Now.AddSeconds(1));
        var media = new[] { new PublicSnapshotMediaPlacement(asset.Id, kind.ToString(), role.ToString(), 0) };
        var published = PublishedContent.Create(Guid.NewGuid(), invitation.Id, "template", 1, 1, "{}", 0,
            Now.AddSeconds(1), System.Text.Json.JsonSerializer.Serialize(media));
        db.Invitations.Add(invitation);
        db.MediaAssets.Add(asset);
        db.MediaPlacements.Add(placement);
        db.PublishedContents.Add(published);
        await db.SaveChangesAsync();
        return (connection, invitation, asset, new PublicInvitationActive("active", "template", 1, 1,
            new PublicInvitationContent(null, null, [], null, null, "Europe/Istanbul", null, []), media));
    }

    private static PublicMediaDeliveryService CreateService(DavetiyeDbContext db, IPublicInvitationService invitations,
        IPrivateMediaDeliveryGateway gateway) => new(db, invitations, gateway, new FixedClock(), Options.Create(new CloudflareMediaOptions
        {
            Enabled = true, MaximumVideoPlaybackSessionSeconds = 1800,
        }));

    private static DavetiyeDbContext Context(string connection) => new(new DbContextOptionsBuilder<DavetiyeDbContext>()
        .UseNpgsql(connection, options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

    private sealed class FakeInvitationService(Invitation invitation, PublicInvitationActive snapshot) : IPublicInvitationService
    {
        public PublicInvitationActive CurrentSnapshot { get; set; } = snapshot;
        public bool Closed { get; set; }
        public string? ClosedReason { get; set; }
        public int Reads { get; private set; }
        public Task<PublicInvitationReadResult> GetAsync(string publicCode, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Closed || publicCode != invitation.PublicCode
                ? new PublicInvitationReadResult(PublicInvitationOutcome.Unavailable)
                : new PublicInvitationReadResult(PublicInvitationOutcome.Active, CurrentSnapshot, invitation.Id));
        }
    }

    private sealed class FakeGateway(DateTimeOffset expiresAt) : IPrivateMediaDeliveryGateway
    {
        public int ImageCalls { get; private set; }
        public int VideoCalls { get; private set; }
        public Action? OnVideoIssue { get; init; }
        public Func<CancellationToken, Task<(Uri Url, DateTimeOffset ExpiresAt)?>>? OnVideoIssueAsync { get; init; }
        public Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateImageCapabilityAsync(Guid assetId, DateTimeOffset expiry, CancellationToken cancellationToken)
        {
            ImageCalls++;
            return Task.FromResult<(Uri Url, DateTimeOffset ExpiresAt)?>(
                (new Uri("https://worker.example.test/image?token=short"), expiresAt));
        }
        public Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateVideoSessionAsync(Guid assetId, DateTimeOffset expiry, CancellationToken cancellationToken)
        {
            VideoCalls++;
            OnVideoIssue?.Invoke();
            if (OnVideoIssueAsync is not null) return OnVideoIssueAsync(cancellationToken);
            return Task.FromResult<(Uri Url, DateTimeOffset ExpiresAt)?>(
                (new Uri("https://customer.cloudflarestream.com/token/iframe"), expiresAt));
        }
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
}
