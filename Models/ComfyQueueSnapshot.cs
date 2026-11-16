namespace XantaComfyLauncher.Models;

public sealed class ComfyQueueSnapshot
{
    public IReadOnlyList<ComfyQueueItem> Running { get; init; } = Array.Empty<ComfyQueueItem>();
    public IReadOnlyList<ComfyQueueItem> Pending { get; init; } = Array.Empty<ComfyQueueItem>();

    public int RunningCount => Running.Count;
    public int PendingCount => Pending.Count;
}
