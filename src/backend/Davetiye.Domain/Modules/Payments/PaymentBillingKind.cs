namespace Davetiye.Domain.Modules.Payments;

/// <summary>Payments-owned, immutable billing shape recorded on a verified checkout.</summary>
public enum PaymentBillingKind
{
    OneTime,
    Monthly
}
