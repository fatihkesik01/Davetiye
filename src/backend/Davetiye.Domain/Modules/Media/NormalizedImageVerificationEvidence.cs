namespace Davetiye.Domain.Modules.Media;

/// <summary>
/// Unforgeable-by-request evidence that the normalized-image ingress verified the stored output.
/// Only the Infrastructure adapter for the server-side normalization pipeline may issue it.
/// Browser completion payloads and ordinary provider inspection records cannot construct it.
/// </summary>
public sealed class NormalizedImageVerificationEvidence
{
    internal NormalizedImageVerificationEvidence(
        string providerObjectReference,
        string detectedContentType,
        long byteLength)
    {
        ProviderObjectReference = providerObjectReference;
        DetectedContentType = detectedContentType;
        ByteLength = byteLength;
    }

    public string ProviderObjectReference { get; }
    public string DetectedContentType { get; }
    public long ByteLength { get; }
}
