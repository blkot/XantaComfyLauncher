namespace ComfyLauncher.Utils;

public sealed class AppPaths
{
    public string InstanceRoot { get; }
    public string ComfyDir => Path.Combine(InstanceRoot, "ComfyUI");
    public string MainScript => Path.Combine(ComfyDir, "main.py");
    public string RequirementsFile => Path.Combine(ComfyDir, "requirements.txt");
    public string PythonExe => Path.Combine(InstanceRoot, ".venv", "Scripts", "python.exe");
    public string LauncherDataDir => Path.Combine(InstanceRoot, ".launcher");
    public string ConfigFile => Path.Combine(LauncherDataDir, "launcher-config.json");
    public string CacheDir => Path.Combine(LauncherDataDir, "cache");
    public string SnapshotsDir => Path.Combine(LauncherDataDir, "snapshots");
    public string VersionStateFile => Path.Combine(LauncherDataDir, "version-state.json");
    public string TempDir => Path.Combine(InstanceRoot, ".tmp");
    public string ComfyTempDir => Path.Combine(TempDir, "comfyui");
    public string LogsDir => Path.Combine(InstanceRoot, "logs");

    private AppPaths(string instanceRoot)
    {
        InstanceRoot = Path.GetFullPath(instanceRoot);
    }

    public static AppPaths Discover(string[] args)
    {
        var overrideRoot = ReadOption(args, "--root");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Validate(Path.GetFullPath(overrideRoot));
        }

        var envRoot = Environment.GetEnvironmentVariable("COMFY_LAUNCHER_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            return Validate(Path.GetFullPath(envRoot));
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ComfyUI", "main.py")) &&
                    File.Exists(Path.Combine(dir.FullName, ".venv", "Scripts", "python.exe")))
                {
                    return new AppPaths(dir.FullName);
                }
            }
        }

        throw new DirectoryNotFoundException(
            "未找到 ComfyUI 实例目录。请把启动器放在实例目录内，或使用 --root <实例路径>。");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(LauncherDataDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(SnapshotsDir);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(ComfyTempDir);
        Directory.CreateDirectory(LogsDir);
    }

    public string GetLocalUrl(int port) => $"http://127.0.0.1:{port}";
    public string GetLanUrl(int port) => $"http://{GetLanIp()}:{port}";

    public string GetLanIp()
    {
        try
        {
            var candidates = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .Where(n => n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => new { n.NetworkInterfaceType, Address = a.Address.ToString() }))
                .Where(x => !x.Address.StartsWith("169.254.", StringComparison.Ordinal))
                .OrderBy(x => x.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or
                    System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? 0 : 1)
                .ToList();
            return candidates.FirstOrDefault()?.Address ?? "未检测到";
        }
        catch
        {
            return "未检测到";
        }
    }

    private static AppPaths Validate(string root)
    {
        if (!File.Exists(Path.Combine(root, "ComfyUI", "main.py")))
        {
            throw new DirectoryNotFoundException($"目录中找不到 ComfyUI/main.py：{root}");
        }
        if (!File.Exists(Path.Combine(root, ".venv", "Scripts", "python.exe")))
        {
            throw new DirectoryNotFoundException($"目录中找不到 .venv/Scripts/python.exe：{root}");
        }
        return new AppPaths(root);
    }

    public static string? ReadOption(string[] args, string name)
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
