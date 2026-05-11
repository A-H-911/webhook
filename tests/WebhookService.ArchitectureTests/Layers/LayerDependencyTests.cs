using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace WebhookService.ArchitectureTests.Layers;

public sealed class LayerDependencyTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void Domain_DoesNotDependOn_Application()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.Application")
            .Check(_arch);
    }

    [Fact]
    public void Domain_DoesNotDependOn_Infrastructure()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.Infrastructure")
            .Check(_arch);
    }

    [Fact]
    public void Domain_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Domain")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Application_DoesNotDependOn_Infrastructure()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.Infrastructure")
            .Check(_arch);
    }

    [Fact]
    public void Application_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_API()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.API")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_StreamWorker()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.StreamWorker")
            .Check(_arch);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_JobsWorker()
    {
        Types().That().ResideInNamespaceMatching("WebhookService\\.Infrastructure")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching("WebhookService\\.JobsWorker")
            .Check(_arch);
    }
}
