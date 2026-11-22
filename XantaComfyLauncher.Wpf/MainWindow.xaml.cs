using System.IO;
using System.Windows;
using System.Windows.Threading;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Desktop.Utils;
using XantaComfyLauncher.Utils;
using XantaComfyLauncher.Desktop.ViewModels;

namespace XantaComfyLauncher.Desktop;

public partial class MainWindow
{
    private readonly DashboardViewModel _viewModel = new();
    private readonly DispatcherTimer _trayTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private WpfTrayIconManager? _tray;
    private readonly bool _startMinimized;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoadedAsync;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _trayTimer.Stop();
            _tray?.Dispose();
            _viewModel.Dispose();
        };
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        BuildTray();
        UpdateTrayTooltip();
        _trayTimer.Tick += (_, _) => UpdateTrayTooltip();
        _trayTimer.Start();
        if (_startMinimized)
        {
            Hide();
            ShowInTaskbar = false;
            _tray?.ShowBalloon("XantaComfyLauncher", "启动器已在托盘运行。");
        }
    }

    private void BuildTray()
    {
        _tray = new WpfTrayIconManager(
            _viewModel.Config.MinimizeToTray,
            WindowsStartupService.IsEnabled(),
            _viewModel.Config.AutoRestartOnCrash);
        _tray.ShowRequested += RestoreWindow;
        _tray.StartRequested += async () => await _viewModel.StartAsync();
        _tray.StopRequested += async () => await _viewModel.StopAsync();
        _tray.OpenRequested += _viewModel.OpenComfyUi;
        _tray.QueueRequested += OpenQueue;
        _tray.ExitRequested += Close;
        _tray.MinimizeToTrayChanged += enabled =>
        {
            _viewModel.Config.MinimizeToTray = enabled;
            SaveConfig();
        };
        _tray.AutoRestartOnCrashChanged += enabled =>
        {
            _viewModel.Config.AutoRestartOnCrash = enabled;
            SaveConfig();
        };
        _tray.WarnLowVramBeforeStartChanged += enabled =>
        {
            _viewModel.Config.WarnLowVramBeforeStart = enabled;
            SaveConfig();
        };
    }

    private void SaveConfig()
    {
        try { new LauncherConfigService().Save(_viewModel.Config); }
        catch (Exception ex) { LauncherLog.Error("wpf", ex); }
    }

    private void UpdateTrayTooltip()
    {
        if (_tray == null) return;
        _tray.SetStatusText($"{_viewModel.QueueSummary} | VRAM {_viewModel.GpuMemoryText} · GPU {_viewModel.GpuTemperatureText}");
    }

    private void RestoreWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.Config.MinimizeToTray)
        {
            Hide();
            ShowInTaskbar = false;
            _tray?.ShowBalloon("XantaComfyLauncher", "已最小化到托盘，双击托盘图标恢复。");
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) => await _viewModel.StartAsync();
    private async void StopButton_Click(object sender, RoutedEventArgs e) => await _viewModel.StopAsync();
    private void OpenButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenComfyUi();

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        switch (tag)
        {
            case "queue": OpenQueue(); break;
            case "nodes": OpenLegacyNodes(); break;
            case "workflows": OpenLegacyWorkflows(); break;
            case "models": OpenLegacyModels(); break;
            case "logs": OpenLogs(); break;
            case "settings":
                System.Windows.MessageBox.Show(this,
                    "WPF 设置页将在 Phase 2 迁移。\n当前设置仍由旧版启动器窗口维护。",
                    "WPF Preview", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void OpenQueue()
    {
        if (_viewModel.Paths == null) return;
        if (!_viewModel.IsRunning)
        {
            System.Windows.MessageBox.Show(this, "请先启动 ComfyUI。", "队列管理",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var window = new QueueWindow(_viewModel.Paths, _viewModel.Config.Port)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenLegacyNodes()
    {
        if (_viewModel.Paths == null) return;
        var window = new NodeManagerWindow(_viewModel.Paths)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenLegacyWorkflows()
    {
        if (_viewModel.Paths == null) return;
        var window = new WorkflowProfilesWindow(_viewModel.Paths,
            _viewModel.Paths.GetLocalUrl(_viewModel.Config.Port))
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenLegacyModels()
    {
        if (_viewModel.Paths == null) return;
        var window = new ModelPathsWindow(_viewModel.Paths)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenLogs()
    {
        try
        {
            var path = Path.GetDirectoryName(LauncherLog.LogFile) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "打开日志目录失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _trayTimer.Stop();
        _tray?.Dispose();
        _tray = null;
        _viewModel.Dispose();
    }
}
