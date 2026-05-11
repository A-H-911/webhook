using NetArchTest.Rules;
using Hookbin.API.Controllers;
using Hookbin.Application.Tokens.Commands.CreateToken;
using Hookbin.Domain.Entities;
using Hookbin.Infrastructure.Persistence;
using System.Linq;

namespace Hookbin.ArchitectureTests.Structure;

public sealed class FolderNamespaceTests
{
    [Fact]
    public void Domain_SourceFilePaths_MatchNamespaces()
    {
        var result = Types.InAssembly(typeof(WebhookToken).Assembly)
            .Should()
            .HaveSourceFilePathMatchingNamespace()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain types have folder/namespace mismatch: {string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? [])}");
    }

    [Fact]
    public void Application_SourceFilePaths_MatchNamespaces()
    {
        var result = Types.InAssembly(typeof(CreateTokenCommand).Assembly)
            .Should()
            .HaveSourceFilePathMatchingNamespace()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Application types have folder/namespace mismatch: {string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? [])}");
    }

    [Fact]
    public void Infrastructure_SourceFilePaths_MatchNamespaces()
    {
        var result = Types.InAssembly(typeof(ApplicationDbContext).Assembly)
            .That().DoNotHaveNameStartingWith("<")
            .Should()
            .HaveSourceFilePathMatchingNamespace()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure types have folder/namespace mismatch: {string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? [])}");
    }

    [Fact]
    public void API_SourceFilePaths_MatchNamespaces()
    {
        var result = Types.InAssembly(typeof(TokensController).Assembly)
            .That().DoNotHaveNameStartingWith("<")
            .Should()
            .HaveSourceFilePathMatchingNamespace()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"API types have folder/namespace mismatch: {string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? [])}");
    }
}
