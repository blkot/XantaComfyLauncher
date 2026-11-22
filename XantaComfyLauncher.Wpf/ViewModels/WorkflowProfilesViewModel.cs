using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed record WorkflowFileItem(string FullPath, string DisplayPath)
{
    public override string ToString() => DisplayPath;
}

public sealed class WorkflowProfileRow
{
    public required WorkflowProfile Profile { get; init; }
    public required string Status { get; init; }
    public string Name => Profile.Name;
    public string Packages => Profile.RequiredPackages.Count.ToString();
    public string Unresolved => (Profile.UnresolvedNodeTypes.Count + Profile.UnresolvedCnrIds.Count).ToString();
    public string Updated => Profile.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string WorkflowPath => Profile.WorkflowPath;
    public bool IsActive { get; init; }
}

public sealed class AlwaysEnabledPackageOption : INotifyPropertyChanged
{
    private bool _isChecked;
    public required string Name { get; init; }
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class WorkflowProfilesViewModel : INotifyPropertyChanged
{
    private readonly AppPaths _paths;
    private readonly string _baseUrl;
    private readonly WorkflowProfileService _service;
    private WorkflowProfileCatalog _catalog = new();
    private WorkflowFileItem? _selectedWorkflow;
    private WorkflowProfileRow? _selectedProfile;
    private bool _busy;
    private string _statusText = "就绪";
    private string _detailText = "";

    public WorkflowProfilesViewModel(AppPaths paths, string baseUrl)
    {
        _paths = paths;
        _baseUrl = baseUrl;
        _service = new WorkflowProfileService(paths);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<WorkflowFileItem> WorkflowFiles { get; } = new();
    public ObservableCollection<WorkflowProfileRow> Profiles { get; } = new();
    public ObservableCollection<AlwaysEnabledPackageOption> AlwaysEnabledPackages { get; } = new();

    public WorkflowFileItem? SelectedWorkflow
    {
        get => _selectedWorkflow;
        set => Set(ref _selectedWorkflow, value);
    }
    public WorkflowProfileRow? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            DetailText = BuildDetail(value?.Profile);
            Raise(nameof(CanUseSelected));
        }
    }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(CanUseSelected));
                Raise(nameof(CanAnalyze));
            }
        }
    }
    public bool CanUseSelected => !IsBusy && SelectedProfile != null;
    public bool CanAnalyze => !IsBusy && SelectedWorkflow != null;

    public async Task InitializeAsync()
    {
        _catalog = _service.LoadCatalog();
        ReloadWorkflowFiles();
        await LoadAlwaysEnabledPackagesAsync();
        PopulateProfiles();
        UpdateStatusSummary();
    }

    public void ReloadWorkflowFiles()
    {
        var current = SelectedWorkflow?.FullPath;
        WorkflowFiles.Clear();
        foreach (var file in _service.GetWorkflowFiles())
            WorkflowFiles.Add(new WorkflowFileItem(file, Path.GetRelativePath(_paths.InstanceRoot, file)));
        SelectedWorkflow = current == null
            ? WorkflowFiles.FirstOrDefault()
            : WorkflowFiles.FirstOrDefault(x => string.Equals(x.FullPath, current, StringComparison.OrdinalIgnoreCase))
              ?? WorkflowFiles.FirstOrDefault();
    }

    public async Task AnalyzeSelectedAsync()
    {
        if (IsBusy || SelectedWorkflow == null) return;
        IsBusy = true;
        try
        {
            StatusText = "正在分析工作流...";
            var profile = await _service.AnalyzeAndSaveAsync(
                SelectedWorkflow.FullPath, new Progress<string>(text => StatusText = text));
            _catalog = _service.LoadCatalog();
            PopulateProfiles(profile.Id);
            StatusText = $"已分析 {profile.Name}：{profile.RequiredPackages.Count} 个包，" +
                         $"{profile.UnresolvedNodeTypes.Count + profile.UnresolvedCnrIds.Count} 个未解析项";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectUpdateTarget(string target) { }

    public void SetSelectedProfileActive()
    {
        if (SelectedProfile == null) return;
        _catalog.LoadMode = CustomNodeLoadModes.Profile;
        _catalog.ActiveProfileId = SelectedProfile.Profile.Id;
        _service.SaveCatalog(_catalog);
        PopulateProfiles(SelectedProfile.Profile.Id);
        StatusText = $"启动配置：按工作流加载 {SelectedProfile.Name}";
    }

    public void SetAllMode()
    {
        _catalog.LoadMode = CustomNodeLoadModes.All;
        _catalog.ActiveProfileId = "";
        _service.SaveCatalog(_catalog);
        PopulateProfiles();
        StatusText = "启动配置：全部加载 custom_nodes";
    }

    public void DeleteSelectedProfile()
    {
        if (SelectedProfile == null) return;
        var profile = SelectedProfile.Profile;
        _catalog.Profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_catalog.ActiveProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _catalog.ActiveProfileId = "";
            _catalog.LoadMode = CustomNodeLoadModes.All;
        }
        _service.SaveCatalog(_catalog);
        PopulateProfiles();
        StatusText = $"已删除 Profile：{profile.Name}";
    }

    public async Task RefreshNodeIndexAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = "正在刷新节点索引...";
            var map = await _service.RefreshNodeIndexAsync(
                _baseUrl, new Progress<string>(text => StatusText = text));
            StatusText = $"节点索引完成：{map.NodePackages.Count} 个后端节点，" +
                         $"{map.CnrIdPackages.Count} 个包别名";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SaveAlwaysEnabledPackages()
    {
        _catalog.AlwaysEnabledPackages = AlwaysEnabledPackages
            .Where(option => option.IsChecked)
            .Select(option => option.Name)
            .ToList();
        _service.SaveCatalog(_catalog);
        StatusText = $"始终启用插件：{_catalog.AlwaysEnabledPackages.Count} 个";
    }

    private async Task LoadAlwaysEnabledPackagesAsync()
    {
        AlwaysEnabledPackages.Clear();
        var packages = await _service.GetInstalledPackageNamesAsync(new Progress<string>(text => StatusText = text));
        foreach (var package in packages)
        {
            AlwaysEnabledPackages.Add(new AlwaysEnabledPackageOption
            {
                Name = package,
                IsChecked = _catalog.AlwaysEnabledPackages.Contains(package, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private void PopulateProfiles(string? selectId = null)
    {
        var selected = selectId ?? SelectedProfile?.Profile.Id;
        Profiles.Clear();
        foreach (var profile in _catalog.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var changed = _service.IsWorkflowChanged(profile);
            var status = !File.Exists(profile.WorkflowPath) ? "文件缺失" : changed ? "工作流已变化" : "有效";
            Profiles.Add(new WorkflowProfileRow
            {
                Profile = profile,
                Status = status,
                IsActive = string.Equals(profile.Id, _catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(_catalog.LoadMode, CustomNodeLoadModes.Profile, StringComparison.OrdinalIgnoreCase)
            });
        }
        SelectedProfile = selected == null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(p => string.Equals(p.Profile.Id, selected, StringComparison.OrdinalIgnoreCase))
              ?? Profiles.FirstOrDefault();
    }

    private void UpdateStatusSummary()
    {
        var active = _catalog.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, _catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        StatusText = string.Equals(_catalog.LoadMode, CustomNodeLoadModes.Profile, StringComparison.OrdinalIgnoreCase)
            ? $"当前启动配置：按工作流 {active?.Name ?? "（未选择）"}"
            : "当前启动配置：全部加载";
    }

    private static string BuildDetail(WorkflowProfile? profile)
    {
        if (profile == null) return "";
        var lines = new List<string>
        {
            "Profile：" + profile.Name,
            "工作流：" + profile.WorkflowPath,
            "Hash：" + profile.WorkflowHash,
            "更新时间：" + profile.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            "",
            "所需插件包："
        };
        lines.AddRange(profile.RequiredPackages.Select(p => "  " + p));
        lines.Add("");
        lines.Add("未解析节点类型：");
        lines.AddRange(profile.UnresolvedNodeTypes.Select(t => "  " + t));
        lines.Add("");
        lines.Add("未解析 CNR ID：");
        lines.AddRange(profile.UnresolvedCnrIds.Select(id => "  " + id));
        return string.Join(Environment.NewLine, lines);
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
}
