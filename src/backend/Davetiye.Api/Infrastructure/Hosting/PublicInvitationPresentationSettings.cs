namespace Davetiye.Api.Infrastructure.Hosting;

/// <summary>Validated operator settings supplied by the composition root, never request Host.</summary>
public sealed record PublicInvitationPresentationSettings(string BaseUrl, string AppShellUrl);
