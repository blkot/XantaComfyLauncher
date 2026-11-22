using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class ComfyVersionService
{
    private readonly GitService _git;

    public ComfyVersionService(GitService git) => _git = git;

    public async Task<VersionCatalog> GetVersionsAsync(
        bool includePrerelease,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("正在读取 ComfyUI 远程 Git tags...");
        var tags = await _git.GetRemoteTagsAsync(ct).ConfigureAwait(false);
        var currentTag = await _git.GetCurrentTagAsync(ct).ConfigureAwait(false);

        var items = new List<ComfyVersionInfo>();
        foreach (var tag in tags)
        {
            var isPrerelease = IsPrereleaseTag(tag.Tag);
            if (!includePrerelease && isPrerelease) continue;

            items.Add(new ComfyVersionInfo
            {
                Tag = tag.Tag,
                Commit = tag.Commit,
                IsPrerelease = isPrerelease,
                IsCurrent = string.Equals(tag.Tag, currentTag, StringComparison.OrdinalIgnoreCase)
            });
        }

        items.Sort(SemVer.Compare);
        progress?.Report($"已读取 {items.Count} 个 ComfyUI 版本 tag。");
        return new VersionCatalog { Versions = items };
    }

    private static bool IsPrereleaseTag(string tag) =>
        SemVer.TryParse(tag, out _, out var suffix) && !string.IsNullOrWhiteSpace(suffix);
}
