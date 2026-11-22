using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LauncherConfigService _configService = new();
    private readonly GpuMonitorService _gpuMonitor = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private LauncherConfig _config = new();
    private AppPaths? _paths;
    private ComfyProcessService? _process;
    private bool _refreshing;
    private string _instancePath = "";
    private string _statusText = "正在初始化...";
    private string _statusDetail = "";
    private bool _isRunning;
    private bool _isBusy;
    private string _comfyVersion = "读取中...";
    private string _comfyCommit = "读取中...";
    private string _pythonVersion = "读取中...";
    private string _torchVersion = "读取中...";
    private string _gpuName = "读取中...";
    private double _gpuUtilization;
    private string _gpuUtilizationText = "--";
    private double _gpuMemoryPercent;
    private string _gpuMemoryText = "--";
    private string _gpuTemperatureText = "--";
    private System.Windows.Media.Brush _gpuTemperatureBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 228, 232));
    private string _gpuProcessText = "";
    private string _queueSummary = "未连接";
    private string _launcherVersion = "";

    public DashboardViewModel()
    {
        _refreshTimer.Tick += async (_, _) => await RefreshMetricsAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InstancePath { get => _instancePath; private set => Set(ref _instancePath, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(CanStart));
                Raise(nameof(CanStop));
                Raise(nameof(RunStateText));
            }
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(CanStart));
                Raise(nameof(CanStop));
            }
        }
    }
    public bool CanStart => !IsRunning && !IsBusy && _paths != null;
    public bool CanStop => IsRunning && !IsBusy && _process != null;
    public string RunStateText => IsRunning ? "运行中" : "已停止";
    public string ComfyVersion { get => _comfyVersion; private set => Set(ref _comfyVersion, value); }
    public string ComfyCommit { get => _comfyCommit; private set => Set(ref _comfyCommit, value); }
    public string PythonVersion { get => _pythonVersion; private set => Set(ref _pythonVersion, value); }
    public string TorchVersion { get => _torchVersion; private set => Set(ref _torchVersion, value); }
    public string GpuName { get => _gpuName; private set => Set(ref _gpuName, value); }
    public double GpuUtilization { get => _gpuUtilization; private set => Set(ref _gpuUtilization, value); }
    public string GpuUtilizationText { get => _gpuUtilizationText; private set => Set(ref _gpuUtilizationText, value); }
    public double GpuMemoryPercent { get => _gpuMemoryPercent; private set => Set(ref _gpuMemoryPercent, value); }
    public string GpuMemoryText { get => _gpuMemoryText; private set => Set(ref _gpuMemoryText, value); }
    public string GpuTemperatureText { get => _gpuTemperatureText; private set => Set(ref _gpuTemperatureText, value); }
    public System.Windows.Media.Brush GpuTemperatureBrush { get => _gpuTemperatureBrush; private set => Set(ref _gpuTemperatureBrush, value); }
    public string GpuProcessText { get => _gpuProcessText; private set => Set(ref _gpuProcessText, value); }
    public string QueueSummary { get => _queueSummary; private set => Set(ref _queueSummary, value); }
    public string LauncherVersion { get => _launcherVersion; private set => Set(ref _launcherVersion, value); }
    public AppPaths? Paths => _paths;
    public LauncherConfig Config => _config;

    public async Task InitializeAsync()
    {
        _config = _configService.LoadOrCreate();
        InstancePath = string.IsNullOrWhiteSpace(_config.InstanceRoot) ? "未绑定实例" : _config.InstanceRoot;
        LauncherVersion = "WPF Preview · " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
        if (!AppPaths.TryCreate(_config.InstanceRoot, out var paths, out var error) || paths == null)
        {
            StatusText = "未绑定 ComfyUI 实例";
            StatusDetail = error;
            return;
        }

        _paths = paths;
        _process = new ComfyProcessService(paths);
        _process.LogReceived += line => StatusDetail = line;
        _process.StateChanged += state => OnUi(() =>
        {
            IsRunning = state is ComfyProcessState.Starting or ComfyProcessState.Running or ComfyProcessState.Stopping;
            StatusText = state switch
            {
                ComfyProcessState.Starting => "正在启动 ComfyUI...",
                ComfyProcessState.Running => "ComfyUI 运行中",
                ComfyProcessState.Stopping => "正在停止 ComfyUI...",
                ComfyProcessState.Error => "ComfyUI 启动失败",
                _ => "ComfyUI 已停止"
            };
        });
        _process.Healthy += info => OnUi(() =>
        {
            IsRunning = true;
            StatusText = "ComfyUI 已就绪";
            StatusDetail = info.LocalUrl;
        });
        _process.Exited += code => OnUi(() =>
        {
            IsRunning = false;
            StatusText = "ComfyUI 已退出";
            StatusDetail = "退出代码：" + code;
            _ = RefreshMetricsAsync();
        });

        StatusText = "正在读取实例信息...";
        await RefreshEnvironmentAsync(paths);
        await RefreshMetricsAsync();
        _refreshTimer.Start();
    }

    public async Task StartAsync()
    {
        if (_paths == null || _process == null || IsBusy) return;
        IsBusy = true;
        try
        {
            var whitelist = new WorkflowProfileService(_paths).ResolveActiveWhitelist();
            StatusText = "正在启动 ComfyUI...";
            StatusDetail = whitelist.IsProfileMode
                ? "按工作流加载：" + whitelist.ProfileName
                : "加载全部 custom_nodes";
            await _process.StartAsync(_config, whitelist.Packages);
            IsRunning = true;
        }
        catch (Exception ex)
        {
            StatusText = "启动失败";
            StatusDetail = ex.Message;
            LauncherLog.Error("wpf", ex);
        }
        finally
        {
            IsBusy = false;
            await RefreshMetricsAsync();
        }
    }

    public async Task StopAsync()
    {
        if (_process == null || IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = "正在停止 ComfyUI...";
            await _process.StopAsync();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            StatusText = "停止失败";
            StatusDetail = ex.Message;
            LauncherLog.Error("wpf", ex);
        }
        finally
        {
            IsBusy = false;
            await RefreshMetricsAsync();
        }
    }

    public void OpenComfyUi()
    {
        if (_paths == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.GetLocalUrl(_config.Port),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusDetail = "打开浏览器失败：" + ex.Message;
        }
    }

    private async Task RefreshEnvironmentAsync(AppPaths paths)
    {
        try
        {
            var git = new GitService(paths);
            var probe = new EnvironmentProbeService(paths, git);
            var info = await probe.ProbeAsync();
            ComfyVersion = info.ComfyVersion;
            ComfyCommit = info.ComfyCommit;
            PythonVersion = info.PythonVersion;
            TorchVersion = info.TorchVersion;
            GpuName = string.Join("  ", new[] { info.GpuName, info.GpuMemory }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            StatusText = "实例已加载";
            StatusDetail = paths.InstanceRoot;
        }
        catch (Exception ex)
        {
            StatusText = "环境信息读取失败";
            StatusDetail = ex.Message;
            LauncherLog.Error("wpf", ex);
        }
    }

    private async Task RefreshMetricsAsync()
    {
        if (_refreshing || _paths == null) return;
        _refreshing = true;
        try
        {
            var snapshot = await _gpuMonitor.GetSnapshotAsync(_process?.ProcessId);
            OnUi(() => UpdateGpu(snapshot));

            if (_process?.IsRunning == true)
            {
                using var api = new ComfyApiService(_paths.GetLocalUrl(_config.Port));
                var queue = await api.GetQueueAsync();
                QueueSummary = $"运行 {queue.RunningCount} / 等待 {queue.PendingCount}";
            }
            else
            {
                QueueSummary = "ComfyUI 未运行";
            }
        }
        catch (Exception ex)
        {
            QueueSummary = "队列不可用";
            LauncherLog.Write("wpf", "刷新指标失败：" + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateGpu(GpuSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            GpuUtilization = 0;
            GpuUtilizationText = "--";
            GpuMemoryPercent = 0;
            GpuMemoryText = "--";
            GpuTemperatureText = "--";
            GpuTemperatureBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 228, 232));
            GpuProcessText = string.IsNullOrWhiteSpace(snapshot.Error) ? "GPU 监控不可用" : snapshot.Error;
            return;
        }

        GpuName = snapshot.GpuName;
        GpuUtilization = snapshot.UtilizationPercent ?? 0;
        GpuUtilizationText = snapshot.UtilizationPercent is int util ? util + "%" : "--";
        GpuMemoryPercent = snapshot.MemoryUsedMb is int used && snapshot.MemoryTotalMb is int total && total > 0
            ? Math.Clamp(used * 100.0 / total, 0, 100)
            : 0;
        GpuMemoryText = snapshot.MemoryUsedMb is int usedMb && snapshot.MemoryTotalMb is int totalMb
            ? $"{usedMb / 1024.0:0.0} / {totalMb / 1024.0:0.0} GB"
            : "--";
        GpuTemperatureText = snapshot.TemperatureC is int temp ? temp + "°C" : "--";
        GpuTemperatureBrush = snapshot.TemperatureC switch
        {
            null => new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 228, 232)),
            <= 60 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 245, 222)),
            <= 80 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 236, 200)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 214, 214))
        };
        GpuProcessText = snapshot.ComfyProcessMemoryMb is int processMb
            ? $"ComfyUI 进程显存：{processMb} MB"
            : "";
    }

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _refreshTimer.Stop();
        _process?.Dispose();
    }
}
