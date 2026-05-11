namespace Hookbin.Domain.ValueObjects;

public sealed record CustomResponse
{
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "text/plain";
    public string? Body { get; init; }
    public string Headers { get; init; } = "{}";
}
