using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Hookbin.ArchitectureTests.Conventions;

public sealed class CqrsConventionTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    [Fact]
    public void Commands_AreSealed()
    {
        Classes().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .And().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void Queries_AreSealed()
    {
        Classes().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .And().HaveNameEndingWith("Query")
            .Should().BeSealed()
            .Check(_arch);
    }

    [Fact]
    public void CommandHandlers_AreInternalAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .And().HaveNameEndingWith("CommandHandler")
            .Should().BeSealed()
            .AndShould().NotBePublic()
            .Check(_arch);
    }

    [Fact]
    public void QueryHandlers_AreInternalAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .And().HaveNameEndingWith("QueryHandler")
            .Should().BeSealed()
            .AndShould().NotBePublic()
            .Check(_arch);
    }

    [Fact]
    public void Validators_ArePublicAndSealed()
    {
        Classes().That().ResideInNamespaceMatching("Hookbin\\.Application")
            .And().HaveNameEndingWith("Validator")
            .Should().BeSealed()
            .AndShould().BePublic()
            .Check(_arch);
    }

    [Fact]
    public void CommandHandlers_ImplementIRequestHandler()
    {
        var handlerTypes = typeof(Hookbin.Application.Tokens.Commands.CreateToken.CreateTokenCommand).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && t.Namespace is not null
                && t.Namespace.StartsWith("Hookbin.Application", StringComparison.Ordinal)
                && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .ToList();

        handlerTypes.Should().NotBeEmpty("at least one command handler must exist for the rule to be meaningful");

        var nonHandlerImplementors = handlerTypes
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition().FullName?.StartsWith("MediatR.IRequestHandler", StringComparison.Ordinal) == true))
            .Select(t => t.FullName)
            .ToList();

        nonHandlerImplementors.Should().BeEmpty(
            "every *CommandHandler must implement MediatR.IRequestHandler<,> or IRequestHandler<>. Offenders: {0}",
            string.Join(", ", nonHandlerImplementors));
    }

    [Fact]
    public void QueryHandlers_ImplementIRequestHandler()
    {
        var handlerTypes = typeof(Hookbin.Application.Tokens.Commands.CreateToken.CreateTokenCommand).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && t.Namespace is not null
                && t.Namespace.StartsWith("Hookbin.Application", StringComparison.Ordinal)
                && t.Name.EndsWith("QueryHandler", StringComparison.Ordinal))
            .ToList();

        handlerTypes.Should().NotBeEmpty("at least one query handler must exist for the rule to be meaningful");

        var nonHandlerImplementors = handlerTypes
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition().FullName?.StartsWith("MediatR.IRequestHandler", StringComparison.Ordinal) == true))
            .Select(t => t.FullName)
            .ToList();

        nonHandlerImplementors.Should().BeEmpty(
            "every *QueryHandler must implement MediatR.IRequestHandler<,>. Offenders: {0}",
            string.Join(", ", nonHandlerImplementors));
    }
}
