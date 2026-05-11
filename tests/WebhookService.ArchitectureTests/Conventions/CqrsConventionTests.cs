using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace WebhookService.ArchitectureTests.Conventions;

public sealed class CqrsConventionTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void Commands_AreSealed()
    {
        Classes().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .And().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void Queries_AreSealed()
    {
        Classes().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .And().HaveNameEndingWith("Query")
            .Should().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void CommandHandlers_AreInternalAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .And().HaveNameEndingWith("CommandHandler")
            .Should().BeSealed()
            .AndShould().NotBePublic()
            .Check(_arch);
    }

    [Fact]
    public void QueryHandlers_AreInternalAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .And().HaveNameEndingWith("QueryHandler")
            .Should().BeSealed()
            .AndShould().NotBePublic()
            .Check(_arch);
    }

    [Fact]
    public void Validators_ArePublicAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("WebhookService\\.Application")
            .And().HaveNameEndingWith("Validator")
            .Should().BeSealed()
            .AndShould().BePublic()
            .Check(_arch);
    }
}
