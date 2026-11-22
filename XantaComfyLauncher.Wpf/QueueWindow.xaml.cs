using System.Windows;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Desktop;

public partial class QueueWindow
{
    private readonly QueueViewModel _viewModel;

    public QueueWindow(AppPaths paths, int port)
    {
        InitializeComponent();
        _viewModel = new QueueViewModel(paths, port);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private async void InterruptButton_Click(object sender, RoutedEventArgs e) => await _viewModel.InterruptAsync();
    private async void ClearButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ClearPendingAsync();
    private async void DeleteButton_Click(object sender, RoutedEventArgs e) => await _viewModel.DeleteSelectedAsync();
}
