using Microsoft.Extensions.Options;

namespace Hookbin.API.Options;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username))
            return ValidateOptionsResult.Fail("Auth:Username is required.");

        if (string.IsNullOrWhiteSpace(options.PasswordHash) || !options.PasswordHash.StartsWith("$2"))
            return ValidateOptionsResult.Fail(
                "Auth:PasswordHash must be a valid BCrypt hash (must start with $2).");

        if (options.SessionHours is < 1 or > 168)
            return ValidateOptionsResult.Fail("Auth:SessionHours must be between 1 and 168.");

        return ValidateOptionsResult.Success;
    }
}
