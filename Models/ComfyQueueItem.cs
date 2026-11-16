namespace XantaComfyLauncher.Models;

public sealed record ComfyQueueItem(
    bool IsRunning,
    long? Number,
    string PromptId,
    int NodeCount,
    string Summary)
{
    public string StateText => IsRunning ? "运行中" : "等待中";
}
