using System.Security.Cryptography;
using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class ComfyVersionSwitcher
{
    private readonly AppPaths _paths;
    private readonly GitService _git;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ComfyVersionSwitcher(AppPaths paths, GitService git)
    {
        _paths = paths;
        _git = git;
    }

    public async Task SwitchAsync(
        ComfyVersionInfo version,
        bool updateDependencies,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        GitService.ValidateTag(version.Tag);

        var dirty = await _git.GetTrackedStatusAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dirty))
        {
            throw new InvalidOperationException(
                "ComfyUI 仓库存在已跟踪文件修改，已阻止版本切换。请先提交或撤销这些修改。\n\n" + dirty);
        }

        var previousCommit = await _git.GetCurrentCommitAsync(ct).ConfigureAwait(false);
        var previousTag = await _git.GetCurrentTagAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(previousCommit))
        {
            throw new InvalidOperationException("无法读取当前 ComfyUI 提交。");
        }

        var requirementsBefore = await HashRequirementsAsync(ct).ConfigureAwait(false);
        var state = new VersionSwitchState
        {
            PreviousCommit = previousCommit,
            PreviousTag = previousTag,
            TargetTag = version.Tag,
            RequirementsHashBefore = requirementsBefore,
            SwitchedAt = DateTimeOffset.Now
        };
        await SaveStateAsync(state, ct).ConfigureAwait(false);
        await SavePipFreezeSnapshotAsync(previousTag, ct).ConfigureAwait(false);

        var checkoutDone = false;
        try
        {
            progress?.Report($"正在获取 {version.Tag}...");
            var targetCommit = await _git.FetchTagAsync(version.Tag, ct).ConfigureAwait(false);
            state.TargetCommit = targetCommit;
            await SaveStateAsync(state, ct).ConfigureAwait(false);

            progress?.Report($"正在切换到 {version.Tag} ({targetCommit[..Math.Min(8, targetCommit.Length)]})...");
            await _git.CheckoutAsync(targetCommit, ct).ConfigureAwait(false);
            checkoutDone = true;

            var requirementsAfter = await HashRequirementsAsync(ct).ConfigureAwait(false);
            state.RequirementsHashAfter = requirementsAfter;
            await SaveStateAsync(state, ct).ConfigureAwait(false);

            if (updateDependencies && !string.Equals(requirementsBefore, requirementsAfter, StringComparison.Ordinal))
            {
                progress?.Report("requirements.txt 已变化，正在同步 Python 依赖...");
                await InstallRequirementsAsync(progress, ct).ConfigureAwait(false);
            }
            else if (!updateDependencies)
            {
                progress?.Report("已跳过依赖同步（按用户设置）。");
            }
            else
            {
                progress?.Report("requirements.txt 未变化，跳过依赖同步。");
            }

            progress?.Report("正在执行核心启动自检...");
            await QuickVerifyAsync(progress, ct).ConfigureAwait(false);
            progress?.Report($"已切换到 {version.Tag}。");
        }
        catch (Exception ex)
        {
            if (checkoutDone)
            {
                progress?.Report("切换失败，正在回滚到切换前提交...");
                try
                {
                    await RollbackInternalAsync(state, updateDependencies, progress, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    throw new InvalidOperationException(
                        $"版本切换失败，且自动回滚也失败。原始错误：{ex.Message}；回滚错误：{rollbackEx.Message}",
                        new AggregateException(ex, rollbackEx));
                }

                throw new InvalidOperationException(
                    $"版本切换失败，已回滚到 {Short(previousCommit)}。原因：{ex.Message}", ex);
            }
            throw;
        }
    }

    public async Task<VersionSwitchState?> GetLastStateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.VersionStateFile)) return null;
        try
        {
            await using var stream = File.OpenRead(_paths.VersionStateFile);
            return await JsonSerializer.DeserializeAsync<VersionSwitchState>(stream, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task RollbackAsync(
        bool updateDependencies,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var state = await GetLastStateAsync(ct).ConfigureAwait(false);
        if (state == null || string.IsNullOrWhiteSpace(state.PreviousCommit))
        {
            throw new InvalidOperationException("没有可用的版本切换记录。");
        }

        var dirty = await _git.GetTrackedStatusAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dirty))
        {
            throw new InvalidOperationException(
                "ComfyUI 仓库存在已跟踪文件修改，已阻止回滚。请先提交或撤销这些修改。\n\n" + dirty);
        }

        await RollbackInternalAsync(state, updateDependencies, progress, ct).ConfigureAwait(false);
        state.RolledBackAt = DateTimeOffset.Now;
        await SaveStateAsync(state, ct).ConfigureAwait(false);
        progress?.Report("已回滚到切换前版本。");
    }

    private async Task RollbackInternalAsync(
        VersionSwitchState state,
        bool updateDependencies,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        progress?.Report($"正在回滚到 {Short(state.PreviousCommit)}...");
        await _git.CheckoutAsync(state.PreviousCommit, ct).ConfigureAwait(false);

        if (updateDependencies)
        {
            var currentHash = await HashRequirementsAsync(ct).ConfigureAwait(false);
            if (!string.Equals(currentHash, state.RequirementsHashBefore, StringComparison.Ordinal))
            {
                progress?.Report("正在恢复切换前的 Python 依赖...");
                await InstallRequirementsAsync(progress, ct).ConfigureAwait(false);
            }
        }

        await QuickVerifyAsync(progress, ct).ConfigureAwait(false);
    }

    private async Task InstallRequirementsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(
            _paths.PythonExe,
            new[] { "-m", "pip", "install", "-r", _paths.RequirementsFile, "--disable-pip-version-check" },
            _paths.ComfyDir,
            InstanceEnvironment.Create(_paths),
            line => progress?.Report("[pip] " + line),
            line => progress?.Report("[pip] " + line),
            ct).ConfigureAwait(false);

        if (!result.Success)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException("依赖安装失败：" + Tail(detail, 4000));
        }
    }

    private async Task QuickVerifyAsync(IProgress<string>? progress, CancellationToken ct)
    {
        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        verifyCts.CancelAfter(TimeSpan.FromMinutes(3));
        var result = await ProcessRunner.RunAsync(
            _paths.PythonExe,
            new[]
            {
                _paths.MainScript,
                "--quick-test-for-ci",
                "--disable-all-custom-nodes",
                "--temp-directory", _paths.ComfyTempDir
            },
            _paths.ComfyDir,
            InstanceEnvironment.Create(_paths),
            line => progress?.Report("[verify] " + line),
            line => progress?.Report("[verify] " + line),
            verifyCts.Token).ConfigureAwait(false);

        if (!result.Success)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException("ComfyUI 核心启动自检失败：" + Tail(detail, 4000));
        }
    }

    private async Task SavePipFreezeSnapshotAsync(string previousTag, CancellationToken ct)
    {
        try
        {
            var tag = string.IsNullOrWhiteSpace(previousTag) ? "detached" : previousTag;
            var safeTag = tag.Replace('/', '_').Replace('\\', '_');
            var path = Path.Combine(_paths.SnapshotsDir,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeTag}-pip-freeze.txt");
            var result = await ProcessRunner.RunAsync(
                _paths.PythonExe,
                new[] { "-m", "pip", "freeze" },
                _paths.ComfyDir,
                InstanceEnvironment.Create(_paths),
                cancellationToken: ct).ConfigureAwait(false);
            if (result.Success) await File.WriteAllTextAsync(path, result.StandardOutput, ct).ConfigureAwait(false);
        }
        catch
        {
            // Snapshot is diagnostic only.
        }
    }

    private async Task<string> HashRequirementsAsync(CancellationToken ct)
    {
        if (!File.Exists(_paths.RequirementsFile)) return "";
        var bytes = await File.ReadAllBytesAsync(_paths.RequirementsFile, ct).ConfigureAwait(false);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private async Task SaveStateAsync(VersionSwitchState state, CancellationToken ct)
    {
        _paths.EnsureDirectories();
        await using var stream = File.Create(_paths.VersionStateFile);
        await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, ct).ConfigureAwait(false);
    }

    private static string Short(string commit) => commit.Length > 8 ? commit[..8] : commit;

    private static string Tail(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[^max..];
}
