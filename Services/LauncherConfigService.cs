using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public sealed class LauncherConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ConfigFile => AppPaths.AppConfigFile;

    public LauncherConfig LoadOrCreate()
    {
        var dir = Path.GetDirectoryName(ConfigFile);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(ConfigFile))
        {
            var defaults = new LauncherConfig();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(ConfigFile);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions) ?? new LauncherConfig();
            if (!string.IsNullOrWhiteSpace(config.InstanceRoot))
            {
                config.InstanceRoot = config.InstanceRoot.Trim().Trim('"');
            }
            return config;
        }
        catch
        {
            var backup = ConfigFile + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(ConfigFile, backup, true); } catch { }
            var defaults = new LauncherConfig();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(LauncherConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigFile);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(ConfigFile, json);
    }
}
