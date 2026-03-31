namespace StreamRecorder.Core.Updates;

public sealed class UpdateInfo
{
    public required string Version { get; init; }

    public required string HtmlUrl { get; init; }

    public UpdateAsset? Asset { get; init; }
}
