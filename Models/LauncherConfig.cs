namespace XantaComfyLauncher.Models;

public enum ListenMode
{
    Local,
    Lan
}

public sealed class LauncherConfig
{
    public string InstanceRoot { get; set; } = "";
    public int Port { get; set; } = 8188;
    public ListenMode ListenMode { get; set; } = ListenMode.Local;
    public bool UseSageAttention { get; set; }
    public bool UseFp16Accumulation { get; set; }
    public bool AutoOpenBrowser { get; set; } = true;
    public bool UpdateDependenciesOnSwitch { get; set; } = true;
    public bool IncludePrerelease { get; set; }
    public string ExtraArguments { get; set; } = "";
    public string LastModelDirectory { get; set; } = "";

    // Launcher self-update settings. These are intentionally separate from
    // IncludePrerelease, which controls the ComfyUI core tag list.
    public string LauncherUpdateChannel { get; set; } = "stable";
    public bool CheckLauncherUpdatesOnStartup { get; set; } = true;
    public string UpdateFeedUrl { get; set; } = "";
    public string SkippedLauncherVersion { get; set; } = "";
}
