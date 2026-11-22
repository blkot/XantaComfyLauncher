using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class NodeConflictViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly NodeConflictService _service;
    private readonly CancellationTokenSource _cts = new();
    private string _statusText = "就绪";
    private bool _busy;

    public NodeConflictViewModel(AppPaths paths, string nodesDir)
    {
        _service = new NodeConflictService(paths);
        NodesDir = nodesDir;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<NodeConflict> Items { get; } = new();
    public string NodesDir { get; }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }

    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Items.Clear();
        try
        {
            var conflicts = await _service.ScanAsync(new Progress<string>(text => StatusText = text), _cts.Token);
            foreach (var conflict in conflicts) Items.Add(conflict);
            StatusText = conflicts.Count == 0 ? "未发现冲突" : $"发现 {conflicts.Count} 个问题";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenNodesFolder()
    {
        Directory.CreateDirectory(NodesDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{NodesDir}\"",
            UseShellExecute = true
        });
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
