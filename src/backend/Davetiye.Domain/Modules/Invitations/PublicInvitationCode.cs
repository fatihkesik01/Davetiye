namespace Davetiye.Domain.Modules.Invitations;

/// <summary>
/// Code-owned public locator format. Sixty-four lowercase hexadecimal characters encode 256 bits
/// of CSPRNG entropy and are URL-safe without exposing an internal identifier.
/// </summary>
public static class PublicInvitationCode
{
    public const int EncodedLength = 64;
    public const int EntropyBytes = 32;

    public static bool IsValid(string? value) =>
        value is { Length: EncodedLength } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static void EnsureValid(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"Public code must be exactly {EncodedLength} lowercase hexadecimal characters.",
                parameterName);
        }
    }
}
