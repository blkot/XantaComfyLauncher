using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public sealed class GitService
{
    private readonly AppPaths _paths;

    public GitService(AppPaths paths) => _paths = paths;

    public async Task<bool> IsRepositoryAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "rev-parse", "--is-inside-work-tree" }, ct);
        return result.Success && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetCurrentCommitAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "rev-parse", "HEAD" }, ct);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<string> GetShortCommitAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "rev-parse", "--short", "HEAD" }, ct);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<string> GetCurrentTagAsync(CancellationToken ct = default)
    {
        var exact = await GitAsync(new[] { "describe", "--tags", "--exact-match", "HEAD" }, ct);
        if (exact.Success) return exact.StandardOutput.Trim();

        var tags = await GitAsync(new[] { "tag", "--points-at", "HEAD" }, ct);
        return tags.Success
            ? tags.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(SemVer.IsVersionTag) ?? ""
            : "";
    }

    public async Task<string> GetCommitDateAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "log", "-1", "--format=%cd", "--date=iso-strict" }, ct);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<string> GetTrackedStatusAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "status", "--porcelain", "--untracked-files=no" }, ct);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<string> GetRemoteUrlAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "remote", "get-url", "origin" }, ct);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<IReadOnlyList<RemoteTagInfo>> GetRemoteTagsAsync(CancellationToken ct = default)
    {
        var result = await GitAsync(new[] { "ls-remote", "--tags", "--refs", "origin" }, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"读取远程 tags 失败：{result.StandardError.Trim()}");
        }

        var tags = new Dictionary<string, RemoteTagInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('\t', 2);
            if (parts.Length != 2) continue;
            var commit = parts[0].Trim();
            var tag = parts[1].Trim();
            const string prefix = "refs/tags/";
            if (!tag.StartsWith(prefix, StringComparison.Ordinal)) continue;
            tag = tag[prefix.Length..];
            if (!SemVer.IsVersionTag(tag)) continue;
            tags[tag] = new RemoteTagInfo(tag, commit);
        }
        return tags.Values.ToList();
    }

    public async Task<string> FetchTagAsync(string tag, CancellationToken ct = default)
    {
        ValidateTag(tag);
        var result = await GitAsync(new[]
        {
            "fetch", "--depth", "1", "--force", "origin",
            $"refs/tags/{tag}:refs/tags/{tag}"
        }, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"拉取 tag {tag} 失败：{result.StandardError.Trim()}");
        }

        var commit = await GitAsync(new[] { "rev-parse", $"refs/tags/{tag}^{{commit}}" }, ct);
        if (!commit.Success || string.IsNullOrWhiteSpace(commit.StandardOutput))
        {
            throw new InvalidOperationException($"无法解析 tag {tag} 的提交。");
        }
        return commit.StandardOutput.Trim();
    }

    public async Task CheckoutAsync(string commit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commit) || commit.Length < 7 ||
            commit.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidOperationException("拒绝切换到无效提交。");
        }

        var result = await GitAsync(new[] { "checkout", "--detach", commit }, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"切换提交失败：{result.StandardError.Trim()}");
        }
    }

    public static void ValidateTag(string tag)
    {
        if (!SemVer.IsVersionTag(tag))
        {
            throw new InvalidOperationException($"只允许切换版本 tag，当前值无效：{tag}");
        }
    }

    private async Task<ProcessResult> GitAsync(IEnumerable<string> args, CancellationToken ct)
    {
        return await ProcessRunner.RunAsync(
            "git",
            new[] { "--no-pager", "-C", _paths.ComfyDir }.Concat(args),
            _paths.InstanceRoot,
            cancellationToken: ct).ConfigureAwait(false);
    }
}
