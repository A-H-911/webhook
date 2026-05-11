using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace WebhookService.ArchitectureTests.Conventions;

public sealed class RepositoryEntityConventionTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void RepositoryInterfaces_ResideIn_DomainRepositories()
    {
        Interfaces().That().HaveNameStartingWith("I").And().HaveNameEndingWith("Repository")
            .Should().ResideInNamespace("WebhookService.Domain.Repositories")
            .Check(_arch);
    }

    [Fact]
    public void RepositoryImplementations_ResideIn_InfrastructurePersistenceRepositories()
    {
        Classes().That().HaveNameEndingWith("Repository")
            .Should().ResideInNamespace("WebhookService.Infrastructure.Persistence.Repositories")
            .Check(_arch);
    }

    [Fact]
    public void DomainEntities_AreSealed()
    {
        Classes().That().ResideInNamespace("WebhookService.Domain.Entities")
            .Should().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void EfConfigurations_HaveCorrectNaming_AndNamespace()
    {
        Classes().That().ResideInNamespace("WebhookService.Infrastructure.Persistence.Configurations")
            .Should().HaveNameEndingWith("Configuration")
            .Check(_arch);
    }
}
