namespace StreamRecorder.Core.Updates;

public sealed class UpdateInfo
{
    public string Version { get; set; } = string.Empty;

    public string HtmlUrl { get; set; } = string.Empty;

    public UpdateAsset? Asset { get; set; }
}
