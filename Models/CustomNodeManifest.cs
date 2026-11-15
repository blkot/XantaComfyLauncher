namespace ComfyLauncher.Models;

public sealed class CustomNodeManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<CustomNodeDefinition> Nodes { get; set; } = new();
}
