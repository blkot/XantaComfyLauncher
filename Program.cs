using ComfyLauncher.Services;

namespace ComfyLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
