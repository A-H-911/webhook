using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Hookbin.ArchitectureTests.Conventions;

public sealed class ControllerMiddlewareConventionTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void Controllers_ArePublicAndSealed()
    {
        Classes().That().ResideInNamespace("Hookbin.API.Controllers")
            .Should().BePublic()
            .AndShould().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void Controllers_HaveCorrectNamingSuffix()
    {
        Classes().That().ResideInNamespace("Hookbin.API.Controllers")
            .And().AreNotRecord()
            .Should().HaveNameEndingWith("Controller")
            .Check(_arch);
    }

    [Fact]
    public void Middleware_ArePublicAndSealed()
    {
        Classes().That().ResideInNamespace("Hookbin.API.Middleware")
            .Should().BePublic()
            .AndShould().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void Middleware_HaveCorrectNamingSuffix()
    {
        Classes().That().ResideInNamespace("Hookbin.API.Middleware")
            .Should().HaveNameEndingWith("Middleware")
            .Check(_arch);
    }
}
