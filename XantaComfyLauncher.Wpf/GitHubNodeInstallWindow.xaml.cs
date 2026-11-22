using System.Windows;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Utils;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class GitHubNodeInstallWindow
{
    private readonly GitHubNodeInstallViewModel _viewModel;

    public GitHubNodeInstallWindow(AppPaths paths)
    {
        InitializeComponent();
        _viewModel = new GitHubNodeInstallViewModel(paths);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(_viewModel.SearchAsync);

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var repo = _viewModel.SelectedRepository;
        if (repo == null) return;
        if (MessageBox.Show(this,
                $"安装 {repo.FullName}？\r\n\r\n仓库：{repo.CloneUrl}\r\n\r\n" +
                "安装过程会执行 git clone，并在存在 requirements.txt 时执行 pip install。",
                "安装节点", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            var installed = await _viewModel.InstallSelectedAsync(existing =>
                MessageBox.Show(this,
                    $"{existing.FullName} 已经在节点清单中，是否更新到默认分支 {existing.DefaultBranch}？",
                    "节点已存在", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
            if (installed)
            {
                MessageBox.Show(this, "安装完成，请重启 ComfyUI 使节点生效。",
                    "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "安装节点失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenSelectedRepository();
    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => _viewModel.OpenSelectedRepository();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task SafeRunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "搜索 GitHub 失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
