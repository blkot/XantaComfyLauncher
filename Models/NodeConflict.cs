namespace XantaComfyLauncher.Models;

public sealed class NodeConflict
{
    public string Type { get; set; } = "";
    public string NodeType { get; set; } = "";
    public string PackageA { get; set; } = "";
    public string PackageB { get; set; } = "";
    public string Details { get; set; } = "";
}
