using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class GitHubNodeInstallViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppPaths _paths;
    private readonly CustomNodeManager _manager;
    private readonly GitHubNodeSearchService _search = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _log = new();
    private string _queryText = "";
    private string _statusText = "输入 GitHub URL、author/repo 或关键词";
    private string _logText = "";
    private bool _busy;
    private GitHubRepositoryInfo? _selectedRepository;

    public GitHubNodeInstallViewModel(AppPaths paths)
    {
        _paths = paths;
        _manager = new CustomNodeManager(paths);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<GitHubRepositoryInfo> Results { get; } = new();
    public string QueryText { get => _queryText; set => Set(ref _queryText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) Raise(nameof(CanInstall));
        }
    }
    public GitHubRepositoryInfo? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (Set(ref _selectedRepository, value)) Raise(nameof(CanInstall));
        }
    }
    public bool CanInstall => !IsBusy && SelectedRepository != null;

    public async Task SearchAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(QueryText))
            throw new InvalidOperationException("请输入 GitHub URL、author/repo 或搜索关键词。");
        IsBusy = true;
        Results.Clear();
        try
        {
            StatusText = "正在搜索 GitHub...";
            AppendLog("搜索：" + QueryText.Trim());
            var repos = await _search.SearchAsync(QueryText, _cts.Token);
            foreach (var repo in repos) Results.Add(repo);
            SelectedRepository = Results.FirstOrDefault();
            StatusText = Results.Count == 0 ? "没有找到匹配仓库" : $"找到 {Results.Count} 个仓库";
            AppendLog($"搜索完成：{Results.Count} 个结果");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> InstallSelectedAsync(Func<GitHubRepositoryInfo, bool> confirmUpdate)
    {
        var repo = SelectedRepository;
        if (repo == null || IsBusy) return false;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText = message;
                AppendLog(message);
            });
            var manifest = await _manager.LoadOrCreateManifestAsync(progress, _cts.Token);
            var existing = manifest.Nodes.FirstOrDefault(n =>
                GitHubNodeSearchService.RepositoryUrlsEqual(n.Repository, repo.CloneUrl))
                ?? manifest.Nodes.FirstOrDefault(n =>
                    string.Equals(n.Name, repo.Name, StringComparison.OrdinalIgnoreCase));

            CustomNodeState state;
            if (existing != null)
            {
                if (!GitHubNodeSearchService.RepositoryUrlsEqual(existing.Repository, repo.CloneUrl))
                    throw new InvalidOperationException(
                        $"节点目录名 {repo.Name} 已被另一个仓库占用：\r\n{existing.Repository}");
                if (!confirmUpdate(repo)) return false;
                existing.Branch = string.IsNullOrWhiteSpace(existing.Branch) ? repo.DefaultBranch : existing.Branch;
                state = await _manager.InstallOrUpdateAsync(existing, progress, _cts.Token);
            }
            else
            {
                state = await _manager.InstallOrUpdateAsync(new CustomNodeDefinition
                {
                    Name = repo.Name,
                    Repository = repo.CloneUrl,
                    Branch = repo.DefaultBranch,
                    RequirementsFile = null,
                    Enabled = true,
                    UpdatePolicy = "notify"
                }, progress, _cts.Token);
            }

            StatusText = "安装完成，请重启 ComfyUI 使节点生效。";
            AppendLog($"[ok] 已安装 {state.Definition.Name}");
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenSelectedRepository()
    {
        if (SelectedRepository == null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = SelectedRepository.HtmlUrl,
            UseShellExecute = true
        });
    }

    private void AppendLog(string message)
    {
        LauncherLog.Write("wpf-github-nodes", message);
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_log.Length > 80_000) _log.Remove(0, _log.Length - 50_000);
        LogText = _log.ToString();
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
