namespace XantaComfyLauncher.Models;

public sealed record SystemStatusSnapshot(
    int CpuPercent,
    int MemoryUsedMb,
    int MemoryTotalMb,
    int MemoryPercent,
    string Error = "")
{
    public bool IsAvailable => string.IsNullOrWhiteSpace(Error) && MemoryTotalMb > 0;
}
