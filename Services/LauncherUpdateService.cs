using System.Net;
using System.Reflection;
using Velopack;
using Velopack.Sources;
using XantaComfyLauncher.Models;

namespace XantaComfyLauncher.Services;

public sealed record LauncherUpdateCheckResult(
    bool IsInstalled,
    string CurrentVersion,
    string Channel,
    UpdateInfo? Update,
    string? AvailableVersion);

/// <summary>
/// Wraps Velopack so the launcher UI does not depend on update plumbing.
/// Launcher updates are deliberately isolated from ComfyUI/venv updates.
/// </summary>
public sealed class LauncherUpdateService
{
    private const string DefaultRepositoryUrl = "https://github.com/blkot/XantaComfyLauncher";
    private readonly Func<LauncherConfig> _configAccessor;
    private UpdateManager? _manager;
    private string? _managerKey;

    public LauncherUpdateService(Func<LauncherConfig> configAccessor) =>
        _configAccessor = configAccessor;

    public string Channel
    {
        get
        {
            var config = _configAccessor();
            return string.IsNullOrWhiteSpace(config.LauncherUpdateChannel)
                ? "stable"
                : config.LauncherUpdateChannel.Trim().ToLowerInvariant();
        }
    }

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public bool IsInstalled => TryGetManager()?.IsInstalled == true;

    public async Task<LauncherUpdateCheckResult> CheckForUpdatesAsync()
    {
        var manager = TryGetManager()
            ?? throw new InvalidOperationException("启动器更新源无效。");

        if (!manager.IsInstalled)
        {
            return new LauncherUpdateCheckResult(
                IsInstalled: false,
                CurrentVersion: CurrentVersion,
                Channel: Channel,
                Update: null,
                AvailableVersion: null);
        }

        UpdateInfo? update;
        try
        {
            update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "启动器更新源返回 404。若 GitHub 仓库是私有的，匿名客户端无法读取 Releases；" +
                "请把更新包放到公开只读静态源并设置 XANTA_LAUNCHER_UPDATE_FEED，" +
                "或为本机设置只读的 XANTA_LAUNCHER_GITHUB_TOKEN。", ex);
        }

        return new LauncherUpdateCheckResult(
            IsInstalled: true,
            CurrentVersion: CurrentVersion,
            Channel: Channel,
            Update: update,
            AvailableVersion: update?.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadAsync(
        UpdateInfo update,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var manager = TryGetManager()
            ?? throw new InvalidOperationException("启动器更新源无效。");
        await manager.DownloadUpdatesAsync(
            update,
            percent => progress?.Report(percent),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the current process, applies the downloaded package, and starts
    /// the new launcher version. This method does not return on success.
    /// </summary>
    public void ApplyAndRestart(UpdateInfo update, string[]? restartArgs = null)
    {
        var manager = TryGetManager()
            ?? throw new InvalidOperationException("启动器更新源无效。");
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease, restartArgs ?? Array.Empty<string>());
    }

    private UpdateManager? TryGetManager()
    {
        var channel = Channel;
        var feedUrl = ReadFirstEnvironmentVariable(
            "XANTA_LAUNCHER_UPDATE_FEED",
            "COMFY_LAUNCHER_UPDATE_FEED");
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            feedUrl = _configAccessor().UpdateFeedUrl;
        }

        var managerKey = channel + "|" + feedUrl;
        if (_manager != null && string.Equals(_managerKey, managerKey, StringComparison.Ordinal))
        {
            return _manager;
        }

        _manager = null;
        _managerKey = managerKey;

        var options = new UpdateOptions
        {
            ExplicitChannel = channel,
            AllowVersionDowngrade = false
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(feedUrl))
            {
                _manager = new UpdateManager(feedUrl.Trim(), options, null);
            }
            else
            {
                var token = Environment.GetEnvironmentVariable("XANTA_LAUNCHER_GITHUB_TOKEN");
                var source = new GithubSource(
                    DefaultRepositoryUrl,
                    string.IsNullOrWhiteSpace(token) ? null : token.Trim(),
                    string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase));
                _manager = new UpdateManager(source, options, null);
            }
        }
        catch
        {
            _manager = null;
        }

        return _manager;
    }

    private static string? ReadFirstEnvironmentVariable(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}
