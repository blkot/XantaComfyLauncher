using System.Net.Http.Headers;
using System.Text.Json;
using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public sealed class GitHubReleaseService
{
    private const string ReleasesUrl = "https://api.github.com/repos/comfyanonymous/ComfyUI/releases?per_page=100";
    private static readonly HttpClient Http = CreateClient();
    private readonly AppPaths _paths;
    private readonly string _cacheFile;

    public GitHubReleaseService(AppPaths paths)
    {
        _paths = paths;
        _cacheFile = Path.Combine(paths.CacheDir, "github-releases.json");
    }

    public async Task<(IReadOnlyList<GitHubReleaseInfo> Releases, string? Warning)> GetReleasesAsync(
        bool forceRefresh,
        CancellationToken ct = default)
    {
        var cached = await ReadCacheAsync(ct);
        if (!forceRefresh && cached != null && DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromHours(1))
        {
            return (cached.Releases, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ComfyLauncher", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            }

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"GitHub Releases API 返回 {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var releases = await ParseReleasesAsync(stream, ct).ConfigureAwait(false);
            await WriteCacheAsync(releases, ct).ConfigureAwait(false);
            return (releases, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (cached != null)
            {
                return (cached.Releases, $"Release 信息获取失败，使用缓存：{ex.Message}");
            }
            return (Array.Empty<GitHubReleaseInfo>(), $"Release 信息获取失败，仅显示 Git tags：{ex.Message}");
        }
    }

    private static async Task<IReadOnlyList<GitHubReleaseInfo>> ParseReleasesAsync(Stream stream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var result = new List<GitHubReleaseInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var tag = GetString(item, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) continue;
            DateTimeOffset? published = null;
            var publishedText = GetString(item, "published_at");
            if (DateTimeOffset.TryParse(publishedText, out var parsed)) published = parsed;
            result.Add(new GitHubReleaseInfo
            {
                Tag = tag,
                Title = GetString(item, "name"),
                PublishedAt = published,
                Prerelease = item.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(),
                Body = GetString(item, "body")
            });
        }
        return result;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<ReleaseCache?> ReadCacheAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_cacheFile)) return null;
            await using var stream = File.OpenRead(_cacheFile);
            return await JsonSerializer.DeserializeAsync<ReleaseCache>(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(IReadOnlyList<GitHubReleaseInfo> releases, CancellationToken ct)
    {
        try
        {
            _paths.EnsureDirectories();
            await using var stream = File.Create(_cacheFile);
            await JsonSerializer.SerializeAsync(stream, new ReleaseCache
            {
                FetchedAt = DateTimeOffset.UtcNow,
                Releases = releases.ToList()
            }, cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            // Cache failure must not break version listing.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ComfyLauncher/1.0");
        return client;
    }

    private sealed class ReleaseCache
    {
        public DateTimeOffset FetchedAt { get; set; }
        public List<GitHubReleaseInfo> Releases { get; set; } = new();
    }
}
