namespace ComfyLauncher.Models;

public enum ListenMode
{
    Local,
    Lan
}

public sealed class LauncherConfig
{
    public int Port { get; set; } = 8188;
    public ListenMode ListenMode { get; set; } = ListenMode.Local;
    public bool UseSageAttention { get; set; }
    public bool AutoOpenBrowser { get; set; } = true;
    public bool UpdateDependenciesOnSwitch { get; set; } = true;
    public bool IncludePrerelease { get; set; }
    public string ExtraArguments { get; set; } = "";
}
