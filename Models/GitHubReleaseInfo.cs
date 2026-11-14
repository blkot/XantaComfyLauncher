namespace ComfyLauncher.Models;

public sealed class GitHubReleaseInfo
{
    public string Tag { get; init; } = "";
    public string? Title { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool Prerelease { get; init; }
    public string? Body { get; init; }
}
