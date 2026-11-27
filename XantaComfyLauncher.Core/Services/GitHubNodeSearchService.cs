using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class GitHubNodeSearchService
{
    private const string ApiBase = "https://api.github.com";
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex OwnerRepoRegex = new(
        @"^(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GithubUrlRegex = new(
        @"^(?:https?://)?(?:www\.)?github\.com/(?<owner>[^/\s?#]+)/(?<repo>[^/\s?#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SshUrlRegex = new(
        @"^git@github\.com:(?<owner>[^/\s]+)/(?<repo>[^/\s]+?)(?:\.git)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<GitHubRepositoryInfo>> SearchAsync(
        string input,
        CancellationToken ct = default)
    {
        var query = (input ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("请输入 GitHub URL、author/repo 或搜索关键词。");

        if (TryParseRepository(query, out var owner, out var repo))
        {
            var exact = await GetRepositoryAsync(owner, repo, ct).ConfigureAwait(false);
            if (exact != null) return new[] { exact };
            throw new InvalidOperationException(
                $"未找到 GitHub 仓库 {owner}/{repo}。请确认仓库存在；私有仓库需要在设置页配置 GitHub Token。");
        }

        var searchQuery = query + " fork:false";
        var url = $"{ApiBase}/search/repositories?q={Uri.EscapeDataString(searchQuery)}" +
                  "&sort=stars&order=desc&per_page=20";
        using var request = CreateRequest(url);
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GitHubRepositoryInfo>();
        }

        return items.EnumerateArray()
            .Select(ParseRepository)
            .Where(x => x != null)
            .Cast<GitHubRepositoryInfo>()
            .ToList();
    }

    public static bool RepositoryUrlsEqual(string left, string right)
    {
        return string.Equals(NormalizeRepositoryUrl(left), NormalizeRepositoryUrl(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRepositoryUrl(string value)
    {
        var text = (value ?? "").Trim();
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) text = text[..^4];
        text = text.TrimEnd('/');
        if (text.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            text = "https://github.com/" + text["git@github.com:".Length..];
        if (text.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        return text;
    }

    private async Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string owner,
        string repo,
        CancellationToken ct)
    {
        var url = $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
        using var request = CreateRequest(url);
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return ParseRepository(doc.RootElement);
    }

    private static GitHubRepositoryInfo? ParseRepository(JsonElement item)
    {
        var fullName = GetString(item, "full_name");
        var cloneUrl = GetString(item, "clone_url");
        var htmlUrl = GetString(item, "html_url");
        if (string.IsNullOrWhiteSpace(fullName) ||
            string.IsNullOrWhiteSpace(cloneUrl) ||
            string.IsNullOrWhiteSpace(htmlUrl))
        {
            return null;
        }

        var parts = fullName.Split('/', 2);
        var owner = parts.Length == 2 ? parts[0] : "";
        var name = parts.Length == 2 ? parts[1] : fullName;
        var defaultBranch = GetString(item, "default_branch");
        DateTimeOffset? updated = null;
        if (DateTimeOffset.TryParse(GetString(item, "updated_at"), out var parsed)) updated = parsed;

        return new GitHubRepositoryInfo
        {
            Owner = owner,
            Name = name,
            FullName = fullName,
            CloneUrl = cloneUrl,
            HtmlUrl = htmlUrl,
            DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch,
            Description = GetString(item, "description"),
            Language = GetString(item, "language"),
            StargazersCount = item.TryGetProperty("stargazers_count", out var stars) && stars.TryGetInt32(out var count)
                ? count
                : 0,
            IsArchived = item.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True,
            UpdatedAt = updated
        };
    }

    private static bool TryParseRepository(string input, out string owner, out string repo)
    {
        owner = "";
        repo = "";
        var text = input.Trim().Trim('"');
        Match match = SshUrlRegex.Match(text);
        if (!match.Success) match = GithubUrlRegex.Match(text);
        if (!match.Success) match = OwnerRepoRegex.Match(text);
        if (!match.Success) return false;

        owner = match.Groups["owner"].Value;
        repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XantaComfyLauncher", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var token = GitHubTokenStore.GetEffectiveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden &&
            body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "GitHub API 速率限制已用尽。请稍后重试，或在设置页配置 GitHub Token（Windows 凭据管理器）。");
        }

        throw new InvalidOperationException(
            $"GitHub API 返回 {(int)response.StatusCode} {response.ReasonPhrase}。" +
            (string.IsNullOrWhiteSpace(body) ? "" : "\n" + body));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XantaComfyLauncher/1.0");
        return client;
    }
}
