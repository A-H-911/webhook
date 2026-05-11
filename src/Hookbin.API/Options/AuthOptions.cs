namespace Hookbin.API.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public int SessionHours { get; init; } = 8;
}
