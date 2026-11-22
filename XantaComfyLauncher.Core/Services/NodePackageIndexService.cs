using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class NodePackageIndexService
{
    private readonly AppPaths _paths;
    private readonly CustomNodeManager _manager;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public NodePackageIndexService(AppPaths paths)
    {
        _paths = paths;
        _manager = new CustomNodeManager(paths);
    }

    public string IndexFile => _paths.NodePackageMapFile;

    public NodePackageMap? Load()
    {
        try
        {
            if (!File.Exists(IndexFile)) return null;
            var json = File.ReadAllText(IndexFile);
            return JsonSerializer.Deserialize<NodePackageMap>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<NodePackageMap> LoadOrBuildAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var map = Load() ?? new NodePackageMap();
        await RefreshAliasesAsync(map, progress, ct).ConfigureAwait(false);
        return map;
    }

    public async Task<NodePackageMap> RefreshFromObjectInfoAsync(
        string baseUrl,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("正在读取 ComfyUI /object_info...");
        using var api = new ComfyApiService(baseUrl);
        var modules = await api.GetObjectInfoModulesAsync(ct).ConfigureAwait(false);
        var map = Load() ?? new NodePackageMap();
        map.NodePackages.Clear();
        map.CoreNodeTypes.Clear();
        foreach (var pair in modules)
        {
            var package = ResolveModulePackage(pair.Value);
            if (!string.IsNullOrWhiteSpace(package))
                map.NodePackages[pair.Key] = package;
            else
                map.CoreNodeTypes.Add(pair.Key);
        }
        map.GeneratedAt = DateTimeOffset.Now;
        map.Source = baseUrl;
        await RefreshAliasesAsync(map, progress, ct).ConfigureAwait(false);
        Save(map);
        progress?.Report($"节点索引完成：{map.NodePackages.Count} 个后端节点，{map.CnrIdPackages.Count} 个包别名。");
        return map;
    }

    public async Task<IReadOnlyList<string>> GetInstalledPackageNamesAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = await _manager.LoadOrCreateManifestAsync(progress, ct).ConfigureAwait(false);
        foreach (var node in manifest.Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Name)) names.Add(node.Name);
        }

        try
        {
            if (Directory.Exists(_manager.CustomNodesDir))
            {
                foreach (var dir in Directory.GetDirectories(_manager.CustomNodesDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name) || name == "__pycache__" ||
                        name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (File.Exists(Path.Combine(dir, ".git")) || Directory.Exists(Path.Combine(dir, ".git")) ||
                        File.Exists(Path.Combine(dir, "__init__.py")))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch
        {
            // Best effort discovery.
        }
        return names.ToList();
    }

    private async Task RefreshAliasesAsync(
        NodePackageMap map,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var manifest = await _manager.LoadOrCreateManifestAsync(progress, ct).ConfigureAwait(false);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddAlias(string? alias, string package)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(package)) return;
            aliases[alias.Trim()] = package;
        }

        foreach (var node in manifest.Nodes)
        {
            var package = node.Name;
            if (string.IsNullOrWhiteSpace(package)) continue;
            AddAlias(package, package);
            AddAlias(Path.GetFileNameWithoutExtension(package), package);

            var repo = (node.Repository ?? "").Trim();
            if (string.IsNullOrWhiteSpace(repo)) continue;
            AddAlias(repo, package);
            AddAlias(repo.TrimEnd('/').Replace(".git", "", StringComparison.OrdinalIgnoreCase), package);
            var repoName = repo.TrimEnd('/').Split('/', '\\').LastOrDefault() ?? "";
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repoName = repoName[..^4];
            AddAlias(repoName, package);

            var match = System.Text.RegularExpressions.Regex.Match(repo,
                @"github\.com[:/](?<owner>[^/\s]+)/(?<name>[^/\s]+?)(?:\.git)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                AddAlias($"{match.Groups["owner"].Value}/{match.Groups["name"].Value}", package);
                AddAlias(match.Groups["name"].Value, package);
            }
        }

        try
        {
            if (Directory.Exists(_manager.CustomNodesDir))
            {
                foreach (var dir in Directory.GetDirectories(_manager.CustomNodesDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name) || name == "__pycache__" ||
                        name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    AddAlias(name, name);
                }
            }
        }
        catch
        {
            // Best effort.
        }

        map.CnrIdPackages = aliases;
    }

    private void Save(NodePackageMap map)
    {
        _paths.EnsureDirectories();
        File.WriteAllText(IndexFile, JsonSerializer.Serialize(map, _jsonOptions));
    }

    private static string? ResolveModulePackage(string module)
    {
        const string prefix = "custom_nodes.";
        if (!module.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = module[prefix.Length..];
        var dot = rest.IndexOf('.');
        return dot >= 0 ? rest[..dot] : rest;
    }
}
