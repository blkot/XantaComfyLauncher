using XantaComfyLauncher.Bootstrapper;

namespace XantaComfyLauncher.Bootstrapper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
        {
            var path = ReadOption(args, "--installto");
            if (string.IsNullOrWhiteSpace(path))
            {
                return 2;
            }

            return InstallerEngine.InstallAsync(path)
                .GetAwaiter()
                .GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm(ReadOption(args, "--installto")));
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
