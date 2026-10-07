using Davetiye.Domain.Modules.Memories;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

public sealed class MemoryInputLimitsValidator : IValidateOptions<MemoryInputLimits>
{
    public ValidateOptionsResult Validate(string? name, MemoryInputLimits options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        Range(nameof(options.MaxDisplayNameCharacters), options.MaxDisplayNameCharacters, 1,
            MemoryInputLimits.HardMaxDisplayNameCharacters, failures);
        Range(nameof(options.MaxTextCharacters), options.MaxTextCharacters, 1,
            MemoryInputLimits.HardMaxTextCharacters, failures);
        Range(nameof(options.MaxEmojiCharacters), options.MaxEmojiCharacters, 1,
            MemoryInputLimits.HardMaxEmojiCharacters, failures);
        Range(nameof(options.MaxMediaPerMemory), options.MaxMediaPerMemory, 0,
            MemoryInputLimits.HardMaxMediaPerMemory, failures);
        Range(nameof(options.UploadCapabilityLifetimeMinutes), options.UploadCapabilityLifetimeMinutes, 1,
            MemoryInputLimits.HardMaxUploadCapabilityLifetimeMinutes, failures);
        Range(nameof(options.MaxMemoriesPerInvitation), options.MaxMemoriesPerInvitation, 1,
            MemoryInputLimits.HardMaxMemoriesPerInvitation, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Range(string name, int value, int minimum, int maximum, ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{MemoryInputLimits.SectionName}:{name} must be between {minimum} and {maximum}.");
    }
}
