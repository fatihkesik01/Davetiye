using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

/// <summary>
/// Creator-only, Draft-only invitation commands. The current Account id is resolved from the
/// authenticated server principal for every request; no account or owner identifier is accepted
/// from a caller. This is deliberately a private management surface, not a public invitation URL.
/// </summary>
public static class InvitationDraftEndpoints
{
    public static IEndpointRouteBuilder MapInvitationDraftEndpoints(this IEndpointRouteBuilder apiV1Group)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);

        // The same production HTTPS gate as auth endpoints applies to Creator-owned data. Health
        // checks remain outside this group, while every unsafe cookie-authenticated command also
        // validates the antiforgery header/cookie pair.
        var group = apiV1Group
            .MapGroup("/invitations")
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        group.MapGet("", ListAsync)
            .Produces<InvitationDraftPage>();
        group.MapGet("/{invitationId:guid}", GetAsync)
            .Produces<InvitationDraftDetails>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{invitationId:guid}/validation", GetValidationAsync)
            .Produces<InvitationDraftValidationReport>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .Produces<InvitationDraftDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{invitationId:guid}/draft", AutosaveAsync)
            .Produces<InvitationDraftDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{invitationId:guid}/template", SelectTemplateAsync)
            .Produces<InvitationDraftDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return apiV1Group;
    }

    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var requestedPage = page ?? 1;
        var requestedPageSize = pageSize ?? InvitationDraftContract.DefaultPageSize;
        var invalidPageSize = requestedPageSize < 1 ||
            requestedPageSize > InvitationDraftContract.MaxPageSize;
        var invalidPage = requestedPage < 1 ||
            (!invalidPageSize && requestedPage > int.MaxValue / requestedPageSize);
        if (invalidPage || invalidPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = invalidPage
                    ? ["Page is outside the supported range."]
                    : [],
                ["pageSize"] = invalidPageSize
                    ? [$"Page size must be between 1 and {InvitationDraftContract.MaxPageSize}."]
                    : [],
            }.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        var pageResult = await invitationDraftService.ListAsync(
            accountId.Value, requestedPage, requestedPageSize, cancellationToken);
        return Results.Ok(pageResult);
    }

    private static async Task<IResult> GetAsync(
        Guid invitationId,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var result = await invitationDraftService.GetAsync(accountId.Value, invitationId, cancellationToken);
        return ToResult(result);
    }

    private static async Task<IResult> CreateAsync(
        CreateInvitationDraftRequest request,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var result = await invitationDraftService.CreateAsync(accountId.Value, request, cancellationToken);
        return result.Outcome == InvitationDraftOutcome.Succeeded
            ? Results.Created($"/api/v1/invitations/{result.Draft!.Id}", result.Draft)
            : ToResult(result);
    }

    private static async Task<IResult> GetValidationAsync(
        Guid invitationId,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var report = await invitationDraftService.GetValidationAsync(
            accountId.Value, invitationId, cancellationToken);
        return report is null ? Results.NotFound() : Results.Ok(report);
    }

    private static async Task<IResult> AutosaveAsync(
        Guid invitationId,
        AutosaveInvitationDraftRequest request,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var result = await invitationDraftService.AutosaveAsync(
            accountId.Value, invitationId, request, cancellationToken);
        return ToResult(result);
    }

    private static async Task<IResult> SelectTemplateAsync(
        Guid invitationId,
        SelectInvitationTemplateRequest request,
        HttpContext httpContext,
        [FromServices] ICurrentAccountResolver currentAccountResolver,
        [FromServices] IInvitationDraftService invitationDraftService,
        CancellationToken cancellationToken)
    {
        var accountId = await ResolveCurrentAccountIdAsync(httpContext, currentAccountResolver, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var result = await invitationDraftService.SelectTemplateAsync(
            accountId.Value, invitationId, request, cancellationToken);
        return ToResult(result);
    }

    private static async Task<Guid?> ResolveCurrentAccountIdAsync(
        HttpContext httpContext,
        ICurrentAccountResolver currentAccountResolver,
        CancellationToken cancellationToken)
    {
        // Draft responses can contain personal event details, so authenticated management data is
        // never eligible for browser/proxy storage.
        httpContext.Response.Headers.CacheControl = "no-store";
        var identityUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identityUserId, out var userId)
            ? await currentAccountResolver.ResolveAccountIdAsync(userId, cancellationToken)
            : null;
    }

    private static IResult ToResult(InvitationDraftResult result) => result.Outcome switch
    {
        InvitationDraftOutcome.Succeeded => Results.Ok(result.Draft),
        InvitationDraftOutcome.NotFound => Results.NotFound(),
        InvitationDraftOutcome.Invalid => Results.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()),
        InvitationDraftOutcome.TemplateUnavailable => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Template is unavailable."),
        InvitationDraftOutcome.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The draft was updated elsewhere.",
            extensions: new Dictionary<string, object?>
            {
                ["currentInvitationRevision"] = result.CurrentInvitationRevision,
                ["currentContentRevision"] = result.CurrentContentRevision,
            }),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
