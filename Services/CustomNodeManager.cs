using System.Security.Cryptography;
using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class CustomNodeManager
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public CustomNodeManager(AppPaths paths) => _paths = paths;

    public string CustomNodesDir => Path.Combine(_paths.ComfyDir, "custom_nodes");
    public string ManifestFile => _paths.CustomNodeManifestFile;

    public async Task<CustomNodeManifest> LoadOrCreateManifestAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _paths.EnsureDirectories();
        if (File.Exists(ManifestFile))
        {
            try
            {
                await using var stream = File.OpenRead(ManifestFile);
                var manifest = await JsonSerializer.DeserializeAsync<CustomNodeManifest>(stream, cancellationToken: ct)
                    .ConfigureAwait(false);
                if (manifest != null)
                {
                    await SyncManifestWithFoldersAsync(manifest, progress, ct).ConfigureAwait(false);
                    return manifest;
                }
            }
            catch
            {
                // Rebuild below.
            }
        }

        var rebuilt = new CustomNodeManifest();
        await SyncManifestWithFoldersAsync(rebuilt, progress, ct).ConfigureAwait(false);
        await SaveManifestAsync(rebuilt, ct).ConfigureAwait(false);
        return rebuilt;
    }

    public async Task<IReadOnlyList<CustomNodeState>> GetStatesAsync(
        bool checkRemote,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await LoadOrCreateManifestAsync(progress, ct).ConfigureAwait(false);
        var definitions = manifest.Nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var states = new CustomNodeState[definitions.Count];

        // Local status is cheap; run with bounded parallelism so the UI can populate quickly.
        using (var localGate = new SemaphoreSlim(8))
        {
            var localTasks = definitions.Select(async (definition, index) =>
            {
                await localGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    states[index] = await GetLocalStateAsync(definition, ct).ConfigureAwait(false);
                }
                finally
                {
                    localGate.Release();
                }
            });
            await Task.WhenAll(localTasks).ConfigureAwait(false);
        }

        if (checkRemote)
        {
            // Network checks are safe to parallelize across different repositories.
            // Keep the concurrency bounded to avoid GitHub throttling and disk/process storms.
            using var remoteGate = new SemaphoreSlim(4);
            var remoteTasks = states.Select(async state =>
            {
                if (!state.IsInstalled || state.State == CustomNodeInstallState.NotGit) return;
                await remoteGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await CheckRemoteStateAsync(state, progress, ct).ConfigureAwait(false);
                }
                finally
                {
                    remoteGate.Release();
                }
            });
            await Task.WhenAll(remoteTasks).ConfigureAwait(false);
        }

        manifest.UpdatedAt = DateTimeOffset.Now;
        await SaveManifestAsync(manifest, ct).ConfigureAwait(false);
        return states;
    }

    private async Task<CustomNodeState> GetLocalStateAsync(
        CustomNodeDefinition definition,
        CancellationToken ct)
    {
        var path = GetNodePath(definition.Name);
        var state = new CustomNodeState { Definition = definition, Path = path };
        if (!Directory.Exists(path))
        {
            state.State = CustomNodeInstallState.Missing;
            return state;
        }
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            state.State = CustomNodeInstallState.NotGit;
            return state;
        }

        state.IsInstalled = true;
        state.InstalledCommit = (await GitAsync(path, new[] { "rev-parse", "HEAD" }, ct)
            .ConfigureAwait(false)).StandardOutput.Trim();
        state.LatestTag = definition.LatestTag;
        definition.InstalledCommit = state.InstalledCommit;

        var dirty = (await GitAsync(path, new[] { "status", "--porcelain", "--untracked-files=no" }, ct)
            .ConfigureAwait(false)).StandardOutput;
        state.IsDirty = !string.IsNullOrWhiteSpace(dirty);
        state.State = state.IsDirty ? CustomNodeInstallState.Dirty : CustomNodeInstallState.Installed;
        return state;
    }

    private async Task CheckRemoteStateAsync(
        CustomNodeState state,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var definition = state.Definition;
        progress?.Report($"检查更新：{definition.Name}");
        try
        {
            if (string.IsNullOrWhiteSpace(definition.Branch))
            {
                definition.Branch = await DetectDefaultBranchAsync(state.Path, ct).ConfigureAwait(false);
            }
            var latest = await GetRemoteBranchCommitAsync(state.Path, definition.Branch, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                definition.LatestCommit = latest;
                definition.LastCheckedAt = DateTimeOffset.Now;
                definition.LastError = null;
                state.LatestCommit = latest;
                state.UpdateAvailable = !string.Equals(
                    definition.InstalledCommit, latest, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                definition.LatestTag = await GetLatestRemoteTagAsync(state.Path, isLocalPath: true, ct)
                    .ConfigureAwait(false);
                state.LatestTag = definition.LatestTag;
            }
            catch
            {
                // Tag lookup is optional and must not break normal update checks.
                definition.LatestTag = null;
                state.LatestTag = null;
            }

            if (state.IsDirty)
            {
                state.State = CustomNodeInstallState.Dirty;
            }
            else if (state.UpdateAvailable)
            {
                state.State = CustomNodeInstallState.UpdateAvailable;
            }
            else
            {
                state.State = CustomNodeInstallState.Installed;
            }
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
            definition.LastError = ex.Message;
            state.State = CustomNodeInstallState.Error;
        }
    }

    public Task<CustomNodeState> InstallOrUpdateAsync(
        CustomNodeDefinition definition,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return InstallOrUpdateAsync(definition, NodeUpdateTarget.LatestCommit, progress, ct);
    }

    public async Task<CustomNodeState> InstallOrUpdateAsync(
        CustomNodeDefinition definition,
        NodeUpdateTarget target,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetNodePath(definition.Name);
            var isNewInstall = !Directory.Exists(path);
            var state = new CustomNodeState { Definition = definition, Path = path };
            var requirementsBeforeHash = "";
            var selectedTag = "";

            if (isNewInstall)
            {
                Directory.CreateDirectory(CustomNodesDir);
                if (target == NodeUpdateTarget.LatestTag)
                {
                    selectedTag = await GetLatestRemoteTagAsync(definition.Repository, isLocalPath: false, ct)
                        .ConfigureAwait(false) ?? "";
                    if (string.IsNullOrWhiteSpace(selectedTag))
                    {
                        throw new InvalidOperationException($"{definition.Name} 的仓库没有可用 tag。");
                    }

                    progress?.Report($"安装 {definition.Name} 到 tag {selectedTag}...");
                    var tagClone = await ProcessRunner.RunAsync(
                        "git",
                        new[] { "clone", "--depth", "1", "--branch", selectedTag, definition.Repository, path },
                        _paths.InstanceRoot,
                        onOutput: line => progress?.Report("[git] " + line),
                        onError: line => progress?.Report("[git] " + line),
                        cancellationToken: ct).ConfigureAwait(false);
                    if (!tagClone.Success)
                    {
                        if (Directory.Exists(path))
                        {
                            try { Directory.Delete(path, recursive: true); } catch { }
                        }
                        throw new InvalidOperationException($"安装 tag 失败：{Tail(tagClone.StandardError, 3000)}");
                    }
                }
                else
                {
                    progress?.Report($"安装 {definition.Name}...");
                    var branch = string.IsNullOrWhiteSpace(definition.Branch) ? "main" : definition.Branch;
                    var clone = await ProcessRunner.RunAsync(
                        "git",
                        new[] { "clone", "--depth", "1", "--branch", branch, definition.Repository, path },
                        _paths.InstanceRoot,
                        onOutput: line => progress?.Report("[git] " + line),
                        onError: line => progress?.Report("[git] " + line),
                        cancellationToken: ct).ConfigureAwait(false);

                    if (!clone.Success)
                    {
                        // Some repositories do not use the guessed branch. Let Git choose the remote default.
                        if (Directory.Exists(path))
                        {
                            try { Directory.Delete(path, recursive: true); } catch { }
                        }
                        clone = await ProcessRunner.RunAsync(
                            "git",
                            new[] { "clone", "--depth", "1", definition.Repository, path },
                            _paths.InstanceRoot,
                            onOutput: line => progress?.Report("[git] " + line),
                            onError: line => progress?.Report("[git] " + line),
                            cancellationToken: ct).ConfigureAwait(false);
                    }
                    if (!clone.Success)
                    {
                        throw new InvalidOperationException($"安装失败：{Tail(clone.StandardError, 3000)}");
                    }
                }
            }
            else
            {
                if (!Directory.Exists(Path.Combine(path, ".git")))
                {
                    throw new InvalidOperationException($"{definition.Name} 已存在但不是 Git 仓库，已拒绝覆盖。");
                }

                var dirty = (await GitAsync(path, new[] { "status", "--porcelain", "--untracked-files=no" }, ct)
                    .ConfigureAwait(false)).StandardOutput;
                if (!string.IsNullOrWhiteSpace(dirty))
                {
                    throw new InvalidOperationException($"{definition.Name} 存在本地修改，已阻止更新：\n{dirty}");
                }

                requirementsBeforeHash = await HashRequirementsAsync(path, definition.RequirementsFile, ct)
                    .ConfigureAwait(false);

                if (target == NodeUpdateTarget.LatestTag)
                {
                    selectedTag = await GetLatestRemoteTagAsync(path, isLocalPath: true, ct)
                        .ConfigureAwait(false) ?? "";
                    if (string.IsNullOrWhiteSpace(selectedTag))
                    {
                        throw new InvalidOperationException($"{definition.Name} 的仓库没有可用 tag。");
                    }

                    progress?.Report($"更新 {definition.Name} 到 tag {selectedTag}...");
                    var tagFetch = await GitAsync(path, new[]
                    {
                        "fetch", "--depth", "1", "--force", "origin",
                        $"refs/tags/{selectedTag}:refs/tags/{selectedTag}"
                    }, ct).ConfigureAwait(false);
                    if (tagFetch.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"git fetch tag 失败：{Tail(tagFetch.StandardError, 3000)}");
                    }
                    var tagCommit = await GitAsync(path, new[] { "rev-parse", $"refs/tags/{selectedTag}^{{commit}}" }, ct)
                        .ConfigureAwait(false);
                    if (tagCommit.ExitCode != 0 || string.IsNullOrWhiteSpace(tagCommit.StandardOutput))
                    {
                        throw new InvalidOperationException($"无法解析 tag {selectedTag} 的提交。");
                    }
                    var checkout = await GitAsync(path, new[] { "checkout", "--detach", tagCommit.StandardOutput.Trim() }, ct)
                        .ConfigureAwait(false);
                    if (checkout.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"git checkout tag 失败：{Tail(checkout.StandardError, 3000)}");
                    }
                }
                else
                {
                    progress?.Report($"更新 {definition.Name} 到最新提交...");
                    var branch = string.IsNullOrWhiteSpace(definition.Branch)
                        ? await DetectDefaultBranchAsync(path, ct).ConfigureAwait(false)
                        : definition.Branch;
                    definition.Branch = branch;

                    var fetch = await GitAsync(path, new[] { "fetch", "--depth", "1", "--force", "origin", branch }, ct)
                        .ConfigureAwait(false);
                    if (fetch.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"git fetch 失败：{Tail(fetch.StandardError, 3000)}");
                    }
                    var commit = await GitAsync(path, new[] { "rev-parse", "FETCH_HEAD" }, ct).ConfigureAwait(false);
                    if (commit.ExitCode != 0 || string.IsNullOrWhiteSpace(commit.StandardOutput))
                    {
                        throw new InvalidOperationException("无法解析远程分支提交。");
                    }
                    var targetCommit = commit.StandardOutput.Trim();
                    var checkout = await GitAsync(path, new[] { "checkout", "--detach", targetCommit }, ct)
                        .ConfigureAwait(false);
                    if (checkout.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"git checkout 失败：{Tail(checkout.StandardError, 3000)}");
                    }
                }
            }

            var installedCommit = (await GitAsync(path, new[] { "rev-parse", "HEAD" }, ct)
                .ConfigureAwait(false)).StandardOutput.Trim();
            definition.InstalledCommit = installedCommit;
            definition.InstalledTag = target == NodeUpdateTarget.LatestTag ? selectedTag : null;
            if (target == NodeUpdateTarget.LatestTag) definition.LatestTag = selectedTag;
            definition.LatestCommit = installedCommit;
            definition.LastCheckedAt = DateTimeOffset.Now;
            definition.LastError = null;

            if (string.IsNullOrWhiteSpace(definition.RequirementsFile) &&
                File.Exists(Path.Combine(path, "requirements.txt")))
            {
                definition.RequirementsFile = "requirements.txt";
            }

            if (!string.IsNullOrWhiteSpace(definition.RequirementsFile))
            {
                var requirementsPath = Path.Combine(path, definition.RequirementsFile);
                var requirementsAfterHash = await HashRequirementsAsync(path, definition.RequirementsFile, ct)
                    .ConfigureAwait(false);
                if (isNewInstall || !string.Equals(requirementsBeforeHash, requirementsAfterHash, StringComparison.Ordinal))
                {
                    progress?.Report($"安装 {definition.Name} 依赖...");
                    var pip = await ProcessRunner.RunAsync(
                        _paths.PythonExe,
                        new[] { "-m", "pip", "install", "-r", requirementsPath, "--disable-pip-version-check" },
                        _paths.ComfyDir,
                        InstanceEnvironment.Create(_paths),
                        line => progress?.Report("[pip] " + line),
                        line => progress?.Report("[pip] " + line),
                        ct).ConfigureAwait(false);
                    if (!pip.Success)
                    {
                        definition.LastError = "依赖安装失败：" + Tail(pip.StandardError, 3000);
                        throw new InvalidOperationException(definition.LastError);
                    }
                }
            }

            var manifest = await LoadManifestOnlyAsync(ct).ConfigureAwait(false);
            var entry = manifest.Nodes.FirstOrDefault(n =>
                string.Equals(n.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                manifest.Nodes.Add(definition);
            }
            else
            {
                entry.Repository = definition.Repository;
                entry.Branch = definition.Branch;
                entry.InstalledCommit = definition.InstalledCommit;
                entry.InstalledTag = definition.InstalledTag;
                entry.LatestTag = definition.LatestTag;
                entry.LatestCommit = definition.LatestCommit;
                entry.LastCheckedAt = definition.LastCheckedAt;
                entry.LastError = definition.LastError;
                entry.RequirementsFile = definition.RequirementsFile;
                entry.Enabled = definition.Enabled;
            }
            manifest.UpdatedAt = DateTimeOffset.Now;
            await SaveManifestAsync(manifest, ct).ConfigureAwait(false);

            state.IsInstalled = true;
            state.InstalledCommit = installedCommit;
            state.LatestCommit = installedCommit;
            state.LatestTag = definition.LatestTag;
            state.State = CustomNodeInstallState.Installed;
            return state;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<(CustomNodeDefinition Definition, string? Error)>> InstallMissingAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await LoadOrCreateManifestAsync(progress, ct).ConfigureAwait(false);
        var results = new List<(CustomNodeDefinition Definition, string? Error)>();
        foreach (var definition in manifest.Nodes.Where(n => n.Enabled))
        {
            var path = GetNodePath(definition.Name);
            if (Directory.Exists(path) && Directory.Exists(Path.Combine(path, ".git"))) continue;
            try
            {
                await InstallOrUpdateAsync(definition, progress, ct).ConfigureAwait(false);
                results.Add((definition, null));
            }
            catch (Exception ex)
            {
                definition.LastError = ex.Message;
                results.Add((definition, ex.Message));
            }
        }
        return results;
    }

    public Task<IReadOnlyList<(CustomNodeDefinition Definition, string? Error)>> UpdateAllAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return UpdateAllAsync(NodeUpdateTarget.LatestCommit, progress, ct);
    }

    public async Task<IReadOnlyList<(CustomNodeDefinition Definition, string? Error)>> UpdateAllAsync(
        NodeUpdateTarget target,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await LoadOrCreateManifestAsync(progress, ct).ConfigureAwait(false);
        var results = new List<(CustomNodeDefinition, string?)>();
        foreach (var definition in manifest.Nodes.Where(n => n.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await InstallOrUpdateAsync(definition, target, progress, ct).ConfigureAwait(false);
                results.Add((definition, null));
            }
            catch (Exception ex)
            {
                definition.LastError = ex.Message;
                results.Add((definition, ex.Message));
                progress?.Report($"[error] {definition.Name}: {ex.Message}");
            }
        }
        return results;
    }

    public async Task SaveManifestAsync(CustomNodeManifest manifest, CancellationToken ct = default)
    {
        _paths.EnsureDirectories();
        manifest.UpdatedAt = DateTimeOffset.Now;
        await using var stream = File.Create(ManifestFile);
        await JsonSerializer.SerializeAsync(stream, manifest, _jsonOptions, ct).ConfigureAwait(false);
    }

    private async Task<CustomNodeManifest> LoadManifestOnlyAsync(CancellationToken ct)
    {
        if (!File.Exists(ManifestFile)) return new CustomNodeManifest();
        await using var stream = File.OpenRead(ManifestFile);
        return await JsonSerializer.DeserializeAsync<CustomNodeManifest>(stream, cancellationToken: ct)
            .ConfigureAwait(false) ?? new CustomNodeManifest();
    }

    private async Task SyncManifestWithFoldersAsync(
        CustomNodeManifest manifest,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(CustomNodesDir)) return;
        var existing = manifest.Nodes.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(CustomNodesDir))
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) continue;
            var name = Path.GetFileName(dir);
            if (existing.ContainsKey(name)) continue;
            progress?.Report($"发现已安装节点：{name}");
            var definition = new CustomNodeDefinition
            {
                Name = name,
                Repository = (await GitAsync(dir, new[] { "remote", "get-url", "origin" }, ct).ConfigureAwait(false))
                    .StandardOutput.Trim(),
                Branch = await DetectDefaultBranchAsync(dir, ct).ConfigureAwait(false),
                InstalledCommit = (await GitAsync(dir, new[] { "rev-parse", "HEAD" }, ct).ConfigureAwait(false))
                    .StandardOutput.Trim(),
                InstalledTag = await TryGetInstalledTagAsync(dir, ct).ConfigureAwait(false),
                RequirementsFile = File.Exists(Path.Combine(dir, "requirements.txt")) ? "requirements.txt" : null,
                Enabled = true,
                UpdatePolicy = "notify"
            };
            manifest.Nodes.Add(definition);
            existing[name] = definition;
        }
        manifest.Nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetNodePath(string name) => Path.Combine(CustomNodesDir, name);

    private async Task<string> DetectDefaultBranchAsync(string repoPath, CancellationToken ct)
    {
        var current = await GitAsync(repoPath, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, ct).ConfigureAwait(false);
        var branch = current.StandardOutput.Trim();
        if (current.ExitCode == 0 && !string.IsNullOrWhiteSpace(branch) && branch != "HEAD")
        {
            return branch;
        }

        var remote = await GitAsync(repoPath, new[] { "ls-remote", "--symref", "origin", "HEAD" }, ct)
            .ConfigureAwait(false);
        if (remote.ExitCode == 0)
        {
            foreach (var line in remote.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"^ref:\s+refs/heads/(\S+)\s+HEAD");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        return "main";
    }

    private async Task<string?> GetLatestRemoteTagAsync(
        string repositoryOrPath,
        bool isLocalPath,
        CancellationToken ct)
    {
        var args = new List<string>();
        if (isLocalPath)
        {
            args.Add("-C");
            args.Add(repositoryOrPath);
        }
        args.AddRange(new[] { "ls-remote", "--tags", "--refs", "--sort=-v:refname" });
        args.Add(isLocalPath ? "origin" : repositoryOrPath);

        var result = await ProcessRunner.RunAsync(
            "git", args, _paths.InstanceRoot, cancellationToken: ct).ConfigureAwait(false);
        if (!result.Success)
        {
            args.Remove("--sort=-v:refname");
            result = await ProcessRunner.RunAsync(
                "git", args, _paths.InstanceRoot, cancellationToken: ct).ConfigureAwait(false);
        }
        if (!result.Success) return null;

        var tags = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Trim().Split('\t', 2);
                return parts.Length == 2 && parts[1].StartsWith("refs/tags/", StringComparison.Ordinal)
                    ? parts[1]["refs/tags/".Length..]
                    : "";
            })
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToList();
        if (tags.Count == 0) return null;

        // --sort=-v:refname is supported by modern Git. If Git ignored it,
        // prefer a semantic version tag, otherwise keep the last remote ref.
        return tags.FirstOrDefault(tag => SemVer.TryParse(tag, out _, out _)) ?? tags[^1];
    }

    private async Task<string?> TryGetInstalledTagAsync(string repoPath, CancellationToken ct)
    {
        var result = await GitAsync(repoPath, new[] { "describe", "--tags", "--exact-match", "HEAD" }, ct)
            .ConfigureAwait(false);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    private async Task<string> GetRemoteBranchCommitAsync(string repoPath, string branch, CancellationToken ct)
    {
        var result = await GitAsync(repoPath, new[] { "ls-remote", "origin", $"refs/heads/{branch}" }, ct)
            .ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
        var line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line?.Split('\t')[0].Trim() ?? "";
    }

    private async Task<ProcessResult> GitAsync(string repoPath, IEnumerable<string> args, CancellationToken ct)
    {
        return await ProcessRunner.RunAsync(
            "git",
            new[] { "--no-pager", "-C", repoPath }.Concat(args),
            _paths.InstanceRoot,
            cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<string> HashRequirementsAsync(string repoPath, string? requirementsFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requirementsFile)) return "";
        var path = Path.Combine(repoPath, requirementsFile);
        if (!File.Exists(path)) return "";
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Tail(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[^max..];
}
