namespace XantaComfyLauncher.Models;

public sealed class WorkflowProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkflowPath { get; set; } = "";
    public string WorkflowHash { get; set; } = "";
    public List<string> RequiredNodeTypes { get; set; } = new();
    public List<string> RequiredPackages { get; set; } = new();
    public List<string> UnresolvedNodeTypes { get; set; } = new();
    public List<string> UnresolvedCnrIds { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
