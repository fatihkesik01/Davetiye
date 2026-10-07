namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public interface IAccountDeletionService
{
    Task<AccountDeletionRequestOutcome> RequestAsync(Guid accountId, CancellationToken cancellationToken);

    Task<AccountDeletionConfirmationOutcome> ConfirmAsync(string token, CancellationToken cancellationToken);
}

public enum AccountDeletionRequestOutcome
{
    Accepted,
    AccountUnavailable,
}

public enum AccountDeletionConfirmationOutcome
{
    Confirmed,
    InvalidOrExpiredToken,
}

public sealed record ConfirmAccountDeletionRequest(string Token);
