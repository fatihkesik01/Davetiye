namespace Davetiye.Application.Modules.Invitations.Contracts;

public interface IInvitationDraftService
{
    Task<InvitationDraftPage> ListAsync(
        Guid accountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<InvitationDraftResult> CreateAsync(
        Guid accountId,
        CreateInvitationDraftRequest request,
        CancellationToken cancellationToken);

    Task<InvitationDraftResult> GetAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken);

    Task<InvitationDraftValidationReport?> GetValidationAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken);

    Task<InvitationDraftResult> AutosaveAsync(
        Guid accountId,
        Guid invitationId,
        AutosaveInvitationDraftRequest request,
        CancellationToken cancellationToken);

    Task<InvitationDraftResult> SelectTemplateAsync(
        Guid accountId,
        Guid invitationId,
        SelectInvitationTemplateRequest request,
        CancellationToken cancellationToken);
}
