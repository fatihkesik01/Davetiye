namespace Davetiye.Application.Modules.Media.Contracts;

/// <summary>Trusted, provider-neutral browser origins required to deliver enabled media.</summary>
public interface IMediaContentSources
{
    IReadOnlyList<Uri> ImageSources { get; }
    IReadOnlyList<Uri> ConnectionSources { get; }
    IReadOnlyList<Uri> FrameSources { get; }
}
