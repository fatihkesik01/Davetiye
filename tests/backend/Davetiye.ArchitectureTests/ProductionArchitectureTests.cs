using Davetiye.Api;
using Davetiye.Application;
using Davetiye.Domain;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure;
using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class ProductionArchitectureTests
{
    private static readonly Type[] ProductionTypes =
    [
        .. ApiAssembly.Value.GetTypes(),
        .. ApplicationAssembly.Value.GetTypes(),
        .. DomainAssembly.Value.GetTypes(),
        .. InfrastructureAssembly.Value.GetTypes()
    ];

    [Fact]
    public void Production_module_types_do_not_reference_foreign_module_internals()
    {
        var moduleTypeCount = ProductionTypes.Count(type =>
            type.Namespace?.Contains(".Modules.", StringComparison.Ordinal) == true);

        Assert.True(moduleTypeCount > 0, "Architecture gate must inspect at least one production module type.");
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(ProductionTypes);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Production_contract_tree_contains_only_public_narrow_contracts()
    {
        Assert.Empty(ArchitectureRuleEvaluator.FindContractSurfaceViolations(ProductionTypes));
    }

    [Fact]
    public void Application_contracts_do_not_expose_mutable_media_placement_rows()
    {
        var placementReferences = ProductionTypes
            .Where(type => type.Namespace?.StartsWith(
                "Davetiye.Application.Modules.", StringComparison.Ordinal) == true &&
                type.Namespace.Split('.').Contains("Contracts", StringComparer.Ordinal))
            .SelectMany(GetContractMemberTypes)
            .SelectMany(FlattenType)
            .Where(type => type == typeof(MediaPlacement))
            .ToArray();

        Assert.Empty(placementReferences);
    }

    private static IEnumerable<Type> GetContractMemberTypes(Type contractType)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly;

        return contractType.GetProperties(flags).Select(property => property.PropertyType)
            .Concat(contractType.GetMethods(flags).Select(method => method.ReturnType))
            .Concat(contractType.GetMethods(flags)
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType));
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nestedType in FlattenType(elementType))
            {
                yield return nestedType;
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nestedType in FlattenType(argument))
            {
                yield return nestedType;
            }
        }
    }

    [Fact]
    public void Production_shared_kernel_contains_only_allowlisted_primitives()
    {
        var sharedKernelTypes = ProductionTypes
            .Where(type => type.Namespace?.StartsWith(
                "Davetiye.Domain.Modules.SharedKernel",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(sharedKernelTypes);
        Assert.Empty(ArchitectureRuleEvaluator.FindSharedKernelViolations(sharedKernelTypes));
    }

    [Fact]
    public static void Cross_module_entity_reference_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidEntityReference)]);

        Assert.Single(violations);
    }

    [Fact]
    public static void Foreign_entity_configuration_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidOwnedEntityConfiguration)]);

        Assert.Single(violations);
    }

    [Fact]
    public static void Body_only_static_foreign_module_call_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidBodyOnlyReference)]);

        Assert.Single(violations);
        Assert.Contains("OwnedOperations", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void Foreign_module_attribute_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidAttributeReference)]);

        Assert.Single(violations);
        Assert.Contains("OwnedMarkerAttribute", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void Foreign_module_generic_constraint_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidGenericConstraint<>)]);

        Assert.Single(violations);
        Assert.Contains("IOwnedConstraint", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void MethodSpec_foreign_generic_argument_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidMethodSpecReference)]);

        Assert.Single(violations);
        Assert.Contains("OwnedEntity", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void Foreign_constructor_attribute_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidConstructorAttributeReference)]);

        Assert.Single(violations);
        Assert.Contains("OwnedMarkerAttribute", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void Foreign_type_generic_parameter_attribute_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidTypeGenericParameterAttribute<>)]);

        Assert.Single(violations);
        Assert.Contains("OwnedMarkerAttribute", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public static void Foreign_method_generic_parameter_attribute_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
            [typeof(Davetiye.Domain.Modules.ForeignModule.InvalidMethodGenericParameterAttribute)]);

        Assert.Single(violations);
        Assert.Contains("OwnedMarkerAttribute", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_module_identifier_and_public_contract_fixtures_are_allowed()
    {
        var violations = ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(
        [
            typeof(Davetiye.Domain.Modules.ForeignModule.AllowedIdentifierReference),
            typeof(Davetiye.Application.Modules.ForeignModule.AllowedContractReference),
            typeof(Davetiye.Application.Modules.ForeignModule.AllowedContractBodyReference)
        ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Internal_and_handler_contract_fixtures_are_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindContractSurfaceViolations(
        [
            typeof(Davetiye.Application.Modules.OwningModule.Contracts.IInternalContract),
            typeof(Davetiye.Application.Modules.OwningModule.Contracts.PaymentHandler),
            typeof(Davetiye.Application.Modules.OwningModule.Contracts.Internal.Commands.HiddenCommand)
        ]);

        Assert.Equal(4, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("must be public", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("handler surface", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("namespace", StringComparison.Ordinal));
    }

    [Fact]
    public static void Shared_kernel_business_type_fixture_is_rejected()
    {
        var violations = ArchitectureRuleEvaluator.FindSharedKernelViolations(
            [typeof(Davetiye.Domain.Modules.SharedKernel.InvitationRepository)]);

        Assert.Single(violations);
    }
}
