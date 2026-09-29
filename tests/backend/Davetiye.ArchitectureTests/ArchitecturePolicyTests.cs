using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class ArchitecturePolicyTests
{
    [Theory]
    [InlineData("Davetiye.Domain.Modules.IdentityAccounts", "Davetiye.Domain", true)]
    [InlineData("Davetiye.Application.Modules.Plans.Contracts", "Davetiye.Application", true)]
    [InlineData("Davetiye.Domain.IdentityAccounts", "Davetiye.Domain", false)]
    [InlineData("Davetiye.Domain.Modules.identityAccounts", "Davetiye.Domain", false)]
    public void Module_namespace_policy_has_positive_and_negative_proof(
        string candidate,
        string layerNamespace,
        bool expected)
    {
        Assert.Equal(expected, ArchitecturePolicy.IsValidModuleNamespace(candidate, layerNamespace));
    }

    [Theory]
    [InlineData("Davetiye.Application.Modules.Notifications.Contracts.IEmailSender", true)]
    [InlineData("Davetiye.Application.Modules.Media.CloudflareUploadClient", false)]
    [InlineData("Davetiye.Domain.Modules.Payments.IyzicoPayment", false)]
    [InlineData("Davetiye.Domain.Modules.Notifications.ResendMessage", false)]
    public void Provider_neutrality_policy_has_positive_and_negative_proof(
        string candidate,
        bool expected)
    {
        Assert.Equal(expected, ArchitecturePolicy.IsProviderNeutral(candidate));
    }

    [Theory]
    [InlineData("Davetiye.Application.Modules.Media.Contracts.IMediaAccess", true)]
    [InlineData("Davetiye.Application.Modules.Media.Contracts.Internal.Commands", false)]
    [InlineData("Davetiye.Application.Modules.Media.Contracts.Handlers", false)]
    [InlineData("Davetiye.Application.Modules.Media.IMediaAccess", false)]
    [InlineData("Davetiye.Application.Contracts.IMediaAccess", false)]
    public void Cross_module_contract_policy_has_positive_and_negative_proof(
        string candidate,
        bool expected)
    {
        Assert.Equal(expected, ArchitecturePolicy.IsApplicationContractNamespace(candidate));
    }

    [Theory]
    [InlineData("AccountId", true)]
    [InlineData("IClock", true)]
    [InlineData("Money", true)]
    [InlineData("Result`1", true)]
    [InlineData("Invitation", false)]
    [InlineData("InvitationRepository", false)]
    public void Shared_kernel_allowlist_has_positive_and_negative_proof(
        string candidate,
        bool expected)
    {
        Assert.Equal(expected, ArchitecturePolicy.IsAllowedSharedKernelTypeName(candidate));
    }

    [Theory]
    [InlineData("IMediaAccess", true)]
    [InlineData("MediaContract", true)]
    [InlineData("InternalMediaContract", false)]
    [InlineData("MediaImplementation", false)]
    [InlineData("MediaHandler", false)]
    public void Contract_type_name_policy_rejects_implementation_surfaces(
        string candidate,
        bool expected)
    {
        Assert.Equal(expected, ArchitecturePolicy.IsAllowedContractTypeName(candidate));
    }

    [Fact]
    public void Module_source_policy_accepts_a_production_shaped_source()
    {
        const string source = "namespace Davetiye.Domain.Modules.Media.Uploads;\npublic sealed class UploadIntent;";

        var violation = ArchitecturePolicy.ValidateModuleSource(
            "Domain",
            "Modules/Media/Uploads/UploadIntent.cs",
            source);

        Assert.Null(violation);
    }

    [Fact]
    public void Module_source_policy_detects_a_mismatched_production_shaped_source()
    {
        const string source = "namespace Davetiye.Domain.Modules.Payments;\npublic sealed class UploadIntent;";

        var violation = ArchitecturePolicy.ValidateModuleSource(
            "Domain",
            "Modules/Media/UploadIntent.cs",
            source);

        Assert.NotNull(violation);
    }
}
