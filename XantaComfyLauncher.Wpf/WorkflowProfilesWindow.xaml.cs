using System.Windows;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Utils;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class WorkflowProfilesWindow
{
    private readonly WorkflowProfilesViewModel _viewModel;

    public WorkflowProfilesWindow(AppPaths paths, string baseUrl)
    {
        InitializeComponent();
        _viewModel = new WorkflowProfilesViewModel(paths, baseUrl);
        DataContext = _viewModel;
        Loaded += async (_, _) => await SafeRunAsync(_viewModel.InitializeAsync);
    }

    private void RefreshWorkflowListButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ReloadWorkflowFiles();

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(_viewModel.AnalyzeSelectedAsync);

    private async void RefreshIndexButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "刷新节点索引需要 ComfyUI 正在运行，并且最好使用“全部加载”模式启动。继续吗？",
                "刷新节点索引", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await SafeRunAsync(_viewModel.RefreshNodeIndexAsync);
    }

    private void SetActiveButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedProfileActive();

    private void AllModeButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllMode();

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProfile == null) return;
        if (MessageBox.Show(this, $"删除 Profile“{_viewModel.SelectedProfile.Name}”吗？", "删除 Profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _viewModel.DeleteSelectedProfile();
    }

    private void AlwaysEnabledCheckBox_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SaveAlwaysEnabledPackages();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "工作流 Profile 操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
