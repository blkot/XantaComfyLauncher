namespace XantaComfyLauncher.Models;

public sealed class VersionCatalog
{
    public IReadOnlyList<ComfyVersionInfo> Versions { get; init; } = Array.Empty<ComfyVersionInfo>();
}
