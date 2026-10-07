using Davetiye.Domain.Modules.Memories;
using Davetiye.Infrastructure.Modules.Memories;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class MemoriesDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid InvitationId = Guid.NewGuid();

    [Fact]
    public void Configuration_defaults_to_disabled_creator_only_and_toggles_without_touching_memories()
    {
        var configuration = MemoryConfiguration.Create(Guid.NewGuid(), InvitationId, Now);
        Assert.False(configuration.IsEnabled);
        Assert.Equal(MemoryVisibility.CreatorOnly, configuration.Visibility);
        configuration.SetVisibility(MemoryVisibility.Public, Now.AddMinutes(1));
        Assert.Equal(MemoryVisibility.Public, configuration.Visibility);
        Assert.Equal(1, configuration.Revision);
        Assert.Throws<ArgumentException>(() => configuration.SetEnabled(true, Now));
    }

    [Fact]
    public void Text_memory_is_published_immediately_and_inputs_are_trimmed()
    {
        var memory = Memory.Create(Guid.NewGuid(), InvitationId, "  ", " hi ", null, false, Now);
        Assert.Equal(MemoryState.Published, memory.State);
        Assert.Null(memory.DisplayName);
        Assert.Equal("hi", memory.Text);
        Assert.Equal(Now, memory.FinalizedAt);
        Assert.Throws<ArgumentException>(() => Memory.Create(Guid.NewGuid(), InvitationId, null, " ", null, false, Now));
        Assert.Throws<ArgumentException>(() =>
            Memory.Create(Guid.NewGuid(), InvitationId, null, new string('x', 501), null, false, Now));
    }

    [Fact]
    public void Media_memory_needs_media_before_finalize_and_respects_the_ceiling()
    {
        var memory = Memory.Create(Guid.NewGuid(), InvitationId, null, null, null, true, Now);
        Assert.Equal(MemoryState.PendingMedia, memory.State);
        Assert.Throws<InvalidOperationException>(() => memory.Finalize(Now));
        var asset = Guid.NewGuid();
        memory.AttachMedia(asset, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => memory.AttachMedia(asset, Guid.NewGuid()));
        memory.AttachMedia(Guid.NewGuid(), Guid.NewGuid());
        memory.AttachMedia(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => memory.AttachMedia(Guid.NewGuid(), Guid.NewGuid()));
        memory.Finalize(Now.AddSeconds(5));
        Assert.Equal(MemoryState.Published, memory.State);
        Assert.Throws<InvalidOperationException>(() => memory.Abandon());
        memory.Hide(Now.AddMinutes(1));
        Assert.Equal(MemoryState.Hidden, memory.State);
        Assert.Equal([0, 1, 2], memory.Media.Select(item => item.Ordinal).ToArray());
    }

    [Fact]
    public void Pending_memory_cannot_be_hidden_and_abandoned_memory_cannot_finalize()
    {
        var memory = Memory.Create(Guid.NewGuid(), InvitationId, null, "x", null, true, Now);
        Assert.Throws<InvalidOperationException>(() => memory.Hide(Now));
        memory.Abandon();
        Assert.Equal(MemoryState.Abandoned, memory.State);
        Assert.Throws<InvalidOperationException>(() => memory.Finalize(Now));
    }

    [Fact]
    public void Capability_is_purpose_scoped_short_lived_and_single_use()
    {
        var digest = new byte[32];
        Assert.Throws<ArgumentException>(() => MemoryUploadCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            "rsvp-manage", 1, digest, Now, Now.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => MemoryUploadCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            MemoryUploadCapability.RequiredPurpose, 1, digest, Now, Now.AddMinutes(16)));
        Assert.Throws<ArgumentException>(() => MemoryUploadCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            MemoryUploadCapability.RequiredPurpose, 1, new byte[31], Now, Now.AddMinutes(5)));
        var capability = MemoryUploadCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            MemoryUploadCapability.RequiredPurpose, 1, digest, Now, Now.AddMinutes(15));
        Assert.False(capability.IsUsableAt(Now.AddMinutes(15)));
        capability.Consume(Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => capability.Consume(Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(MemoryState.Published, true, MemoryVisibility.Public, true, true)]
    [InlineData(MemoryState.Published, true, MemoryVisibility.CreatorOnly, true, false)]
    [InlineData(MemoryState.Published, true, MemoryVisibility.Public, false, false)]
    [InlineData(MemoryState.Published, false, MemoryVisibility.Public, true, false)]
    [InlineData(MemoryState.Hidden, true, MemoryVisibility.Public, true, false)]
    [InlineData(MemoryState.PendingMedia, true, MemoryVisibility.Public, true, true)]
    public void Public_projection_requires_published_enabled_public_and_entitled(
        MemoryState state, bool enabled, MemoryVisibility visibility, bool entitled, bool expected) =>
        Assert.Equal(expected, MemoryProjectionPolicy.IsVisibleToPublic(state, enabled, visibility, entitled));

    [Fact]
    public void Creator_projection_ignores_entitlement_and_includes_hidden_but_not_pending()
    {
        Assert.True(MemoryProjectionPolicy.IsVisibleToCreator(MemoryState.Published));
        Assert.True(MemoryProjectionPolicy.IsVisibleToCreator(MemoryState.Hidden));
        Assert.False(MemoryProjectionPolicy.IsVisibleToCreator(MemoryState.PendingMedia));
        Assert.False(MemoryProjectionPolicy.IsVisibleToCreator(MemoryState.Abandoned));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void Submissions_need_module_entitlement_and_effective_active(
        bool enabled, bool entitled, bool active, bool expected) =>
        Assert.Equal(expected, MemoryProjectionPolicy.CanAcceptSubmission(enabled, entitled, active));

    [Fact]
    public void Validator_accepts_defaults_and_tightening_but_never_widens_hard_ceilings()
    {
        var validator = new MemoryInputLimitsValidator();
        Assert.True(validator.Validate(null, new MemoryInputLimits()).Succeeded);
        Assert.True(validator.Validate(null, new MemoryInputLimits { MaxTextCharacters = 200, MaxMediaPerMemory = 1 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { MaxTextCharacters = 501 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { MaxDisplayNameCharacters = 61 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { MaxEmojiCharacters = 33 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { MaxMediaPerMemory = 4 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { UploadCapabilityLifetimeMinutes = 16 }).Succeeded);
        Assert.False(validator.Validate(null, new MemoryInputLimits { MaxTextCharacters = 0 }).Succeeded);
    }

    [Fact]
    public void Limits_validate_submissions_against_configured_values()
    {
        var limits = new MemoryInputLimits { MaxTextCharacters = 10 };
        Assert.Null(limits.ValidateSubmission(null, "short", null, 0));
        Assert.NotNull(limits.ValidateSubmission(null, new string('x', 11), null, 0));
        Assert.NotNull(limits.ValidateSubmission(null, null, null, 0));
        Assert.NotNull(limits.ValidateSubmission(null, null, null, 4));
        Assert.Null(limits.ValidateSubmission(null, null, null, 3));
    }

    [Fact]
    public void Dropping_rejected_media_keeps_a_text_memory_finalizable_but_an_empty_memory_is_not()
    {
        var withText = Memory.Create(Guid.NewGuid(), InvitationId, null, "kept text", null, true, Now);
        var asset = Guid.NewGuid();
        withText.AttachMedia(asset, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => withText.DropMedia(Guid.NewGuid()));
        withText.DropMedia(asset);
        Assert.Empty(withText.Media);
        withText.Finalize(Now.AddSeconds(1));
        Assert.Equal(MemoryState.Published, withText.State);
        Assert.Throws<InvalidOperationException>(() => withText.DropMedia(asset));

        var empty = Memory.Create(Guid.NewGuid(), InvitationId, null, null, null, true, Now);
        var only = Guid.NewGuid();
        empty.AttachMedia(only, Guid.NewGuid());
        empty.DropMedia(only);
        Assert.Throws<InvalidOperationException>(() => empty.Finalize(Now.AddSeconds(1)));
        empty.Abandon();
        Assert.Equal(MemoryState.Abandoned, empty.State);
    }
}
