namespace ComfyLauncher.Models;

public sealed class ComfyVersionInfo
{
    public required string Tag { get; init; }
    public string? Commit { get; init; }
    public string? Title { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool IsPrerelease { get; init; }
    public bool IsCurrent { get; set; }
    public string? ReleaseNotes { get; init; }
    public string? Source { get; init; }

    public string DisplayText
    {
        get
        {
            var current = IsCurrent ? "● " : "  ";
            var date = PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "----------";
            var title = string.IsNullOrWhiteSpace(Title) ? "Git tag" : Title;
            return $"{current}{Tag,-14} {date}  {title}";
        }
    }

    public override string ToString() => DisplayText;
}
