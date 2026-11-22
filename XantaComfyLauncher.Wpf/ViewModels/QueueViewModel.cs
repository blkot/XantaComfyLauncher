using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class QueueViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppPaths _paths;
    private readonly int _port;
    private readonly ComfyApiService _api;
    private readonly ComfyWebSocketService _ws;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource _cts = new();
    private bool _refreshing;
    private bool _actionBusy;
    private string _queueSummary = "正在连接...";
    private string _executionStatus = "等待执行任务";
    private string _executionNode = "";
    private string _executionDetail = "实时进度连接中...";
    private string _executionError = "";
    private double _executionPercent;
    private ComfyQueueItem? _selectedItem;

    public QueueViewModel(AppPaths paths, int port)
    {
        _paths = paths;
        _port = port;
        _api = new ComfyApiService(paths.GetLocalUrl(port));
        _ws = new ComfyWebSocketService(paths.GetLocalUrl(port));
        _ws.ProgressChanged += progress => OnUi(() => UpdateProgress(progress));
        _ws.ConnectionStatusChanged += status => OnUi(() => ExecutionDetail = status);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ComfyQueueItem> Items { get; } = new();
    public string QueueSummary { get => _queueSummary; private set => Set(ref _queueSummary, value); }
    public string ExecutionStatus { get => _executionStatus; private set => Set(ref _executionStatus, value); }
    public string ExecutionNode { get => _executionNode; private set => Set(ref _executionNode, value); }
    public string ExecutionDetail { get => _executionDetail; private set => Set(ref _executionDetail, value); }
    public string ExecutionError { get => _executionError; private set => Set(ref _executionError, value); }
    public double ExecutionPercent { get => _executionPercent; private set => Set(ref _executionPercent, value); }
    public ComfyQueueItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Set(ref _selectedItem, value)) Raise(nameof(CanDelete));
        }
    }
    public bool CanDelete => SelectedItem is { IsRunning: false } && !_actionBusy;
    public bool CanInterrupt => !_actionBusy;

    public async Task InitializeAsync()
    {
        _ws.Start();
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing || _cts.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            var snapshot = await _api.GetQueueAsync(_cts.Token);
            var previousSelection = SelectedItem?.PromptId;
            Items.Clear();
            foreach (var item in snapshot.Running.Concat(snapshot.Pending))
                Items.Add(item);
            SelectedItem = previousSelection == null
                ? null
                : Items.FirstOrDefault(item => string.Equals(item.PromptId, previousSelection, StringComparison.OrdinalIgnoreCase));
            QueueSummary = $"运行 {snapshot.RunningCount} / 等待 {snapshot.PendingCount}";
            var running = snapshot.Running.FirstOrDefault();
            if (running != null) _ws.UpdateNodeTypes(running.NodeTypes);
        }
        catch (OperationCanceledException)
        {
            // Window is closing.
        }
        catch (Exception ex)
        {
            QueueSummary = "队列不可用";
            ExecutionError = ex.Message;
            LauncherLog.Write("wpf-queue", ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public async Task InterruptAsync()
    {
        if (_actionBusy) return;
        SetActionBusy(true);
        try
        {
            await _api.InterruptAsync(_cts.Token);
            ExecutionStatus = "已请求中断";
        }
        catch (Exception ex)
        {
            ExecutionError = "中断失败：" + ex.Message;
        }
        finally
        {
            SetActionBusy(false);
            await RefreshAsync();
        }
    }

    public async Task ClearPendingAsync()
    {
        if (_actionBusy) return;
        SetActionBusy(true);
        try
        {
            await _api.ClearPendingAsync(_cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ExecutionError = "清空失败：" + ex.Message;
        }
        finally
        {
            SetActionBusy(false);
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (_actionBusy || SelectedItem is not { IsRunning: false } item) return;
        SetActionBusy(true);
        try
        {
            await _api.DeletePendingAsync(new[] { item.PromptId }, _cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ExecutionError = "删除失败：" + ex.Message;
        }
        finally
        {
            SetActionBusy(false);
        }
    }

    private void UpdateProgress(ComfyExecutionProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.Error))
        {
            ExecutionStatus = "执行失败";
            ExecutionError = progress.Error;
            return;
        }

        ExecutionStatus = progress.Status;
        ExecutionNode = string.IsNullOrWhiteSpace(progress.NodeClassType)
            ? progress.NodeId
            : $"{progress.NodeClassType} ({progress.NodeId})";
        var details = new List<string>();
        if (progress.Value > 0 || progress.Max > 0) details.Add($"{progress.Value}/{progress.Max}");
        if (progress.NodeElapsed is TimeSpan elapsed) details.Add("已用 " + FormatDuration(elapsed));
        if (progress.Eta is TimeSpan eta) details.Add("预计剩余 " + FormatDuration(eta));
        if (progress.QueueRemaining > 0) details.Add($"队列剩余 {progress.QueueRemaining}");
        ExecutionDetail = details.Count > 0 ? string.Join(" · ", details) : progress.Status;
        if (progress.Percent > 0 || progress.Status == "执行完成")
            ExecutionPercent = Math.Clamp(progress.Percent, 0, 100);
        if (progress.Status == "执行完成")
            ExecutionStatus = "执行完成";
    }

    private void SetActionBusy(bool value)
    {
        _actionBusy = value;
        Raise(nameof(CanDelete));
        Raise(nameof(CanInterrupt));
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

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
        _cts.Cancel();
        _ws.Dispose();
        _api.Dispose();
        _cts.Dispose();
    }
}
