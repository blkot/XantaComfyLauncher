namespace ComfyLauncher.Models;

public sealed class VersionCatalog
{
    public IReadOnlyList<ComfyVersionInfo> Versions { get; init; } = Array.Empty<ComfyVersionInfo>();
    public string? Warning { get; init; }
}
