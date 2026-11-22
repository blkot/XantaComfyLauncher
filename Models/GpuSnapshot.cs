namespace XantaComfyLauncher.Models;

public sealed class GpuSnapshot
{
    public string GpuName { get; set; } = "";
    public int? UtilizationPercent { get; set; }
    public int? MemoryUsedMb { get; set; }
    public int? MemoryTotalMb { get; set; }
    public int? MemoryFreeMb { get; set; }
    public int? TemperatureC { get; set; }
    public int? ComfyProcessMemoryMb { get; set; }
    public bool ComfyProcessRunning { get; set; }
    public string Error { get; set; } = "";
    public bool IsAvailable => string.IsNullOrWhiteSpace(Error) && !string.IsNullOrWhiteSpace(GpuName);
}
