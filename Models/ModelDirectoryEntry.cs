namespace XantaComfyLauncher.Models;

public sealed class ModelDirectoryEntry
{
    public required string Section { get; init; }
    public required string Path { get; init; }
    public List<string> Categories { get; init; } = new();

    public string CategoryText => string.Join(", ", Categories);
}
