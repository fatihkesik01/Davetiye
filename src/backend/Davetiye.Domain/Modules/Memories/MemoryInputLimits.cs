namespace Davetiye.Domain.Modules.Memories;

/// <summary>
/// Typed, overrideable Memory validation limits. NOTE: every numeric default below is an
/// ENGINEERING DEFAULT proposed by the architect, not a product-approved number, and the hard
/// ceilings are mirrored by database check constraints. Guest media quota values are a separate
/// open product decision and are intentionally absent.
/// </summary>
public sealed class MemoryInputLimits
{
    public const string SectionName = "MemoryValidation";

    public const int HardMaxDisplayNameCharacters = 60;
    public const int HardMaxTextCharacters = 500;
    public const int HardMaxEmojiCharacters = 32;
    public const int HardMaxMediaPerMemory = 3;
    public const int HardMaxUploadCapabilityLifetimeMinutes = 15;
    public const int HardMaxMemoriesPerInvitation = 5000;

    public const int ApprovedMaxDisplayNameCharacters = 60;
    public const int ApprovedMaxTextCharacters = 500;
    public const int ApprovedMaxEmojiCharacters = 16;
    public const int ApprovedMaxMediaPerMemory = 3;
    public const int ApprovedUploadCapabilityLifetimeMinutes = 10;
    /// <summary>ENGINEERING DEFAULT (not a product-approved number, not a plan-catalog key): abuse ceiling on non-abandoned memories per invitation.</summary>
    public const int EngineeringDefaultMaxMemoriesPerInvitation = 500;

    public int MaxDisplayNameCharacters { get; init; } = ApprovedMaxDisplayNameCharacters;
    public int MaxTextCharacters { get; init; } = ApprovedMaxTextCharacters;
    public int MaxEmojiCharacters { get; init; } = ApprovedMaxEmojiCharacters;
    public int MaxMediaPerMemory { get; init; } = ApprovedMaxMediaPerMemory;
    public int UploadCapabilityLifetimeMinutes { get; init; } = ApprovedUploadCapabilityLifetimeMinutes;
    public int MaxMemoriesPerInvitation { get; init; } = EngineeringDefaultMaxMemoriesPerInvitation;

    /// <summary>Returns a validation message, or null when the (already trimmed) submission fits the limits.</summary>
    public string? ValidateSubmission(string? displayName, string? text, string? emoji, int mediaCount)
    {
        if (displayName is not null && displayName.Length > MaxDisplayNameCharacters)
            return $"Display name may contain at most {MaxDisplayNameCharacters} characters.";
        if (text is not null && text.Length > MaxTextCharacters)
            return $"Text may contain at most {MaxTextCharacters} characters.";
        if (emoji is not null && emoji.Length > MaxEmojiCharacters)
            return $"Emoji may contain at most {MaxEmojiCharacters} characters.";
        if (mediaCount < 0 || mediaCount > MaxMediaPerMemory)
            return $"A memory may contain at most {MaxMediaPerMemory} media items.";
        if (text is null && emoji is null && mediaCount == 0)
            return "A memory needs text, an emoji or media.";
        return null;
    }
}
