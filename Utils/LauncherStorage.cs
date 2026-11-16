namespace XantaComfyLauncher.Utils;

/// <summary>
/// Paths owned by the launcher itself. These must never live inside the
/// installation directory because that directory is replaced during updates.
/// </summary>
public static class LauncherStorage
{
    public const string ProductName = "XantaComfyLauncher";

    public static string DataDir
    {
        get
        {
            var overrideDir = ReadFirstEnvironmentVariable(
                "XANTA_LAUNCHER_DATA_DIR",
                "COMFY_LAUNCHER_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return Path.GetFullPath(overrideDir.Trim().Trim('"'));
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(AppContext.BaseDirectory, "data");
            }

            return Path.Combine(localAppData, ProductName);
        }
    }

    public static string ConfigFile
    {
        get
        {
            var overrideFile = ReadFirstEnvironmentVariable(
                "XANTA_LAUNCHER_CONFIG",
                "COMFY_LAUNCHER_CONFIG");
            return !string.IsNullOrWhiteSpace(overrideFile)
                ? Path.GetFullPath(overrideFile.Trim().Trim('"'))
                : Path.Combine(DataDir, "config.json");
        }
    }

    public static string LogsDir => Path.Combine(DataDir, "logs");
    public static string UpdateStateDir => Path.Combine(DataDir, "update-state");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(UpdateStateDir);
    }

    /// <summary>
    /// Old builds stored launcher-config.json next to the executable. Look in
    /// the usual development/portable locations and migrate it on first run.
    /// </summary>
    public static string? FindLegacyConfig()
    {
        var candidates = new List<string>();
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
                {
                    candidates.Add(Path.Combine(dir.FullName, "launcher-config.json"));
                    candidates.Add(Path.Combine(dir.FullName, "LauncherApp", "launcher-config.json"));
                    candidates.Add(Path.Combine(dir.FullName, "XantaComfyLauncherApp", "launcher-config.json"));
                }
            }
            catch
            {
                // Best-effort migration only.
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path =>
                !string.Equals(path, ConfigFile, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path));
    }

    private static string? ReadFirstEnvironmentVariable(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}
