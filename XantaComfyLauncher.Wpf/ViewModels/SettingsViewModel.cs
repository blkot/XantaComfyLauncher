using System.ComponentModel;
using System.Runtime.CompilerServices;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly LauncherConfigService _service = new();
    private LauncherConfig _config = new();
    private string _instancePath = "";
    private string _portText = "8188";
    private bool _isLocal = true;
    private bool _isLan;
    private bool _useSageAttention;
    private bool _useFp16Accumulation;
    private bool _autoOpenBrowser = true;
    private bool _updateDependencies = true;
    private string _extraArguments = "";
    private string _launcherChannel = "stable";
    private bool _checkLauncherUpdates = true;
    private bool _autoDownloadLauncherUpdates;
    private bool _minimizeToTray = true;
    private bool _autoStartWithWindows;
    private bool _autoRestartOnCrash;
    private bool _warnLowVram = true;
    private string _vramThresholdText = "2048";
    private string _statusText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> LauncherChannels { get; } = new[] { "stable", "beta" };
    public string InstancePath { get => _instancePath; set => Set(ref _instancePath, value); }
    public string PortText { get => _portText; set => Set(ref _portText, value); }
    public bool IsLocal
    {
        get => _isLocal;
        set
        {
            if (!Set(ref _isLocal, value)) return;
            if (value) IsLan = false;
        }
    }
    public bool IsLan
    {
        get => _isLan;
        set
        {
            if (!Set(ref _isLan, value)) return;
            if (value) IsLocal = false;
        }
    }
    public bool UseSageAttention { get => _useSageAttention; set => Set(ref _useSageAttention, value); }
    public bool UseFp16Accumulation { get => _useFp16Accumulation; set => Set(ref _useFp16Accumulation, value); }
    public bool AutoOpenBrowser { get => _autoOpenBrowser; set => Set(ref _autoOpenBrowser, value); }
    public bool UpdateDependencies { get => _updateDependencies; set => Set(ref _updateDependencies, value); }
    public string ExtraArguments { get => _extraArguments; set => Set(ref _extraArguments, value); }
    public string LauncherChannel { get => _launcherChannel; set => Set(ref _launcherChannel, value); }
    public bool CheckLauncherUpdates { get => _checkLauncherUpdates; set => Set(ref _checkLauncherUpdates, value); }
    public bool AutoDownloadLauncherUpdates { get => _autoDownloadLauncherUpdates; set => Set(ref _autoDownloadLauncherUpdates, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
    public bool AutoStartWithWindows { get => _autoStartWithWindows; set => Set(ref _autoStartWithWindows, value); }
    public bool AutoRestartOnCrash { get => _autoRestartOnCrash; set => Set(ref _autoRestartOnCrash, value); }
    public bool WarnLowVram { get => _warnLowVram; set => Set(ref _warnLowVram, value); }
    public string VramThresholdText { get => _vramThresholdText; set => Set(ref _vramThresholdText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public void Load()
    {
        _config = _service.LoadOrCreate();
        InstancePath = _config.InstanceRoot;
        PortText = _config.Port.ToString();
        IsLocal = _config.ListenMode == ListenMode.Local;
        IsLan = _config.ListenMode == ListenMode.Lan;
        UseSageAttention = _config.UseSageAttention;
        UseFp16Accumulation = _config.UseFp16Accumulation;
        AutoOpenBrowser = _config.AutoOpenBrowser;
        UpdateDependencies = _config.UpdateDependenciesOnSwitch;
        ExtraArguments = _config.ExtraArguments;
        LauncherChannel = string.IsNullOrWhiteSpace(_config.LauncherUpdateChannel)
            ? "stable"
            : _config.LauncherUpdateChannel.ToLowerInvariant();
        CheckLauncherUpdates = _config.CheckLauncherUpdatesOnStartup;
        AutoDownloadLauncherUpdates = _config.AutoDownloadLauncherUpdates;
        MinimizeToTray = _config.MinimizeToTray;
        AutoStartWithWindows = WindowsStartupService.IsEnabled();
        AutoRestartOnCrash = _config.AutoRestartOnCrash;
        WarnLowVram = _config.WarnLowVramBeforeStart;
        VramThresholdText = _config.VramWarningThresholdMb.ToString();
    }

    public void Save()
    {
        if (!int.TryParse(PortText.Trim(), out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("端口必须是 1-65535 之间的整数。");
        if (!int.TryParse(VramThresholdText.Trim(), out var threshold) || threshold < 256)
            throw new InvalidOperationException("显存预警阈值必须是不小于 256 的整数 MB。");

        _config.InstanceRoot = InstancePath.Trim();
        _config.Port = port;
        _config.ListenMode = IsLan ? ListenMode.Lan : ListenMode.Local;
        _config.UseSageAttention = UseSageAttention;
        _config.UseFp16Accumulation = UseFp16Accumulation;
        _config.AutoOpenBrowser = AutoOpenBrowser;
        _config.UpdateDependenciesOnSwitch = UpdateDependencies;
        _config.ExtraArguments = ExtraArguments.Trim();
        _config.LauncherUpdateChannel = LauncherChannel;
        _config.CheckLauncherUpdatesOnStartup = CheckLauncherUpdates;
        _config.AutoDownloadLauncherUpdates = AutoDownloadLauncherUpdates;
        _config.MinimizeToTray = MinimizeToTray;
        _config.AutoRestartOnCrash = AutoRestartOnCrash;
        _config.WarnLowVramBeforeStart = WarnLowVram;
        _config.VramWarningThresholdMb = threshold;
        _service.Save(_config);
        WindowsStartupService.SetEnabled(AutoStartWithWindows);
        StatusText = "设置已保存；部分设置会在下次启动 WPF Preview 时生效。";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
