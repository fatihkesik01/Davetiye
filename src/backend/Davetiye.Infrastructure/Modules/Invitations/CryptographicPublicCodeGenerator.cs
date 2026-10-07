using System.Security.Cryptography;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Invitations;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class CryptographicPublicCodeGenerator : IPublicCodeGenerator
{
    public string Generate()
    {
        Span<byte> entropy = stackalloc byte[PublicInvitationCode.EntropyBytes];
        RandomNumberGenerator.Fill(entropy);
        return Convert.ToHexString(entropy).ToLowerInvariant();
    }
}
