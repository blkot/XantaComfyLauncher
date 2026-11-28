using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;
using Velopack;

#pragma warning disable WFO5001 // WinForms dark color mode is still marked experimental.

namespace XantaComfyLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // Velopack must get the first chance to handle install/update hooks.
            VelopackApp.Build().Run();
        }
        catch (Exception ex)
        {
            StartupLog("VelopackApp.Run failed: " + ex);
            return 1;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LauncherLog.Error("ui-unhandled", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                LauncherLog.Error("unhandled", exception);
            }
            else
            {
                LauncherLog.Write("unhandled", e.ExceptionObject?.ToString() ?? "unknown exception");
            }
        };
        LauncherLog.Write("startup", $"XantaComfyLauncher {Application.ProductVersion} starting; args={string.Join(" ", args)}; config={LauncherStorage.ConfigFile}");

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTestRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTestRunner.RunSmokeTestAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--nodes-check", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTestRunner.RunNodesCheckAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--nodes-update", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTestRunner.RunNodeUpdateAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--check-launcher-update", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--apply-launcher-update", StringComparer.OrdinalIgnoreCase))
        {
            return LauncherUpdateCliRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\XantaComfyLauncher",
            createdNew: out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "XantaComfyLauncher 已经在运行。",
                "XantaComfyLauncher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            var appearance = new LauncherConfigService().LoadOrCreate();
            ThemeManager.Initialize(appearance.UseDarkTheme);
            Application.SetColorMode(appearance.UseDarkTheme ? SystemColorMode.Dark : SystemColorMode.Classic);
        }
        catch
        {
            ThemeManager.Initialize(false);
            Application.SetColorMode(SystemColorMode.Classic);
        }
        var startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) ||
                             args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(startMinimized));
        return 0;
    }

    private static void StartupLog(string message) => LauncherLog.Write("startup", message);
}
