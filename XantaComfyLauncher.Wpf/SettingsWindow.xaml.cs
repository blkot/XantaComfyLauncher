using System.Windows;
using Microsoft.Win32;
using XantaComfyLauncher.Desktop.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace XantaComfyLauncher.Desktop;

public partial class SettingsWindow
{
    private readonly SettingsViewModel _viewModel = new();

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Load();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 ComfyUI 实例根目录",
            InitialDirectory = _viewModel.InstancePath
        };
        if (dialog.ShowDialog(this) == true)
            _viewModel.InstancePath = dialog.FolderName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Save();
            MessageBox.Show(this, "设置已保存。部分设置需要重启 WPF Preview 后生效。",
                "设置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存设置失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
