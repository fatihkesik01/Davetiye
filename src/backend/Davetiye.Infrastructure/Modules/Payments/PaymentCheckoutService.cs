using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class PaymentCheckoutService(DavetiyeDbContext db, IPaymentGateway gateway,
    IPaymentAccountEligibilityReader accountEligibility,
    IPaymentInvitationEligibilityReader invitationEligibility,
    IPaymentPlanCatalogReader planCatalog,
    IOutermostAccountQuotaTransactionRunner accountLock,
    IOptions<PublicWebOptions> publicWebOptions, Davetiye.Domain.Modules.SharedKernel.IClock clock) : IPaymentCheckoutService
{
    public async Task<IndividualPurchasePlanCatalog> ListPlansAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var isIndividual = await accountEligibility.IsIndividualCreatorAsync(accountId, cancellationToken);
        if (!isIndividual) return new(false, []);
        var plans = await planCatalog.ListIndividualPlansAsync(cancellationToken);
        return new(true, plans);
    }

    public async Task<PaymentCheckoutResult> StartAsync(Guid accountId, StartPaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(accountId, request);
        if (validation.Count != 0) return Invalid(validation);

        var planKey = request.PlanKey.Trim().ToLowerInvariant();
        var idempotencyKey = request.IdempotencyKey.Trim();
        PaymentAttempt? attempt = null;
        (PaymentCheckoutOutcome Outcome, PaymentCheckoutResponse? Response, bool Created) admission;
        try
        {
            admission = await accountLock.ExecuteAndCommitAsync(accountId, async token =>
            {
                if (!await accountEligibility.IsIndividualCreatorAsync(accountId, token))
                    return (PaymentCheckoutOutcome.NotEligible, (PaymentCheckoutResponse?)null, false);
                if (!await invitationEligibility.IsOwnedAndAvailableAsync(accountId, request.InvitationId, token))
                    return (PaymentCheckoutOutcome.NotFound, (PaymentCheckoutResponse?)null, false);

                var existing = await db.PaymentAttempts.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.AccountId == accountId && item.InvitationId == request.InvitationId &&
                        item.IdempotencyKey == idempotencyKey, token);
                if (existing is not null)
                    return existing.PlanKey != planKey
                        ? (PaymentCheckoutOutcome.Conflict, (PaymentCheckoutResponse?)null, false)
                        : (ReplayOutcome(existing), ToResponse(existing), false);

                var plan = await planCatalog.FindIndividualPlanAsync(planKey, token);
                if (plan is null)
                    return (PaymentCheckoutOutcome.Invalid,
                        (PaymentCheckoutResponse?)null, false);
                if (await db.PaymentAttempts.AsNoTracking().AnyAsync(item => item.AccountId == accountId &&
                    item.InvitationId == request.InvitationId &&
                    (item.Status == PaymentAttemptStatus.Pending || item.Status == PaymentAttemptStatus.Unknown), token))
                    return (PaymentCheckoutOutcome.Conflict, (PaymentCheckoutResponse?)null, false);
                if (await planCatalog.HasActivePaidGrantAsync(accountId, request.InvitationId, token))
                    return (PaymentCheckoutOutcome.Conflict, (PaymentCheckoutResponse?)null, false);

                var now = clock.UtcNow.ToUniversalTime();
                var reference = $"dv{Guid.NewGuid():N}";
                attempt = PaymentAttempt.Create(Guid.NewGuid(), accountId, request.InvitationId, plan.PlanId, plan.Key,
                    plan.Amount, plan.Currency, idempotencyKey, reference, now, MapBillingKind(plan.BillingKind));
                db.PaymentAttempts.Add(attempt);
                await db.SaveChangesAsync(token);
                return (PaymentCheckoutOutcome.Created, (PaymentCheckoutResponse?)null, true);
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            var replay = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(item => item.AccountId == accountId &&
                item.InvitationId == request.InvitationId && item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (replay is not null && replay.PlanKey == planKey)
                return new(ReplayOutcome(replay), ToResponse(replay));
            return new(PaymentCheckoutOutcome.Conflict);
        }

        if (!admission.Created)
        {
            if (admission.Outcome == PaymentCheckoutOutcome.Invalid)
                return Invalid(new Dictionary<string, string[]> { ["planKey"] = ["An active one-time TRY plan is required."] });
            return new(admission.Outcome, admission.Response);
        }
        if (attempt is null) throw new InvalidOperationException("Checkout admission did not persist its payment attempt.");

        ProviderCheckoutResult providerResult;
        try
        {
            var returnBase = new Uri(publicWebOptions.Value.BaseUrl.TrimEnd('/') + "/");
            providerResult = await gateway.CreateCheckoutAsync(new ProviderCheckoutRequest(
                attempt.Reference, attempt.PlanKey, attempt.Amount, attempt.Currency,
                new Uri(returnBase, $"payment/return?reference={Uri.EscapeDataString(attempt.Reference)}").ToString(),
                new Uri(returnBase, $"payment/cancel?reference={Uri.EscapeDataString(attempt.Reference)}").ToString(),
                attempt.Reference), cancellationToken);
        }
        catch (Exception)
        {
            attempt.MarkUnknown(clock.UtcNow.ToUniversalTime());
            // Provider timeout/cancellation has an unknown outcome. Persist that state even if
            // the client disconnected so a new idempotency key cannot create a second charge.
            await db.SaveChangesAsync(CancellationToken.None);
            return new(PaymentCheckoutOutcome.ProviderUnavailable, ToResponse(attempt));
        }

        try
        {
            if (!gateway.IsTrustedCheckoutUrl(providerResult.CheckoutUrl))
                throw new ArgumentException("Provider returned a checkout URL outside its trusted origins.", nameof(providerResult));
            attempt.SetCheckout(providerResult.CheckoutUrl, providerResult.ProviderCheckoutId, clock.UtcNow.ToUniversalTime());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            attempt.MarkUnknown(clock.UtcNow.ToUniversalTime());
            await db.SaveChangesAsync(CancellationToken.None);
            return new(PaymentCheckoutOutcome.ProviderUnavailable, ToResponse(attempt));
        }

        return new(PaymentCheckoutOutcome.Created, ToResponse(attempt));
    }

    private static PaymentBillingKind MapBillingKind(PurchasableBillingKind billingKind) => billingKind switch
    {
        PurchasableBillingKind.OneTime => PaymentBillingKind.OneTime,
        PurchasableBillingKind.Monthly => PaymentBillingKind.Monthly,
        _ => throw new InvalidOperationException("Unsupported purchasable billing kind."),
    };

    private static Dictionary<string, string[]> Validate(Guid accountId, StartPaymentCheckoutRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (accountId == Guid.Empty) errors["account"] = ["A signed-in Creator account is required."];
        if (request.InvitationId == Guid.Empty) errors["invitationId"] = ["Invitation is required."];
        if (string.IsNullOrWhiteSpace(request.PlanKey) || request.PlanKey.Trim().Length > 64 ||
            request.PlanKey.Trim().ToLowerInvariant() is not "standard" and not "premium")
            errors["planKey"] = ["Only Standard or Premium is available for individual checkout."];
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length is < 16 or > 100)
            errors["idempotencyKey"] = ["Idempotency-Key must contain 16 to 100 characters."];
        return errors;
    }

    private static PaymentCheckoutResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(PaymentCheckoutOutcome.Invalid, Errors: errors);

    private static PaymentCheckoutOutcome ReplayOutcome(PaymentAttempt attempt) =>
        attempt.Status == PaymentAttemptStatus.Unknown ||
        (attempt.Status == PaymentAttemptStatus.Pending && attempt.CheckoutUrl is null)
            ? PaymentCheckoutOutcome.ProviderUnavailable
            : PaymentCheckoutOutcome.Replayed;

    private static PaymentCheckoutResponse ToResponse(PaymentAttempt attempt) => new(attempt.Id, attempt.Reference,
        attempt.Status, attempt.PlanKey, attempt.Amount, attempt.Currency, "one-time", attempt.CheckoutUrl);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
