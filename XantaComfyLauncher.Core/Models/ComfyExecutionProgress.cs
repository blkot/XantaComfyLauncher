namespace XantaComfyLauncher.Models;

public sealed class ComfyExecutionProgress
{
    public string PromptId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string NodeClassType { get; set; } = "";
    public string Status { get; set; } = "";
    public int Value { get; set; }
    public int Max { get; set; }
    public double Percent { get; set; }
    public TimeSpan? NodeElapsed { get; set; }
    public TimeSpan? Eta { get; set; }
    public long QueueRemaining { get; set; }
    public string Error { get; set; } = "";
}
