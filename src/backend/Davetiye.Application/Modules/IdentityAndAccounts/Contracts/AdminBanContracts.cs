namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public sealed record AdminBanRequest
{
    public required string Reason { get; init; }
    public string? InternalNote { get; init; }
}

public enum AdminBanOutcome
{
    Succeeded,
    AccountNotFound,
    AlreadyBanned,
    InvalidRequest,
    SecurityStampUpdateFailed
}

public sealed record AdminBanResult(AdminBanOutcome Outcome);

public enum AdminUnbanOutcome
{
    Succeeded,
    AccountNotFound,
    NoActiveBan,
    SecurityStampUpdateFailed
}

public sealed record AdminUnbanResult(AdminUnbanOutcome Outcome);

public interface IAdminBanService
{
    Task<AdminBanResult> BanAsync(
        Guid actorIdentityUserId,
        Guid accountId,
        AdminBanRequest request,
        CancellationToken cancellationToken);

    Task<AdminUnbanResult> UnbanAsync(
        Guid actorIdentityUserId,
        Guid accountId,
        CancellationToken cancellationToken);
}
