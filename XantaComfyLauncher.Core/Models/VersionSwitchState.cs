namespace XantaComfyLauncher.Models;

public sealed class VersionSwitchState
{
    public string PreviousCommit { get; set; } = "";
    public string PreviousTag { get; set; } = "";
    public string TargetTag { get; set; } = "";
    public string TargetCommit { get; set; } = "";
    public string RequirementsHashBefore { get; set; } = "";
    public string RequirementsHashAfter { get; set; } = "";
    public DateTimeOffset SwitchedAt { get; set; }
    public DateTimeOffset? RolledBackAt { get; set; }
}
