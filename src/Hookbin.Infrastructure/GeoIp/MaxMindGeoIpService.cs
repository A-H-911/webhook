using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Logging;
using Hookbin.Application.GeoIp;

namespace Hookbin.Infrastructure.GeoIp;

internal sealed class MaxMindGeoIpService : IGeoIpService, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<MaxMindGeoIpService> _logger;

    public MaxMindGeoIpService(ILogger<MaxMindGeoIpService> logger)
    {
        _logger = logger;
        var path = Environment.GetEnvironmentVariable("HOOKBIN_GEOIP_PATH")
                   ?? "/app/data/GeoLite2-Country.mmdb";

        if (!File.Exists(path))
        {
            _logger.LogWarning("GeoIP database not found at {Path}; country lookup disabled", path);
            return;
        }

        try
        {
            _reader = new DatabaseReader(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GeoIP database at {Path}; country lookup disabled", path);
        }
    }

    public string? GetCountry(string ipAddress)
    {
        if (_reader is null)
            return null;

        try
        {
            var response = _reader.Country(ipAddress);
            return response.Country.IsoCode;
        }
        catch (Exception ex) when (ex is AddressNotFoundException or GeoIP2Exception or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose() => _reader?.Dispose();
}
