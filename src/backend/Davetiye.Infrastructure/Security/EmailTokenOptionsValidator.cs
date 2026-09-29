using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class EmailTokenOptionsValidator : IValidateOptions<EmailTokenOptions>
{
    public const int MinTokenLifetimeMinutes = 1;
    public const int MaxTokenLifetimeMinutes = 240;

    public ValidateOptionsResult Validate(string? name, EmailTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.TokenLifetimeMinutes is < MinTokenLifetimeMinutes or > MaxTokenLifetimeMinutes)
        {
            failures.Add(
                $"EmailTokens:TokenLifetimeMinutes must be between {MinTokenLifetimeMinutes} and {MaxTokenLifetimeMinutes}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
