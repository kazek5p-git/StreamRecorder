namespace StreamRecorder.Core.Updates;

public sealed class UpdateAsset
{
    public string Name { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    public long Size { get; set; }

    public UpdateAssetKind Kind { get; set; }
}
