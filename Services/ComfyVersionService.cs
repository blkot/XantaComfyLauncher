using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public sealed class ComfyVersionService
{
    private readonly GitService _git;
    private readonly GitHubReleaseService _releases;

    public ComfyVersionService(GitService git, GitHubReleaseService releases)
    {
        _git = git;
        _releases = releases;
    }

    public async Task<VersionCatalog> GetVersionsAsync(
        bool includePrerelease,
        bool forceRefresh,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("正在读取远程 Git tags...");
        var tags = await _git.GetRemoteTagsAsync(ct).ConfigureAwait(false);
        var currentTag = await _git.GetCurrentTagAsync(ct).ConfigureAwait(false);

        progress?.Report($"已读取 {tags.Count} 个版本 tag，正在获取 Release 信息...");
        var (releases, warning) = await _releases.GetReleasesAsync(forceRefresh, ct).ConfigureAwait(false);
        var byTag = releases
            .Where(r => !string.IsNullOrWhiteSpace(r.Tag))
            .GroupBy(r => r.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ComfyVersionInfo>();
        foreach (var tag in tags)
        {
            byTag.TryGetValue(tag.Tag, out var release);
            if (!includePrerelease && release?.Prerelease == true) continue;
            if (!includePrerelease && SemVer.TryParse(tag.Tag, out _, out var suffix) &&
                !string.IsNullOrWhiteSpace(suffix))
            {
                continue;
            }

            items.Add(new ComfyVersionInfo
            {
                Tag = tag.Tag,
                Commit = tag.Commit,
                Title = release?.Title,
                PublishedAt = release?.PublishedAt,
                IsPrerelease = release?.Prerelease == true || IsPrereleaseTag(tag.Tag),
                IsCurrent = string.Equals(tag.Tag, currentTag, StringComparison.OrdinalIgnoreCase),
                ReleaseNotes = release?.Body,
                Source = release != null ? "Release" : "Tag"
            });
        }

        items.Sort(SemVer.Compare);
        return new VersionCatalog { Versions = items, Warning = warning };
    }

    private static bool IsPrereleaseTag(string tag) =>
        SemVer.TryParse(tag, out _, out var suffix) && !string.IsNullOrWhiteSpace(suffix);
}
