using System.Diagnostics;
using System.IO;
using System.Windows;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class NodeManagerWindow
{
    private readonly AppPaths _paths;
    private readonly NodeManagerViewModel _viewModel;

    public NodeManagerWindow(AppPaths paths)
    {
        _paths = paths;
        _viewModel = new NodeManagerViewModel(paths);
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void RefreshLocalButton_Click(object sender, RoutedEventArgs e) => await SafeRunAsync(() => _viewModel.RefreshAsync(false));
    private async void RefreshRemoteButton_Click(object sender, RoutedEventArgs e) => await SafeRunAsync(() => _viewModel.RefreshAsync(true));

    private async void InstallMissingButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "安装清单中所有缺失节点吗？", "安装缺失节点",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await SafeRunAsync(_viewModel.InstallMissingAsync);
    }

    private async void UpdateSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow == null)
        {
            MessageBox.Show(this, "请先选择一个节点。", "更新节点", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"更新 {_viewModel.SelectedRow.Name} 到{_viewModel.SelectedUpdateTarget}吗？",
                "更新节点", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await SafeRunAsync(_viewModel.UpdateSelectedAsync);
    }

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"更新全部节点到{_viewModel.SelectedUpdateTarget}吗？有本地修改的仓库会跳过。",
                "全部更新", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await SafeRunAsync(_viewModel.UpdateAllAsync);
    }

    private async void InstallFromGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GitHubNodeInstallWindow(_paths) { Owner = this };
        if (window.ShowDialog() == true)
            await SafeRunAsync(() => _viewModel.RefreshAsync(true));
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_viewModel.CustomNodesDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_viewModel.CustomNodesDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开节点目录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConflictsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new NodeConflictWindow(_paths) { Owner = this };
        window.ShowDialog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "节点操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
