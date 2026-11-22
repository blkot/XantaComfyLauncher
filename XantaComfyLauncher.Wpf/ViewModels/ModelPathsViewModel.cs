using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop.ViewModels;

public sealed class ModelPathsViewModel : INotifyPropertyChanged
{
    private readonly AppPaths _paths;
    private readonly ModelPathManagementService _service;
    private ModelPathValidation? _selectedPath;
    private string _yamlText = "";
    private string _statusText = "就绪";
    private bool _busy;

    public ModelPathsViewModel(AppPaths paths)
    {
        _paths = paths;
        _service = new ModelPathManagementService(paths);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ModelPathValidation> Paths { get; } = new();
    public ModelPathValidation? SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (Set(ref _selectedPath, value)) Raise(nameof(CanOpenSelected));
        }
    }
    public string YamlText { get => _yamlText; set => Set(ref _yamlText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) Raise(nameof(CanOpenSelected));
        }
    }
    public bool CanOpenSelected => !IsBusy && SelectedPath != null;

    public void SetStatus(string message) => StatusText = message;

    public void Refresh()
    {
        Paths.Clear();
        foreach (var entry in _service.GetConfiguredPaths())
            Paths.Add(entry);
        YamlText = _service.ReadYaml();
        StatusText = $"共 {Paths.Count} 个模型路径";
    }

    public void OpenSelected()
    {
        if (SelectedPath == null) return;
        Directory.CreateDirectory(SelectedPath.Path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{SelectedPath.Path}\"",
            UseShellExecute = true
        });
    }

    public void SaveYaml()
    {
        _service.SaveYaml(YamlText);
        Refresh();
        StatusText = "extra_model_paths.yaml 已保存并创建备份";
    }

    public async Task CreateJunctionAsync(string linkPath, string targetPath)
    {
        IsBusy = true;
        try
        {
            await _service.CreateJunctionAsync(linkPath, targetPath);
            StatusText = "Junction 已创建：" + linkPath;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MigrateAsync(string sourcePath, string destinationPath, IProgress<string>? progress)
    {
        IsBusy = true;
        try
        {
            var result = await _service.MigrateDirectoryAsync(sourcePath, destinationPath, progress);
            Refresh();
            StatusText = result;
        }
        finally
        {
            IsBusy = false;
        }
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
