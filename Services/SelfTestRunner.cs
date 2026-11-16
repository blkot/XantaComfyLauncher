using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

internal static class SelfTestRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var started = DateTimeOffset.Now;
        var report = new Dictionary<string, object?>();
        var ok = false;
        AppPaths? paths = null;
        try
        {
            paths = ResolvePaths(args, out var config, out var source);
            paths.EnsureDirectories();
            var git = new GitService(paths);
            var probe = new EnvironmentProbeService(paths, git);
            var repo = await git.IsRepositoryAsync();
            var remote = await git.GetRemoteUrlAsync();
            var env = await probe.ProbeAsync();
            var versions = new ComfyVersionService(git);
            var catalog = await versions.GetVersionsAsync(includePrerelease: false);
            var portFree = await probe.IsPortFreeAsync(config.Port);

            report["config_file"] = AppPaths.AppConfigFile;
            report["instance_root"] = paths.InstanceRoot;
            report["path_source"] = source;
            report["git_repository"] = repo;
            report["git_remote"] = remote;
            report["config"] = config;
            report["environment"] = env;
            report["version_count"] = catalog.Versions.Count;
            report["latest_versions"] = catalog.Versions.Take(5).Select(v => new
            {
                v.Tag,
                v.Commit,
                v.IsCurrent,
                v.IsPrerelease
            }).ToList();
            report["port_free"] = portFree;
            report["python_exists"] = File.Exists(paths.PythonExe);
            report["comfy_main_exists"] = File.Exists(paths.MainScript);
            ok = repo && File.Exists(paths.PythonExe) && File.Exists(paths.MainScript) &&
                 catalog.Versions.Count > 0;
        }
        catch (Exception ex)
        {
            DiagnosticLog("self-test exception: " + ex);
            report["exception"] = ex.ToString();
        }

        report["ok"] = ok;
        report["duration_seconds"] = (DateTimeOffset.Now - started).TotalSeconds;
        WriteReport(paths, "self-test.json", report);
        return ok ? 0 : 1;
    }

    public static async Task<int> RunSmokeTestAsync(string[] args)
    {
        var started = DateTimeOffset.Now;
        var report = new Dictionary<string, object?>();
        var logs = new List<string>();
        var ok = false;
        AppPaths? paths = null;
        try
        {
            paths = ResolvePaths(args, out _, out var source);
            paths.EnsureDirectories();
            var config = new LauncherConfig
            {
                InstanceRoot = paths.InstanceRoot,
                Port = 8199,
                ListenMode = ListenMode.Local,
                UseSageAttention = false,
                UseFp16Accumulation = args.Contains("--fp16", StringComparer.OrdinalIgnoreCase),
                AutoOpenBrowser = false,
                UpdateDependenciesOnSwitch = false,
                ExtraArguments = ""
            };

            report["config_file"] = AppPaths.AppConfigFile;
            report["instance_root"] = paths.InstanceRoot;
            report["path_source"] = source;

            using var process = new ComfyProcessService(paths);
            process.LogReceived += line =>
            {
                lock (logs) logs.Add(line);
            };
            process.StateChanged += state =>
            {
                // Reproduce the launcher UI's immediate process-state read.
                var running = process.IsRunning;
                lock (logs) logs.Add($"[test] state={state} running={running}");
            };
            var healthyTcs = new TaskCompletionSource<ComfyRunInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Healthy += info => healthyTcs.TrySetResult(info);

            await process.StartAsync(config);
            var completed = await Task.WhenAny(healthyTcs.Task, Task.Delay(TimeSpan.FromMinutes(3)));
            if (completed == healthyTcs.Task)
            {
                var info = await healthyTcs.Task;
                report["run_info"] = info;
                ok = true;
            }
            else
            {
                report["error"] = "ComfyUI 在 3 分钟内未通过健康检查。";
            }
            await process.StopAsync();
        }
        catch (Exception ex)
        {
            report["exception"] = ex.ToString();
        }

        report["fp16_enabled"] = logs.Any(line =>
            line.Contains("Enabled fp16 accumulation", StringComparison.OrdinalIgnoreCase));
        report["ok"] = ok;
        report["duration_seconds"] = (DateTimeOffset.Now - started).TotalSeconds;
        report["log_tail"] = logs.TakeLast(80).ToList();
        WriteReport(paths, "smoke-test.json", report);
        return ok ? 0 : 1;
    }


    public static async Task<int> RunNodesCheckAsync(string[] args)
    {
        var started = DateTimeOffset.Now;
        var report = new Dictionary<string, object?>();
        var ok = false;
        AppPaths? paths = null;
        try
        {
            paths = ResolvePaths(args, out _, out var source);
            var manager = new CustomNodeManager(paths);
            var manifest = await manager.LoadOrCreateManifestAsync();
            var states = await manager.GetStatesAsync(checkRemote: true);
            var rows = states.Select(s => new
            {
                name = s.Definition.Name,
                repository = s.Definition.Repository,
                branch = s.Definition.Branch,
                installed_commit = s.InstalledCommit,
                latest_commit = s.LatestCommit,
                status = s.StatusText,
                installed = s.IsInstalled,
                dirty = s.IsDirty,
                update_available = s.UpdateAvailable,
                error = s.Error
            }).ToList();

            report["config_file"] = AppPaths.AppConfigFile;
            report["instance_root"] = paths.InstanceRoot;
            report["path_source"] = source;
            report["manifest_file"] = manager.ManifestFile;
            report["node_count"] = manifest.Nodes.Count;
            report["nodes"] = rows;
            report["ok"] = manifest.Nodes.Count > 0 && states.All(s => s.State != CustomNodeInstallState.Error);
            ok = (bool)report["ok"]!;
        }
        catch (Exception ex)
        {
            report["exception"] = ex.ToString();
        }

        report["ok"] = ok;
        report["duration_seconds"] = (DateTimeOffset.Now - started).TotalSeconds;
        if (paths != null)
        {
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(
                Path.Combine(paths.LauncherDataDir, "nodes-check.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return ok ? 0 : 1;
    }


    public static async Task<int> RunNodeUpdateAsync(string[] args)
    {
        var started = DateTimeOffset.Now;
        var report = new Dictionary<string, object?>();
        var ok = false;
        AppPaths? paths = null;
        try
        {
            paths = ResolvePaths(args, out _, out var source);
            var nodeName = AppPaths.ReadOption(args, "--nodes-update");
            if (string.IsNullOrWhiteSpace(nodeName))
                throw new InvalidOperationException("用法：--nodes-update <节点目录名>");

            var manager = new CustomNodeManager(paths);
            var manifest = await manager.LoadOrCreateManifestAsync();
            var definition = manifest.Nodes.FirstOrDefault(n =>
                string.Equals(n.Name, nodeName, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
                throw new InvalidOperationException("清单中找不到节点：" + nodeName);

            var state = await manager.InstallOrUpdateAsync(definition);
            report["config_file"] = AppPaths.AppConfigFile;
            report["instance_root"] = paths.InstanceRoot;
            report["path_source"] = source;
            report["manifest_file"] = manager.ManifestFile;
            report["node"] = definition.Name;
            report["branch"] = definition.Branch;
            report["installed_commit"] = state.InstalledCommit;
            report["ok"] = true;
            ok = true;
        }
        catch (Exception ex)
        {
            report["exception"] = ex.ToString();
        }

        report["ok"] = ok;
        report["duration_seconds"] = (DateTimeOffset.Now - started).TotalSeconds;
        if (paths != null)
        {
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(
                Path.Combine(paths.LauncherDataDir, "nodes-update-result.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return ok ? 0 : 1;
    }

    private static AppPaths ResolvePaths(string[] args, out LauncherConfig config, out string source)
    {
        var configService = new LauncherConfigService();
        config = configService.LoadOrCreate();

        var rootArg = AppPaths.ReadOption(args, "--root");
        if (!string.IsNullOrWhiteSpace(rootArg))
        {
            if (!AppPaths.TryCreate(rootArg, out var fromArg, out var error) || fromArg == null)
            {
                throw new InvalidOperationException("--root 无效：" + error);
            }
            config.InstanceRoot = fromArg.InstanceRoot;
            source = "--root";
            return fromArg;
        }

        if (AppPaths.TryCreate(config.InstanceRoot, out var configured, out var configError) && configured != null)
        {
            source = "config";
            return configured;
        }

        var discovered = AppPaths.Discover(args);
        if (discovered != null)
        {
            config.InstanceRoot = discovered.InstanceRoot;
            source = "discovery";
            return discovered;
        }

        throw new InvalidOperationException(
            "没有配置 ComfyUI 实例路径。请先在启动器中指定路径，或使用 --root <路径>。" + configError);
    }

    private static void WriteReport(AppPaths? paths, string fileName, object report)
    {
        try
        {
            if (paths == null) return;
            paths.EnsureDirectories();
            var path = Path.Combine(paths.LauncherDataDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLog("write report failed: " + ex);
        }
    }

    private static void DiagnosticLog(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "XantaComfyLauncher");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "selftest.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must not affect tests.
        }
    }
}
