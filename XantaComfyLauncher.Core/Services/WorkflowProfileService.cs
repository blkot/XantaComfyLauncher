using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class WorkflowProfileService
{
    private static readonly HashSet<string> CoreFrontendOnlyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Note",
        "MarkdownNote",
        "Reroute",
        "PrimitiveNode"
    };

    private readonly AppPaths _paths;
    private readonly NodePackageIndexService _indexService;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public WorkflowProfileService(AppPaths paths)
    {
        _paths = paths;
        _indexService = new NodePackageIndexService(paths);
    }

    public string CatalogFile => _paths.WorkflowProfilesFile;

    public WorkflowProfileCatalog LoadCatalog()
    {
        try
        {
            if (!File.Exists(CatalogFile)) return new WorkflowProfileCatalog();
            var json = File.ReadAllText(CatalogFile);
            return JsonSerializer.Deserialize<WorkflowProfileCatalog>(json, _jsonOptions)
                ?? new WorkflowProfileCatalog();
        }
        catch
        {
            return new WorkflowProfileCatalog();
        }
    }

    public void SaveCatalog(WorkflowProfileCatalog catalog)
    {
        _paths.EnsureDirectories();
        File.WriteAllText(CatalogFile, JsonSerializer.Serialize(catalog, _jsonOptions));
    }

    public IReadOnlyList<string> GetWorkflowFiles()
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
                 {
                     _paths.WorkflowsDir,
                     Path.Combine(_paths.InstanceRoot, "workflows")
                 })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                    files.Add(file);
                }
            }
            catch
            {
                // Best effort.
            }
        }
        return files.ToList();
    }

    public async Task<WorkflowProfile> AnalyzeAndSaveAsync(
        string workflowPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(workflowPath);
        var references = WorkflowNodeParser.Extract(fullPath);
        if (references.Count == 0)
            throw new InvalidOperationException("工作流中没有解析到节点。请确认这是 ComfyUI 工作流 JSON。");

        progress?.Report("正在读取节点包索引...");
        var map = await _indexService.LoadOrBuildAsync(progress, ct).ConfigureAwait(false);
        var packages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedCnrIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            var resolved = false;
            var cnrId = reference.CnrId.Trim();
            string? unresolvedCnr = null;
            if (!string.IsNullOrWhiteSpace(cnrId) &&
                !string.Equals(cnrId, "comfy-core", StringComparison.OrdinalIgnoreCase))
            {
                var byCnr = map.ResolveCnrId(cnrId);
                if (!string.IsNullOrWhiteSpace(byCnr))
                {
                    packages.Add(byCnr);
                    resolved = true;
                }
                else
                {
                    unresolvedCnr = cnrId;
                }
            }

            var nodeType = reference.NodeType.Trim();
            if (!string.IsNullOrWhiteSpace(nodeType))
            {
                var byNode = map.ResolveNode(nodeType);
                if (!string.IsNullOrWhiteSpace(byNode))
                {
                    packages.Add(byNode);
                    resolved = true;
                }
                else if (CoreFrontendOnlyTypes.Contains(nodeType) ||
                         map.IsCoreNode(nodeType) ||
                         string.Equals(cnrId, "comfy-core", StringComparison.OrdinalIgnoreCase))
                {
                    resolved = true;
                }
            }

            if (!resolved)
            {
                if (!string.IsNullOrWhiteSpace(nodeType)) unresolvedTypes.Add(nodeType);
                if (!string.IsNullOrWhiteSpace(unresolvedCnr)) unresolvedCnrIds.Add(unresolvedCnr);
            }
        }

        var catalog = LoadCatalog();
        var id = StableWorkflowId(fullPath);
        var profile = catalog.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = new WorkflowProfile { Id = id };
            catalog.Profiles.Add(profile);
        }

        profile.Name = Path.GetFileNameWithoutExtension(fullPath);
        profile.WorkflowPath = fullPath;
        profile.WorkflowHash = HashFile(fullPath);
        profile.RequiredNodeTypes = references.Select(r => r.NodeType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.RequiredPackages = packages.ToList();
        profile.UnresolvedNodeTypes = unresolvedTypes.ToList();
        profile.UnresolvedCnrIds = unresolvedCnrIds.ToList();
        profile.UpdatedAt = DateTimeOffset.Now;

        SaveCatalog(catalog);
        progress?.Report($"分析完成：{profile.RequiredPackages.Count} 个包，{profile.UnresolvedNodeTypes.Count} 个未解析节点。");
        return profile;
    }

    public async Task<NodePackageMap> RefreshNodeIndexAsync(
        string baseUrl,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await _indexService.RefreshFromObjectInfoAsync(baseUrl, progress, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetInstalledPackageNamesAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await _indexService.GetInstalledPackageNamesAsync(progress, ct).ConfigureAwait(false);
    }

    public bool IsWorkflowChanged(WorkflowProfile profile)
    {
        if (!File.Exists(profile.WorkflowPath)) return true;
        try
        {
            return !string.Equals(HashFile(profile.WorkflowPath), profile.WorkflowHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    public CustomNodeWhitelist ResolveActiveWhitelist()
    {
        var catalog = LoadCatalog();
        if (!string.Equals(catalog.LoadMode, CustomNodeLoadModes.Profile, StringComparison.OrdinalIgnoreCase))
            return new CustomNodeWhitelist(null, Array.Empty<string>(), "");

        var profile = catalog.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return new CustomNodeWhitelist(new List<string>(), Array.Empty<string>(), "未选择工作流 Profile");

        var packages = profile.RequiredPackages
            .Concat(catalog.AlwaysEnabledPackages)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missing = packages
            .Where(p => !Directory.Exists(Path.Combine(_paths.NodesDir, p)))
            .ToList();
        return new CustomNodeWhitelist(
            packages.Where(p => !missing.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList(),
            missing,
            profile.Name,
            profile.UnresolvedNodeTypes.Count + profile.UnresolvedCnrIds.Count);
    }

    private static string StableWorkflowId(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return "wf-" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record WorkflowNodeReference(string NodeType, string CnrId);

    private static class WorkflowNodeParser
    {
        public static IReadOnlyList<WorkflowNodeReference> Extract(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var result = new List<WorkflowNodeReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(document.RootElement);
            return result;

            void Walk(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    if (element.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var node in nodes.EnumerateArray()) AddNode(node);
                    }

                    if (element.TryGetProperty("class_type", out var classType) &&
                        classType.ValueKind == JsonValueKind.String)
                    {
                        AddNode(element);
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("nodes") || property.NameEquals("class_type")) continue;
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            Walk(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray()) Walk(item);
                }
            }

            void AddNode(JsonElement node)
            {
                var type = GetString(node, "type") ?? GetString(node, "class_type") ?? "";
                var cnrId = "";
                if (node.TryGetProperty("properties", out var properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    cnrId = GetString(properties, "cnr_id")
                        ?? GetString(properties, "aux_id")
                        ?? "";
                }
                if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(cnrId)) return;
                var key = type + "|" + cnrId;
                if (!seen.Add(key)) return;
                result.Add(new WorkflowNodeReference(type, cnrId));
            }

            static string? GetString(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
    }
}

public sealed record CustomNodeWhitelist(
    IReadOnlyList<string>? Packages,
    IReadOnlyList<string> MissingPackages,
    string ProfileName,
    int UnresolvedCount = 0)
{
    public bool IsProfileMode => Packages != null;
}
