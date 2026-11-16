namespace XantaComfyLauncher.Models;

public sealed class NodePackageMap
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public string Source { get; set; } = "";
    public Dictionary<string, string> NodePackages { get; set; } = new();
    public Dictionary<string, string> CnrIdPackages { get; set; } = new();
    public HashSet<string> CoreNodeTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ResolveNode(string nodeType)
    {
        foreach (var pair in NodePackages)
        {
            if (string.Equals(pair.Key, nodeType, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        return null;
    }

    public string? ResolveCnrId(string cnrId)
    {
        foreach (var pair in CnrIdPackages)
        {
            if (string.Equals(pair.Key, cnrId, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        return null;
    }

    public bool IsCoreNode(string nodeType)
    {
        foreach (var type in CoreNodeTypes)
        {
            if (string.Equals(type, nodeType, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
