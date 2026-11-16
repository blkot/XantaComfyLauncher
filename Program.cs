using XantaComfyLauncher.Services;
using Velopack;

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
        Application.Run(new MainForm());
        return 0;
    }

    private static void StartupLog(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "XantaComfyLauncher");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent startup.
        }
    }
}
