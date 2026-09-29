using Davetiye.Application;
using Davetiye.Domain;
using Davetiye.Infrastructure;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class LayerIdentityTests
{
    [Fact]
    public void Layer_assembly_names_are_stable()
    {
        Assert.Equal("Davetiye.Domain", DomainAssembly.Value.GetName().Name);
        Assert.Equal("Davetiye.Application", ApplicationAssembly.Value.GetName().Name);
        Assert.Equal("Davetiye.Infrastructure", InfrastructureAssembly.Value.GetName().Name);
    }
}
