using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Hookbin.ArchitectureTests.Layers;

public sealed class LayerDependencyTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void Domain_DoesNotDependOn_Application()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.Application")
            .Check(_arch);
    }

    [Fact]
    public void Domain_DoesNotDependOn_Infrastructure()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.Infrastructure")
            .Check(_arch);
    }

    [Fact]
    public void Domain_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Application_DoesNotDependOn_Infrastructure()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.Infrastructure")
            .Check(_arch);
    }

    [Fact]
    public void Application_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_StreamWorker()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.StreamWorker")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_JobsWorker()
    {
        Types().That().ResideInNamespaceMatching("Hookbin\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("Hookbin\\.JobsWorker")
            .Check(_arch);
    }
}
