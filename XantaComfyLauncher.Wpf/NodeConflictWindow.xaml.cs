using System.Windows;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Utils;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class NodeConflictWindow
{
    private readonly NodeConflictViewModel _viewModel;

    public NodeConflictWindow(AppPaths paths)
    {
        InitializeComponent();
        _viewModel = new NodeConflictViewModel(paths, paths.NodesDir);
        DataContext = _viewModel;
        Loaded += async (_, _) => await ScanAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenNodesFolder();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ScanAsync()
    {
        try
        {
            await _viewModel.ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "节点冲突扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
