using Davetiye.Domain.Modules.IdentityAndAccounts;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class AccountDeletionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Account_deletion_gate_and_tombstone_are_idempotent_and_ordered()
    {
        var account = Account.Create(Guid.NewGuid(), Guid.NewGuid(), AccountType.Individual, "Fatih", Now);

        Assert.True(account.BeginDeletion(Now.AddMinutes(1)));
        Assert.False(account.BeginDeletion(Now.AddMinutes(2)));
        Assert.True(account.AnonymizeForDeletion());
        Assert.False(account.AnonymizeForDeletion());
        Assert.Throws<InvalidOperationException>(() => Account.Create(Guid.NewGuid(), Guid.NewGuid(),
            AccountType.Individual, "Other", Now).CompleteDeletion(Now));
        Assert.True(account.CompleteDeletion(Now.AddMinutes(3)));
        Assert.False(account.CompleteDeletion(Now.AddMinutes(4)));

        Assert.Equal("Deleted account", account.DisplayName);
        Assert.Equal(Now.AddMinutes(1), account.DeletionStartedAtUtc);
        Assert.Equal(Now.AddMinutes(3), account.DeletionCompletedAtUtc);
    }

    [Fact]
    public void Deletion_request_only_consumes_unexpired_pending_hashed_tokens()
    {
        var request = AccountDeletionRequest.Create(Guid.NewGuid(), Guid.NewGuid(), new string('A', 64),
            Now, Now.AddHours(1));

        Assert.Equal(new string('a', 64), request.TokenHash);
        Assert.False(request.Consume(Now.AddHours(1)));
        Assert.True(request.Consume(Now.AddMinutes(10)));
        Assert.False(request.Consume(Now.AddMinutes(11)));
        Assert.Equal(AccountDeletionRequestStatus.Consumed, request.Status);
        Assert.Equal(Now.AddMinutes(10), request.ConsumedAtUtc);
    }

    [Fact]
    public void Deletion_work_requires_all_durable_checkpoints_before_completion_and_can_retry()
    {
        var work = AccountDeletionWork.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => work.Complete(Now.AddMinutes(1)));

        Assert.True(work.MarkSubscriptionCancellationsQueued(Now.AddMinutes(1)));
        Assert.True(work.MarkInvitationsPurgeQueued(Now.AddMinutes(1)));
        Assert.True(work.RecordRetry("identity-sanitize", Now.AddMinutes(2), Now.AddMinutes(3)));
        Assert.Equal(AccountDeletionWorkStatus.Retrying, work.Status);
        Assert.True(work.MarkIdentitySanitized(Now.AddMinutes(4)));
        Assert.True(work.Complete(Now.AddMinutes(5)));
        Assert.Equal(AccountDeletionWorkStatus.Completed, work.Status);
        Assert.Equal(1, work.AttemptCount);
        Assert.Null(work.LastErrorKind);
    }
}
