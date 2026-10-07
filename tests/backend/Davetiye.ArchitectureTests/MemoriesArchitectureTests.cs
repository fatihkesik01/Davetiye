using Davetiye.Application;
using Davetiye.Domain;
using Davetiye.Infrastructure;
using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class MemoriesArchitectureTests
{
    private static readonly Type[] AllTypes =
    [
        .. ApplicationAssembly.Value.GetTypes(),
        .. DomainAssembly.Value.GetTypes(),
        .. InfrastructureAssembly.Value.GetTypes()
    ];

    private static Type[] MemoriesTypes => [.. AllTypes.Where(type =>
        type.Namespace?.Contains(".Modules.Memories", StringComparison.Ordinal) == true)];

    [Fact]
    public void Memories_module_exists_in_every_layer_and_has_no_cross_module_references()
    {
        var types = MemoriesTypes;
        Assert.Contains(types, type => type.Namespace == "Davetiye.Domain.Modules.Memories");
        Assert.Contains(types, type => type.Namespace == "Davetiye.Application.Modules.Memories.Contracts");
        Assert.Contains(types, type => type.Namespace == "Davetiye.Infrastructure.Modules.Memories");
        Assert.Empty(ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(types));
    }

    [Fact]
    public void Memories_domain_never_references_Media_or_Invitations_types()
    {
        var domainTypes = MemoriesTypes.Where(type => type.Namespace == "Davetiye.Domain.Modules.Memories");
        var foreign = domainTypes
            .SelectMany(type => type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Select(property => property.PropertyType))
            .Where(type => type.Namespace is not null &&
                (type.Namespace.Contains(".Modules.Media", StringComparison.Ordinal) ||
                 type.Namespace.Contains(".Modules.Invitations", StringComparison.Ordinal)))
            .ToArray();
        Assert.Empty(foreign);
    }

    [Fact]
    public void Memories_infrastructure_does_not_depend_on_Media_infrastructure_or_the_Media_tables()
    {
        var offenders = MemoriesTypes
            .Where(type => type.Namespace == "Davetiye.Infrastructure.Modules.Memories")
            .Where(type => type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .Concat(type.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(parameter => parameter.ParameterType))
                .Any(dependency => dependency.FullName?.Contains("Modules.Media", StringComparison.Ordinal) == true &&
                    !dependency.FullName.Contains(".Contracts.", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Memories_contracts_do_not_expose_domain_entities()
    {
        var entityTypes = MemoriesTypes
            .Where(type => type.Namespace == "Davetiye.Domain.Modules.Memories" && type.IsClass && !type.IsAbstract && type.IsSealed)
            .ToHashSet();
        var exposed = MemoriesTypes
            .Where(type => type.Namespace == "Davetiye.Application.Modules.Memories.Contracts")
            .SelectMany(type => type.GetMethods().SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
                .Concat(type.GetProperties().Select(property => property.PropertyType)))
            .Where(entityTypes.Contains)
            .ToArray();
        Assert.Empty(exposed);
    }
    [Fact]
    public void Memories_services_reach_Invitations_only_through_Application_contracts()
    {
        var offenders = MemoriesTypes
            .Where(type => type.Namespace == "Davetiye.Infrastructure.Modules.Memories")
            .Where(type => type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .Concat(type.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(parameter => parameter.ParameterType))
                .Any(dependency => dependency.Namespace?.Contains(".Modules.Invitations", StringComparison.Ordinal) == true &&
                    dependency.Namespace != "Davetiye.Application.Modules.Invitations.Contracts"))
            .Select(type => type.FullName)
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Invitations_owns_the_Memories_guest_and_creator_access_adapters()
    {
        foreach (var port in new[] { typeof(Application.Modules.Invitations.Contracts.IMemoriesGuestInvitationAccessReader),
                     typeof(Application.Modules.Invitations.Contracts.IMemoriesCreatorInvitationAccessReader) })
        {
            var implementations = InfrastructureAssembly.Value.GetTypes().Where(type => port.IsAssignableFrom(type) && type.IsClass).ToArray();
            var implementation = Assert.Single(implementations);
            Assert.Equal("Davetiye.Infrastructure.Modules.Invitations", implementation.Namespace);
        }
    }
}
