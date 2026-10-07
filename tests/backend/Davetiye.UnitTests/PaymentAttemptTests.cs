using Davetiye.Domain.Modules.Payments;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PaymentAttemptTests
{
    [Fact]
    public void Attempt_prices_and_correlation_are_snapshotted_and_unknown_is_not_a_retryable_terminal_state()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "checkout-idempotency-001", "dv-reference-001", now);

        attempt.MarkUnknown(now.AddMinutes(1));

        Assert.Equal("standard", attempt.PlanKey);
        Assert.Equal(PaymentBillingKind.OneTime, attempt.BillingKindAtAttempt);
        Assert.Equal(699m, attempt.Amount);
        Assert.Equal("TRY", attempt.Currency);
        Assert.Equal("checkout-idempotency-001", attempt.IdempotencyKey);
        Assert.Equal("dv-reference-001", attempt.Reference);
        Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status);
        Assert.Throws<InvalidOperationException>(() => attempt.SetCheckout("https://checkout.example.test/session", null, now.AddMinutes(2)));
    }

    [Fact]
    public void Payment_attempt_rejects_a_non_one_time_checkout_snapshot()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "standard", 699m, "TRY", "checkout-idempotency-shape", "dv-reference-shape", now,
            PaymentBillingKind.Monthly));
    }

    [Theory]
    [InlineData("http://attacker.example.test/checkout")]
    [InlineData("javascript:alert(1)")]
    public void Checkout_url_rejects_untrusted_schemes_and_hosts(string url)
    {
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "premium", 1199m, "TRY", "checkout-idempotency-002", "dv-reference-002",
            new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentException>(() => attempt.SetCheckout(url, null,
            new DateTimeOffset(2026, 10, 6, 12, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Checkout_url_accepts_https_provider_and_loopback_development_urls()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var providerAttempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "checkout-idempotency-003", "dv-reference-003", now);
        var localAttempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "premium", 1199m, "TRY", "checkout-idempotency-004", "dv-reference-004", now);

        providerAttempt.SetCheckout("https://checkout.example.test/session", "provider-id", now);
        localAttempt.SetCheckout("http://localhost:5173/fake-checkout/reference", "fake-reference", now);

        Assert.Equal(PaymentAttemptStatus.Pending, providerAttempt.Status);
        Assert.Equal(PaymentAttemptStatus.Pending, localAttempt.Status);
    }

    [Fact]
    public void Fake_gateway_trusts_only_its_configured_origin_and_checkout_path()
    {
        var gateway = new FakePaymentGateway("http://localhost:5173/fake-checkout/");

        Assert.True(gateway.IsTrustedCheckoutUrl("http://localhost:5173/fake-checkout/session-1"));
        Assert.False(gateway.IsTrustedCheckoutUrl("https://sandbox-api.iyzipay.com/session-1"));
        Assert.False(gateway.IsTrustedCheckoutUrl("http://localhost:5173/other/session-1"));
        Assert.False(gateway.IsTrustedCheckoutUrl("http://localhost.attacker.test:5173/fake-checkout/session-1"));
    }

    [Fact]
    public void Success_persists_provider_payment_and_grant_identity_and_cannot_be_rewritten()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "idempotency-success", "dv-reference-success", now);
        attempt.SetCheckout("https://checkout.example.test/session", "provider-token", now);
        var grantId = Guid.NewGuid();

        Assert.True(attempt.MarkSucceeded(now.AddMinutes(1), "123456789", grantId));
        Assert.False(attempt.MarkSucceeded(now.AddMinutes(2), "987654321", Guid.NewGuid()));
        Assert.False(attempt.MarkFailed(now.AddMinutes(2)));

        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal("123456789", attempt.ProviderPaymentId);
        Assert.Equal(grantId, attempt.GrantedPlanGrantId);
        Assert.Equal(PaymentAttemptSettlementDisposition.Granted, attempt.SettlementDisposition);
    }

    [Fact]
    public void Verified_payment_after_account_deletion_retains_settlement_without_issuing_entitlement()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "idempotency-deleted-account", "dv-reference-deleted-account", now);

        Assert.True(attempt.MarkSucceededWithoutEntitlement(now.AddMinutes(1), "123456789"));
        Assert.False(attempt.MarkSucceededWithoutEntitlement(now.AddMinutes(2), "987654321"));
        Assert.True(attempt.MarkReversed(now.AddMinutes(3), PaymentAttemptReversalKind.FullRefund));

        Assert.Equal(PaymentAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(PaymentAttemptSettlementDisposition.NoEntitlement, attempt.SettlementDisposition);
        Assert.Equal("123456789", attempt.ProviderPaymentId);
        Assert.Null(attempt.GrantedPlanGrantId);
    }

    [Theory]
    [InlineData("not-a-payment-id")]
    [InlineData("")]
    public void Success_requires_provider_payment_identity(string providerPaymentId)
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "idempotency-invalid", "dv-reference-invalid", now);

        Assert.Throws<ArgumentException>(() => attempt.MarkSucceeded(now, providerPaymentId, Guid.NewGuid()));
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Null(attempt.GrantedPlanGrantId);
    }

    [Fact]
    public void Confirmed_reversal_is_terminal_and_conflicting_outcome_cannot_rewrite_it()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "idempotency-reversal", "dv-reference-reversal", now);
        attempt.SetCheckout("https://checkout.example.test/session", "provider-token", now);
        var grantId = Guid.NewGuid();
        attempt.MarkSucceeded(now.AddMinutes(1), "123456789", grantId);

        Assert.True(attempt.MarkReversed(now.AddMinutes(2), PaymentAttemptReversalKind.FullRefund));
        Assert.False(attempt.MarkReversed(now.AddMinutes(3), PaymentAttemptReversalKind.FullRefund));
        Assert.False(attempt.MarkSucceeded(now.AddMinutes(4), "123456789", grantId));

        Assert.Equal(PaymentAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(PaymentAttemptReversalKind.FullRefund, attempt.ReversalKind);
        Assert.Equal(now.AddMinutes(2), attempt.ReversedAtUtc);
        Assert.Equal("123456789", attempt.ProviderPaymentId);
        Assert.Equal(grantId, attempt.GrantedPlanGrantId);
    }

    [Fact]
    public void Final_chargeback_resolution_is_persistent_and_cannot_be_replaced_by_an_opposite_result()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "standard", 699m, "TRY", "idempotency-chargeback", "dv-reference-chargeback", now);
        attempt.SetCheckout("https://checkout.example.test/session", "provider-token", now);
        attempt.MarkSucceeded(now.AddMinutes(1), "123456789", Guid.NewGuid());

        Assert.True(attempt.MarkChargebackResolved(now.AddMinutes(2), PaymentAttemptChargebackResolution.FinalWon));
        Assert.False(attempt.MarkChargebackResolved(now.AddMinutes(3), PaymentAttemptChargebackResolution.FinalLost));
        Assert.True(attempt.MarkReversed(now.AddMinutes(4), PaymentAttemptReversalKind.FullRefund));

        Assert.Equal(PaymentAttemptChargebackResolution.FinalWon, attempt.ChargebackResolution);
        Assert.Equal(now.AddMinutes(2), attempt.ChargebackResolvedAtUtc);
        Assert.Equal(PaymentAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(PaymentAttemptReversalKind.FullRefund, attempt.ReversalKind);
    }

    [Fact]
    public void Persistence_model_enforces_purchase_idempotency_and_single_pending_attempt_without_card_fields()
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;
        using var db = new DavetiyeDbContext(options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PaymentAttempt))!;

        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["AccountId", "InvitationId", "IdempotencyKey"]));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "status IN ('Pending', 'Unknown')" &&
            index.Properties.Select(property => property.Name).SequenceEqual(["AccountId", "InvitationId"]));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["ProviderPaymentId"]));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["GrantedPlanGrantId"]));
        var updatedAtPagingIndex = Assert.Single(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "ix_payment_attempts_updated_at_id");
        Assert.Equal(["UpdatedAt", "Id"], updatedAtPagingIndex.Properties.Select(property => property.Name));
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "ck_payment_attempts_success_identity_pairing");
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "ck_payment_attempts_success_requires_provider_identity");
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "ck_payment_attempts_grant_disposition_pairing");
        Assert.Contains(entity.GetProperties(), property => property.Name == "SettlementDisposition");
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }
}
