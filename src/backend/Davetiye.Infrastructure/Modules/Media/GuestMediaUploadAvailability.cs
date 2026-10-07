using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Exposes only provider readiness to application workflows; provider configuration remains owned by Media.</summary>
public sealed class GuestMediaUploadAvailability(IOptions<CloudflareMediaOptions> options) : IGuestMediaUploadAvailability
{
    public bool IsAvailable => options.Value.Enabled;
}
