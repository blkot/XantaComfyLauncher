namespace ComfyLauncher.Models;

public sealed class EnvironmentInfo
{
    public string ComfyVersion { get; init; } = "未知";
    public string ComfyCommit { get; init; } = "未知";
    public string ComfyCommitDate { get; init; } = "未知";
    public string PythonVersion { get; init; } = "未知";
    public string TorchVersion { get; init; } = "未安装";
    public string SageAttentionVersion { get; init; } = "未安装";
    public string TritonVersion { get; init; } = "未安装";
    public string GpuName { get; init; } = "未知";
    public string GpuMemory { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public string LanIp { get; init; } = "未检测到";
}
