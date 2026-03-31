namespace StreamRecorder.Core.Updates;

public sealed class UpdateAsset
{
    public required string Name { get; init; }

    public required string DownloadUrl { get; init; }

    public required long Size { get; init; }

    public required UpdateAssetKind Kind { get; init; }
}
