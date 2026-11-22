using System.IO;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace XantaComfyLauncher.Desktop;

public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown"));
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, true);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "xanta-wpf-crash.log");
            File.AppendAllText(path,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best effort.
        }
    }
}
