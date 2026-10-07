using System.Reflection;
using Davetiye.Application;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain;
using Davetiye.Infrastructure;
using Xunit;

namespace Davetiye.ArchitectureTests;

/// <summary>P6-M3: Memories and Media meet only through Application contracts; guest assets never leak across scopes or layers.</summary>
public sealed class GuestMediaArchitectureTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] AllTypes =
    [
        .. ApplicationAssembly.Value.GetTypes(),
        .. DomainAssembly.Value.GetTypes(),
        .. InfrastructureAssembly.Value.GetTypes()
    ];

    private static Type[] ModuleTypes(string module) => [.. AllTypes.Where(type =>
        type.Namespace?.Contains($".Modules.{module}", StringComparison.Ordinal) == true)];

    [Fact]
    public void Media_module_references_other_modules_only_through_application_contracts()
    {
        Assert.Empty(ArchitectureRuleEvaluator.FindCrossModuleReferenceViolations(ModuleTypes("Media")));
    }

    [Fact]
    public void Memories_and_Media_expose_each_other_only_contract_types_never_domain_entities_or_infrastructure()
    {
        foreach (var (source, foreign) in new[] { ("Memories", ".Modules.Media"), ("Media", ".Modules.Memories") })
        {
            var offenders = ModuleTypes(source)
                .SelectMany(type => SignatureTypes(type).Select(dependency => (type, dependency)))
                .Where(pair => pair.dependency.Namespace?.Contains(foreign, StringComparison.Ordinal) == true &&
                               !(pair.dependency.Namespace.StartsWith("Davetiye.Application", StringComparison.Ordinal) &&
                                 pair.dependency.Namespace.Contains(".Contracts", StringComparison.Ordinal)))
                .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
                .Distinct()
                .ToArray();
            Assert.Empty(offenders);
        }
    }

    [Fact]
    public void Guest_media_ports_are_implemented_only_inside_the_Media_module()
    {
        foreach (var port in new[] { typeof(IGuestMediaAssetStatusReader), typeof(IGuestMediaStore),
                     typeof(IGuestMediaUploadService), typeof(IGuestMediaUploadAvailability),
                     typeof(IGuestMediaVerificationService) })
        {
            var implementations = AllTypes.Where(type => type.IsClass && !type.IsAbstract && port.IsAssignableFrom(type)).ToArray();
            Assert.NotEmpty(implementations);
            Assert.All(implementations, implementation =>
                Assert.Contains(".Modules.Media", implementation.Namespace, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Memories_owns_the_upload_service_and_the_expiry_sweeper_ports()
    {
        foreach (var port in new[] { typeof(IPublicMemoryUploadService), typeof(IMemoryUploadExpirySweeper),
                     typeof(IPublicMemoryUploadIntentLimiter) })
        {
            var implementation = Assert.Single(AllTypes, type => type.IsClass && !type.IsAbstract && port.IsAssignableFrom(type));
            Assert.Equal("Davetiye.Infrastructure.Modules.Memories", implementation.Namespace);
        }
    }

    [Fact]
    public void Guest_contracts_do_not_leak_provider_references_urls_or_media_domain_types()
    {
        var contractTypes = new[]
        {
            typeof(GuestMediaAssetStatus), typeof(GuestMediaReservation), typeof(GuestMediaReservationCommand),
            typeof(PublicMemoryUploadMediaStatus), typeof(PublicMemoryUploadStatus)
        };
        var offenders = contractTypes.SelectMany(type => type.GetProperties())
            .Where(property => property.PropertyType.Namespace?.StartsWith("Davetiye.Domain", StringComparison.Ordinal) == true ||
                               property.Name.Contains("ProviderObjectReference", StringComparison.Ordinal) ||
                               property.PropertyType == typeof(Uri))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Public_guest_upload_service_has_no_account_or_ip_dependencies_and_reads_no_media_tables()
    {
        var service = ModuleTypes("Memories").Single(type => type.Name == "PublicMemoryUploadService");
        var dependencies = service.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(parameter => parameter.ParameterType.Name).ToArray();
        Assert.DoesNotContain(dependencies, name => name.Contains("HttpContext", StringComparison.Ordinal) ||
                                                      name.Contains("CurrentAccount", StringComparison.Ordinal));
        var mediaSets = service.GetMembers(Declared).OfType<MethodBase>()
            .SelectMany(method => method.GetMethodBody()?.LocalVariables.Select(local => local.LocalType) ?? [])
            .Where(type => type.Namespace?.Contains(".Modules.Media", StringComparison.Ordinal) == true &&
                           !type.Namespace.Contains(".Contracts", StringComparison.Ordinal));
        Assert.Empty(mediaSets);
    }

    private static IEnumerable<Type> SignatureTypes(Type type) => type.GetFields(Declared).Select(field => field.FieldType)
        .Concat(type.GetProperties(Declared).Select(property => property.PropertyType))
        .Concat(type.GetConstructors(Declared).SelectMany(ctor => ctor.GetParameters()).Select(parameter => parameter.ParameterType))
        .Concat(type.GetMethods(Declared).SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)))
        .SelectMany(Flatten);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
            foreach (var nested in Flatten(element)) yield return nested;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var nested in Flatten(argument)) yield return nested;
    }
}
