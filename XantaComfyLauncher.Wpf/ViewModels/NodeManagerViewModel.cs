using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class NodeStateRow
{
    public required CustomNodeState State { get; init; }
    public string Name => State.Definition.Name;
    public string Branch => State.Definition.Branch ?? "";
    public string LatestTag => State.LatestTag ?? "-";
    public string InstalledCommit => Short(State.InstalledCommit);
    public string LatestCommit => Short(State.LatestCommit);
    public string Status => State.StatusText;
    public string Detail => State.Error ?? (State.UpdateAvailable ? "可更新到最新提交或 Tag" : "");
    public string UpdateHint => State.UpdateAvailable ? "有更新" : "";

    private static string Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Length <= 10 ? value : value[..10];
}

public sealed class NodeManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppPaths _paths;
    private readonly CustomNodeManager _manager;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _log = new();
    private bool _busy;
    private string _statusText = "就绪";
    private string _logText = "";
    private NodeStateRow? _selectedRow;
    private string _selectedUpdateTarget = "最新提交";

    public NodeManagerViewModel(AppPaths paths)
    {
        _paths = paths;
        _manager = new CustomNodeManager(paths);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NodeStateRow> Rows { get; } = new();
    public IReadOnlyList<string> UpdateTargets { get; } = new[] { "最新提交", "最新 Tag" };
    public string SelectedUpdateTarget
    {
        get => _selectedUpdateTarget;
        set => Set(ref _selectedUpdateTarget, value);
    }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(CanUpdateSelected));
                Raise(nameof(CanUpdateAll));
                Raise(nameof(CanCheckRemote));
                Raise(nameof(CanInstallMissing));
            }
        }
    }
    public NodeStateRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value)) Raise(nameof(CanUpdateSelected));
        }
    }
    public bool CanUpdateSelected => !IsBusy && SelectedRow != null;
    public bool CanUpdateAll => !IsBusy && Rows.Count > 0;
    public bool CanCheckRemote => !IsBusy;
    public bool CanInstallMissing => !IsBusy;

    public async Task InitializeAsync()
    {
        if (CustomNodeStateCache.TryGet(_paths.InstanceRoot, out var cached))
        {
            ReplaceRows(cached.States);
            StatusText = $"显示上次状态（{cached.UpdatedAt:HH:mm:ss}），点击“检查远程更新”刷新";
            AppendLog($"已加载缓存状态：{cached.States.Count} 个节点");
            return;
        }
        await RefreshAsync(checkRemote: false);
    }

    public async Task RefreshAsync(bool checkRemote)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var progress = CreateProgress();
            AppendLog(checkRemote ? "开始检查远程节点更新..." : "开始读取本地节点状态...");
            var states = await _manager.GetStatesAsync(checkRemote, progress, _cts.Token);
            ReplaceRows(states);
            CustomNodeStateCache.Set(_paths.InstanceRoot, states, checkRemote);
            StatusText = checkRemote
                ? $"已检查 {states.Count} 个节点包（{DateTime.Now:HH:mm:ss}）"
                : $"已加载 {states.Count} 个节点；点击“检查远程更新”获取远程 tag 和提交";
            AppendLog($"节点状态：{states.Count} 个");
        }
        catch (OperationCanceledException)
        {
            StatusText = "操作已取消";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("wpf-nodes", ex);
            AppendLog("[error] " + ex.Message);
            StatusText = "读取节点状态失败";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallMissingAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog("开始安装缺失节点...");
            var results = await _manager.InstallMissingAsync(CreateProgress(), _cts.Token);
            var errors = results.Where(r => r.Error != null).ToList();
            foreach (var (definition, error) in errors)
                AppendLog($"[error] {definition.Name}: {error}");
            StatusText = $"安装完成：{results.Count - errors.Count} 成功，{errors.Count} 失败";
            await RefreshAsync(checkRemote: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UpdateSelectedAsync()
    {
        if (IsBusy || SelectedRow == null) return;
        var def = SelectedRow.State.Definition;
        var target = SelectedUpdateTarget == "最新 Tag" ? NodeUpdateTarget.LatestTag : NodeUpdateTarget.LatestCommit;
        if (target == NodeUpdateTarget.LatestTag && string.IsNullOrWhiteSpace(def.LatestTag))
            throw new InvalidOperationException($"{def.Name} 的远程仓库没有可用 tag。");
        IsBusy = true;
        try
        {
            var targetText = target == NodeUpdateTarget.LatestTag ? $"tag {def.LatestTag}" : "远程最新提交";
            AppendLog($"更新 {def.Name} -> {targetText}");
            await _manager.InstallOrUpdateAsync(def, target, CreateProgress(), _cts.Token);
            AppendLog($"[ok] 已更新 {def.Name}");
            StatusText = $"{def.Name} 更新完成，请重启 ComfyUI";
            await RefreshAsync(checkRemote: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UpdateAllAsync()
    {
        if (IsBusy) return;
        var target = SelectedUpdateTarget == "最新 Tag" ? NodeUpdateTarget.LatestTag : NodeUpdateTarget.LatestCommit;
        IsBusy = true;
        try
        {
            AppendLog(target == NodeUpdateTarget.LatestTag ? "开始全部更新到最新 Tag..." : "开始全部更新到最新提交...");
            var results = await _manager.UpdateAllAsync(target, CreateProgress(), _cts.Token);
            var errors = results.Where(r => r.Error != null).ToList();
            foreach (var (definition, error) in errors)
                AppendLog($"[error] {definition.Name}: {error}");
            StatusText = $"更新完成：{results.Count - errors.Count} 成功，{errors.Count} 失败";
            await RefreshAsync(checkRemote: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string CustomNodesDir => _manager.CustomNodesDir;

    private void ReplaceRows(IReadOnlyList<CustomNodeState> states)
    {
        var selectedName = SelectedRow?.Name;
        Rows.Clear();
        foreach (var state in states.OrderBy(s => s.Definition.Name, StringComparer.OrdinalIgnoreCase))
            Rows.Add(new NodeStateRow { State = state });
        SelectedRow = selectedName == null
            ? null
            : Rows.FirstOrDefault(row => string.Equals(row.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        Raise(nameof(CanUpdateAll));
        Raise(nameof(CanInstallMissing));
    }

    private IProgress<string> CreateProgress() => new Progress<string>(message =>
    {
        OnUi(() =>
        {
            StatusText = message;
            AppendLog(message);
        });
    });

    private void AppendLog(string message)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_log.Length > 120_000)
            _log.Remove(0, _log.Length - 80_000);
        LogText = _log.ToString();
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
        _cts.Cancel();
        _cts.Dispose();
    }
}
