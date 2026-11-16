namespace XantaComfyLauncher.Utils;

public sealed class AppPaths
{
    public string InstanceRoot { get; }
    public string ComfyDir => Path.Combine(InstanceRoot, "ComfyUI");
    public string MainScript => Path.Combine(ComfyDir, "main.py");
    public string RequirementsFile => Path.Combine(ComfyDir, "requirements.txt");
    public string PythonExe => Path.Combine(InstanceRoot, ".venv", "Scripts", "python.exe");
    public string LauncherDataDir => Path.Combine(InstanceRoot, ".launcher");
    public string CacheDir => Path.Combine(LauncherDataDir, "cache");
    public string SnapshotsDir => Path.Combine(LauncherDataDir, "snapshots");
    public string VersionStateFile => Path.Combine(LauncherDataDir, "version-state.json");
    public string CustomNodeManifestFile => Path.Combine(LauncherDataDir, "custom-nodes.json");
    public string TempDir => Path.Combine(InstanceRoot, ".tmp");
    public string ComfyTempDir => Path.Combine(TempDir, "comfyui");
    public string LogsDir => Path.Combine(InstanceRoot, "logs");

    public static string AppConfigFile => LauncherStorage.ConfigFile;

    private AppPaths(string instanceRoot)
    {
        InstanceRoot = Path.GetFullPath(instanceRoot);
    }

    public static bool IsInstanceRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            return File.Exists(Path.Combine(root, "ComfyUI", "main.py")) &&
                   File.Exists(Path.Combine(root, ".venv", "Scripts", "python.exe"));
        }
        catch
        {
            return false;
        }
    }

    public static bool TryCreate(string? root, out AppPaths? paths, out string error)
    {
        paths = null;
        error = "";
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "实例路径不能为空。";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(root.Trim().Trim('"'));
        }
        catch (Exception ex)
        {
            error = "实例路径无效：" + ex.Message;
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, "ComfyUI", "main.py")))
        {
            error = "目录中找不到 ComfyUI\\main.py。";
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, ".venv", "Scripts", "python.exe")))
        {
            error = "目录中找不到 .venv\\Scripts\\python.exe。";
            return false;
        }

        paths = new AppPaths(fullPath);
        return true;
    }

    public static AppPaths? Discover(string[] args)
    {
        var overrideRoot = ReadOption(args, "--root");
        if (!string.IsNullOrWhiteSpace(overrideRoot) && TryCreate(overrideRoot, out var overridePaths, out _))
        {
            return overridePaths;
        }

        var envRoot = Environment.GetEnvironmentVariable("COMFY_LAUNCHER_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && TryCreate(envRoot, out var envPaths, out _))
        {
            return envPaths;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                if (TryCreate(dir.FullName, out var paths, out _))
                {
                    return paths;
                }
            }
        }

        return null;
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
