using System.Text.Json;
using System.Text.Json.Serialization;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class LauncherConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ConfigFile => LauncherStorage.ConfigFile;
    public string? LastLoadSource { get; private set; }

    public LauncherConfig LoadOrCreate()
    {
        LauncherStorage.EnsureDirectories();

        if (File.Exists(ConfigFile))
        {
            return LoadFrom(ConfigFile, isLegacy: false) ?? CreateAndSaveDefault();
        }

        var legacy = LauncherStorage.FindLegacyConfig();
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var migrated = LoadFrom(legacy, isLegacy: true);
            if (migrated != null)
            {
                LastLoadSource = legacy;
                Save(migrated);
                return migrated;
            }
        }

        return CreateAndSaveDefault();
    }

    public void Save(LauncherConfig config)
    {
        LauncherStorage.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        var tempFile = ConfigFile + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, ConfigFile, overwrite: true);
    }

    public string? BackupConfig(string reason = "manual")
    {
        try
        {
            LauncherStorage.EnsureDirectories();
            if (!File.Exists(ConfigFile)) return null;
            var safeReason = string.Concat(reason.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
            var backup = ConfigFile + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}-{safeReason}";
            File.Copy(ConfigFile, backup, overwrite: true);
            LauncherLog.Write("config", "已备份配置：" + backup);
            return backup;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("config", ex);
            return null;
        }
    }

    public string? RestoreLatestBackup()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFile);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
            var latest = Directory.GetFiles(dir, Path.GetFileName(ConfigFile) + ".bak-*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null) return null;
            File.Copy(latest, ConfigFile, overwrite: true);
            LauncherLog.Write("config", "已从备份恢复配置：" + latest);
            return latest;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("config", ex);
            return null;
        }
    }

    private LauncherConfig? LoadFrom(string path, bool isLegacy)
    {
        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions) ?? new LauncherConfig();
            if (!string.IsNullOrWhiteSpace(config.InstanceRoot))
            {
                config.InstanceRoot = config.InstanceRoot.Trim().Trim('"');
            }
            LastLoadSource = isLegacy ? path : ConfigFile;
            return config;
        }
        catch
        {
            if (isLegacy) return null;

            var backup = ConfigFile + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(ConfigFile, backup, overwrite: true); } catch { }
            if (RestoreLatestBackup() != null)
            {
                return LoadFrom(ConfigFile, isLegacy: false);
            }
            return null;
        }
    }

    private LauncherConfig CreateAndSaveDefault()
    {
        var defaults = new LauncherConfig();
        Save(defaults);
        LastLoadSource = ConfigFile;
        return defaults;
    }
}
