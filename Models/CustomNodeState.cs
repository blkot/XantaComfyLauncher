namespace XantaComfyLauncher.Models;

public enum CustomNodeInstallState
{
    Installed,
    Missing,
    NotGit,
    Dirty,
    UpdateAvailable,
    Checking,
    Error
}

public sealed class CustomNodeState
{
    public required CustomNodeDefinition Definition { get; init; }
    public string Path { get; init; } = "";
    public CustomNodeInstallState State { get; set; } = CustomNodeInstallState.Installed;
    public bool IsInstalled { get; set; }
    public bool IsDirty { get; set; }
    public bool UpdateAvailable { get; set; }
    public string InstalledCommit { get; set; } = "";
    public string LatestCommit { get; set; } = "";
    public string? LatestTag { get; set; }
    public string? Error { get; set; }

    public string StatusText => State switch
    {
        CustomNodeInstallState.Missing => "未安装",
        CustomNodeInstallState.NotGit => "非 Git 仓库",
        CustomNodeInstallState.Dirty => "有本地修改",
        CustomNodeInstallState.UpdateAvailable => "有更新",
        CustomNodeInstallState.Checking => "检查中",
        CustomNodeInstallState.Error => "错误",
        _ => UpdateAvailable ? "有更新" : "已安装"
    };
}
