namespace Hookbin.Application.GeoIp;

public interface IGeoIpService
{
    string? GetCountry(string ipAddress);
}
