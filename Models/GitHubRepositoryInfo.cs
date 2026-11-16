namespace XantaComfyLauncher.Models;

public sealed class GitHubRepositoryInfo
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string CloneUrl { get; init; }
    public required string HtmlUrl { get; init; }
    public string DefaultBranch { get; init; } = "main";
    public string? Description { get; init; }
    public string? Language { get; init; }
    public int StargazersCount { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public string DisplayDescription
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Description)) parts.Add(Description);
            if (IsArchived) parts.Add("[已归档]");
            return string.Join("  ", parts);
        }
    }
}
