using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Templates;
using Xunit;

namespace Davetiye.UnitTests;

/// <summary>
/// Pure Domain tests (no database) for docs/PHASE_2_PLAN.md M2's Invitation, WorkingContent and
/// TemplateDefinition invariant guard clauses and revision/concurrency-token increment behavior.
/// </summary>
public sealed class InvitationsAndTemplatesEntityTests
{
    private const string PublicCode =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Invitation_Create_rejects_an_empty_account_id()
    {
        Assert.Throws<ArgumentException>(() => Invitation.Create(
            Guid.NewGuid(), Guid.Empty, PublicCode, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Invitation_Create_builds_a_draft_with_no_template_pinned()
    {
        var accountId = Guid.NewGuid();

        var invitation = Invitation.Create(Guid.NewGuid(), accountId, PublicCode, DateTimeOffset.UtcNow);

        Assert.Equal(accountId, invitation.AccountId);
        Assert.Equal(PublicCode, invitation.PublicCode);
        Assert.Equal(InvitationStoredState.Draft, invitation.State);
        Assert.Null(invitation.TemplateKey);
        Assert.Null(invitation.RendererVersion);
        Assert.Equal(0, invitation.Revision);
    }

    [Fact]
    public void Invitation_PinTemplate_rejects_a_blank_template_key()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => invitation.PinTemplate("   ", 1));
    }

    [Fact]
    public void Invitation_PinTemplate_rejects_a_non_positive_renderer_version()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => invitation.PinTemplate("klasik-dugun", 0));
    }

    [Fact]
    public void Invitation_PinTemplate_sets_the_pin_and_increments_revision()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode, DateTimeOffset.UtcNow);

        invitation.PinTemplate(" klasik-dugun ", 2);

        Assert.Equal("klasik-dugun", invitation.TemplateKey);
        Assert.Equal(2, invitation.RendererVersion);
        Assert.Equal(1, invitation.Revision);
    }

    [Fact]
    public void Invitation_PinTemplate_called_again_re_pins_and_increments_revision_again()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode, DateTimeOffset.UtcNow);
        invitation.PinTemplate("klasik-dugun", 2);

        invitation.PinTemplate("modern-baby-shower", 1);

        Assert.Equal("modern-baby-shower", invitation.TemplateKey);
        Assert.Equal(1, invitation.RendererVersion);
        Assert.Equal(2, invitation.Revision);
    }

    [Fact]
    public void WorkingContent_Create_rejects_an_empty_invitation_id()
    {
        Assert.Throws<ArgumentException>(() => WorkingContent.Create(
            Guid.NewGuid(), Guid.Empty, 1, "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void WorkingContent_Create_rejects_a_non_positive_schema_version()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkingContent.Create(
            Guid.NewGuid(), Guid.NewGuid(), 0, "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void WorkingContent_Create_rejects_syntactically_invalid_json()
    {
        Assert.Throws<ArgumentException>(() => WorkingContent.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1, "{not json", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void WorkingContent_Create_accepts_a_sparse_incomplete_draft_payload()
    {
        var content = WorkingContent.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1, """{"eventType":"dugun"}""", DateTimeOffset.UtcNow);

        Assert.Equal("""{"eventType":"dugun"}""", content.Content);
        Assert.Equal(0, content.Revision);
    }

    [Fact]
    public void WorkingContent_ReplaceContent_rejects_syntactically_invalid_json()
    {
        var content = WorkingContent.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1, "{}", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(
            () => content.ReplaceContent("not json", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void WorkingContent_ReplaceContent_overwrites_the_payload_and_increments_revision()
    {
        var content = WorkingContent.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1, "{}", DateTimeOffset.UtcNow);
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        content.ReplaceContent("""{"eventType":"dugun"}""", 2, updatedAt);

        Assert.Equal("""{"eventType":"dugun"}""", content.Content);
        Assert.Equal(2, content.ContentSchemaVersion);
        Assert.Equal(updatedAt, content.UpdatedAt);
        Assert.Equal(1, content.Revision);
    }

    [Fact]
    public void TemplateDefinition_Create_rejects_a_blank_key()
    {
        Assert.Throws<ArgumentException>(() => TemplateDefinition.Create(
            Guid.NewGuid(), "  ", "Klasik Düğün", "dugun", true, false, 1, null, "[]", "[]", "[]"));
    }

    [Fact]
    public void TemplateDefinition_Create_rejects_a_non_positive_renderer_version()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 0, null, "[]", "[]", "[]"));
    }

    [Fact]
    public void TemplateDefinition_Create_rejects_a_non_array_json_field_list()
    {
        Assert.Throws<ArgumentException>(() => TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1, null,
            "{}", "[]", "[]"));
    }

    [Fact]
    public void TemplateDefinition_Create_builds_a_valid_catalog_row()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(),
            " klasik-dugun ",
            " Klasik Düğün ",
            " dugun ",
            isActive: true,
            isPremium: false,
            currentRendererVersion: 1,
            previewImageUrl: " /templates/klasik-dugun/preview.jpg ",
            supportedModules: """["rsvp","gallery"]""",
            requiredFields: """["eventDate","venueName"]""",
            recommendedFields: """["giftRegistry"]""");

        Assert.Equal("klasik-dugun", template.Key);
        Assert.Equal("Klasik Düğün", template.Name);
        Assert.Equal("dugun", template.Category);
        Assert.True(template.IsActive);
        Assert.False(template.IsPremium);
        Assert.Equal(1, template.CurrentRendererVersion);
        Assert.Equal("/templates/klasik-dugun/preview.jpg", template.PreviewImageUrl);
        Assert.Equal(0, template.Revision);
    }

    [Fact]
    public void TemplateDefinition_description_is_optional_trimmed_and_bounded()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1,
            null, "[]", "[]", "[]", description: "  Zarif ve sade  ");

        Assert.Equal("Zarif ve sade", template.Description);

        template.UpdateMetadata(
            template.Name, template.Category, template.IsPremium, template.PreviewImageUrl,
            template.SupportedModules, template.RequiredFields, template.RecommendedFields,
            description: "   ");
        Assert.Null(template.Description);

        Assert.Throws<ArgumentOutOfRangeException>(() => template.UpdateMetadata(
            template.Name, template.Category, template.IsPremium, template.PreviewImageUrl,
            template.SupportedModules, template.RequiredFields, template.RecommendedFields,
            description: new string('x', TemplateDefinition.DescriptionMaxLength + 1)));
    }

    [Theory]
    [InlineData("https://example.com/preview.webp")]
    [InlineData("//example.com/preview.webp")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/template-previews\\preview.webp")]
    public void TemplateDefinition_rejects_preview_locations_that_are_not_safe_local_assets(string previewImageUrl)
    {
        Assert.Throws<ArgumentException>(() => TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1,
            previewImageUrl, "[]", "[]", "[]"));
    }

    [Fact]
    public void TemplateDefinition_SetActive_toggles_the_flag_and_increments_revision()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1, null, "[]", "[]", "[]");

        template.SetActive(false);

        Assert.False(template.IsActive);
        Assert.Equal(1, template.Revision);
    }

    [Fact]
    public void TemplateDefinition_SetCurrentRendererVersion_changes_only_the_renderer_pin()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1, null, "[]", "[]", "[]");

        template.SetCurrentRendererVersion(2);

        Assert.Equal(2, template.CurrentRendererVersion);
        Assert.Equal(1, template.Revision);
    }

    [Fact]
    public void TemplateDefinition_UpdateMetadata_replaces_fields_and_increments_revision()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1, null, "[]", "[]", "[]");

        template.UpdateMetadata(
            "Klasik Düğün v2",
            "dugun",
            isPremium: true,
            previewImageUrl: "/templates/klasik-dugun/preview-v2.jpg",
            supportedModules: """["rsvp"]""",
            requiredFields: """["eventDate"]""",
            recommendedFields: """["memories"]""");

        Assert.Equal("Klasik Düğün v2", template.Name);
        Assert.True(template.IsPremium);
        Assert.Equal(1, template.CurrentRendererVersion);
        Assert.Equal("/templates/klasik-dugun/preview-v2.jpg", template.PreviewImageUrl);
        Assert.Equal(1, template.Revision);
    }

    [Fact]
    public void TemplateDefinition_UpdateAdminMetadata_changes_only_name_description_visibility_and_revision()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1,
            "/preview.webp", "[]", "[]", "[]");

        template.UpdateAdminMetadata("  New name  ", "  Plain description  ", false);

        Assert.Equal("New name", template.Name);
        Assert.Equal("Plain description", template.Description);
        Assert.False(template.IsActive);
        Assert.Equal("dugun", template.Category);
        Assert.False(template.IsPremium);
        Assert.Equal(1, template.CurrentRendererVersion);
        Assert.Equal("/preview.webp", template.PreviewImageUrl);
        Assert.Equal(1, template.Revision);

        template.UpdateAdminMetadata("New name", "Plain description", false);
        Assert.Equal(1, template.Revision);
    }

    [Fact]
    public void TemplateDefinition_UpdateAdminMetadata_rejects_invalid_name_and_oversized_description()
    {
        var template = TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun", true, false, 1,
            null, "[]", "[]", "[]");

        Assert.Throws<ArgumentException>(() => template.UpdateAdminMetadata("  ", null, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.UpdateAdminMetadata(
            new string('x', TemplateDefinition.NameMaxLength + 1), null, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.UpdateAdminMetadata(
            "Valid", new string('x', TemplateDefinition.DescriptionMaxLength + 1), true));
    }
}
