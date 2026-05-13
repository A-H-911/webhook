using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Hookbin.Infrastructure.GeoIp;

namespace Hookbin.UnitTests.Infrastructure.GeoIp;

public sealed class MaxMindGeoIpServiceTests : IDisposable
{
    private const string GeoIpPathEnvVar = "HOOKBIN_GEOIP_PATH";
    private readonly string? _originalEnvValue;

    public MaxMindGeoIpServiceTests()
    {
        _originalEnvValue = Environment.GetEnvironmentVariable(GeoIpPathEnvVar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, _originalEnvValue);
    }

    private static string MissingDbPath() =>
        Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mmdb");

    [Fact]
    public void Constructor_DoesNotThrow_WhenDatabaseFileMissing()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, MissingDbPath());

        var act = () => new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetCountry_ReturnsNull_WhenDatabaseFileMissing()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, MissingDbPath());
        using var service = new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

        var result = service.GetCountry("8.8.8.8");

        result.Should().BeNull();
    }

    [Fact]
    public void GetCountry_ReturnsNull_ForInvalidIpAddress_WhenDatabaseMissing()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, MissingDbPath());
        using var service = new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

        var result = service.GetCountry("not-an-ip");

        result.Should().BeNull();
    }

    [Fact]
    public void Constructor_DoesNotThrow_WhenDatabaseFileIsCorrupt()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.mmdb");
        File.WriteAllText(tempPath, "not a real mmdb file");
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, tempPath);
        try
        {
            var act = () => new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

            act.Should().NotThrow("constructor swallows DatabaseReader exceptions");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenReaderIsNull()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, MissingDbPath());
        var service = new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

        var act = service.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_IsSafeWhenCalledMultipleTimes()
    {
        Environment.SetEnvironmentVariable(GeoIpPathEnvVar, MissingDbPath());
        var service = new MaxMindGeoIpService(NullLogger<MaxMindGeoIpService>.Instance);

        service.Dispose();
        var act = service.Dispose;

        act.Should().NotThrow();
    }
}
