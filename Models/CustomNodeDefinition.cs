namespace XantaComfyLauncher.Models;

public sealed class CustomNodeDefinition
{
    public string Name { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string InstalledCommit { get; set; } = "";
    public string? InstalledTag { get; set; }
    public string? LatestTag { get; set; }
    public string? RequirementsFile { get; set; } = "requirements.txt";
    public bool Enabled { get; set; } = true;
    public string UpdatePolicy { get; set; } = "notify";
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LatestCommit { get; set; }
    public DateTimeOffset? LatestCommitDate { get; set; }
    public string? LastError { get; set; }
}
