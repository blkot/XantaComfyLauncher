namespace XantaComfyLauncher.Models;

public static class CustomNodeLoadModes
{
    public const string All = "All";
    public const string Profile = "Profile";
}

public sealed class WorkflowProfileCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string LoadMode { get; set; } = CustomNodeLoadModes.All;
    public string ActiveProfileId { get; set; } = "";
    public List<string> AlwaysEnabledPackages { get; set; } = new();
    public List<WorkflowProfile> Profiles { get; set; } = new();
}
