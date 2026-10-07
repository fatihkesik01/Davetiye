using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class AccountDeletionArchitectureTests
{
    [Fact]
    public void Account_deletion_state_changes_are_owned_by_their_business_modules()
    {
        AssertOwnedPort<IOrganizationSubscriptionAccountDeletionCommand,
            OrganizationSubscriptionAccountDeletionHandler>("Payments");
        AssertOwnedPort<IInvitationAccountDeletionCommand, InvitationAccountDeletionHandler>("Invitations");
        AssertOwnedPort<IAccountPlanGrantDeletionCommand, AccountPlanGrantDeletionHandler>("PlansAndEntitlements");

        var coordinator = typeof(AccountDeletionService);
        Assert.DoesNotContain(typeof(OrganizationSubscription), coordinator.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(typeof(Invitation), coordinator.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(typeof(AccountPlanGrant), coordinator.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Checkout_does_not_read_identity_entities_outside_the_identity_eligibility_port()
    {
        var checkoutConstructor = Assert.Single(typeof(PaymentCheckoutService).GetConstructors());
        Assert.DoesNotContain(typeof(Account), checkoutConstructor.GetParameters()
            .Select(parameter => parameter.ParameterType));
        Assert.Contains(checkoutConstructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(IPaymentAccountEligibilityReader));
        Assert.Equal("Davetiye.Infrastructure.Modules.IdentityAndAccounts",
            typeof(PaymentAccountEligibilityReader).Namespace);
    }

    private static void AssertOwnedPort<TContract, TImplementation>(string ownerModule)
    {
        Assert.Equal($"Davetiye.Application.Modules.{ownerModule}.Contracts", typeof(TContract).Namespace);
        Assert.Equal($"Davetiye.Infrastructure.Modules.{ownerModule}", typeof(TImplementation).Namespace);
        Assert.True(typeof(TContract).IsAssignableFrom(typeof(TImplementation)));
    }
}
