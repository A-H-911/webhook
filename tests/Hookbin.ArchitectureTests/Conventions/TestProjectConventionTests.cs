using System.Xml.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Hookbin.ArchitectureTests.Conventions;

public sealed class TestProjectConventionTests(ArchitectureFixture fixture) : IClassFixture<ArchitectureFixture>
{
    private readonly Architecture _arch = fixture.Architecture;

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(Path.GetDirectoryName(typeof(TestProjectConventionTests).Assembly.Location)!, "..", "..", "..", "..", ".."));

    [Fact]
    public void FluentAssertions_Version_IsUniform_AcrossAllTestProjects()
    {
        var csprojPaths = new[]
        {
            Path.Combine(RepoRoot, "tests", "Hookbin.UnitTests", "Hookbin.UnitTests.csproj"),
            Path.Combine(RepoRoot, "tests", "Hookbin.IntegrationTests", "Hookbin.IntegrationTests.csproj"),
            Path.Combine(RepoRoot, "tests", "Hookbin.E2ETests", "Hookbin.E2ETests.csproj"),
        };

        var versions = csprojPaths
            .Where(File.Exists)
            .Select(path =>
            {
                var doc = XDocument.Load(path);
                return doc.Descendants("PackageReference")
                    .FirstOrDefault(e => string.Equals(e.Attribute("Include")?.Value, "FluentAssertions", StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("Version")?.Value;
            })
            .Where(v => v is not null)
            .ToList();

        versions.Should().NotBeEmpty("at least one test project must reference FluentAssertions");
        versions.Distinct().Should().HaveCount(1, "all test projects that use FluentAssertions must use the same version");
    }
}
