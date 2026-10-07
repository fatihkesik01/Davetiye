namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>Limits password-reset requests per normalized destination, independently of source IP.</summary>
public interface IPasswordResetDestinationRateLimiter
{
    bool TryAcquire(string? email);
}
