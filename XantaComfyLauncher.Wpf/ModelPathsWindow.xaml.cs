using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using XantaComfyLauncher.Desktop.ViewModels;
using XantaComfyLauncher.Utils;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class ModelPathsWindow
{
    private readonly ModelPathsViewModel _viewModel;

    public ModelPathsWindow(AppPaths paths)
    {
        InitializeComponent();
        _viewModel = new ModelPathsViewModel(paths);
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Refresh();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _viewModel.Refresh();
    private void OpenButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenSelected();

    private void PathsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _viewModel.OpenSelected();

    private void SaveYamlButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "保存 extra_model_paths.yaml 吗？保存前会自动备份。",
                "保存 YAML", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _viewModel.SaveYaml();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存 YAML 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void JunctionButton_Click(object sender, RoutedEventArgs e)
    {
        var targetDialog = new OpenFolderDialog
        {
            Title = "选择真实模型目录（Junction 目标）",
            InitialDirectory = _viewModel.SelectedPath?.Path ?? ""
        };
        if (targetDialog.ShowDialog(this) != true) return;
        var parentDialog = new OpenFolderDialog
        {
            Title = "选择 Junction 链接所在目录",
            InitialDirectory = _viewModel.SelectedPath?.Path ?? ""
        };
        if (parentDialog.ShowDialog(this) != true) return;

        var linkPath = Path.Combine(parentDialog.FolderName, Path.GetFileName(targetDialog.FolderName) + "-link");
        if (MessageBox.Show(this,
                $"创建 Junction？\r\n\r\n链接：{linkPath}\r\n目标：{targetDialog.FolderName}",
                "创建 Junction", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _viewModel.CreateJunctionAsync(linkPath, targetDialog.FolderName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "创建 Junction 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceDialog = new OpenFolderDialog
        {
            Title = "选择要迁移的模型目录",
            InitialDirectory = _viewModel.SelectedPath?.Path ?? ""
        };
        if (sourceDialog.ShowDialog(this) != true) return;
        var destinationDialog = new OpenFolderDialog
        {
            Title = "选择目标目录（必须不存在或为空）"
        };
        if (destinationDialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this,
                $"迁移目录？\r\n\r\n源：{sourceDialog.FolderName}\r\n目标：{destinationDialog.FolderName}\r\n\r\n" +
                "同卷会移动；跨卷只复制，不删除源目录。",
                "迁移模型目录", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _viewModel.MigrateAsync(
                sourceDialog.FolderName,
                destinationDialog.FolderName,
                new Progress<string>(text => _viewModel.SetStatus(text)));
            MessageBox.Show(this, "迁移操作完成。", "迁移完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "迁移失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
