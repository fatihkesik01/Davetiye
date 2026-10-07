namespace Davetiye.Domain.Modules.Media;

/// <summary>
/// Server-internal evidence returned after authoritative video provider inspection. Never bind
/// this type to an HTTP request or accept it from a browser. Images require the distinct normalized
/// image receipt type.
/// </summary>
public sealed record MediaVerificationEvidence
{
    public string ProviderObjectReference { get; }
    public string DetectedContentType { get; }
    public long ByteLength { get; }
    public int? DurationSeconds { get; }

    internal MediaVerificationEvidence(
        string providerObjectReference,
        string detectedContentType,
        long byteLength,
        int? durationSeconds)
    {
        ProviderObjectReference = providerObjectReference;
        DetectedContentType = detectedContentType;
        ByteLength = byteLength;
        DurationSeconds = durationSeconds;
    }

}
