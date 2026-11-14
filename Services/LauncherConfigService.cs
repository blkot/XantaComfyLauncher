using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public sealed class LauncherConfigService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LauncherConfigService(AppPaths paths) => _paths = paths;

    public LauncherConfig LoadOrCreate()
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.ConfigFile))
        {
            var defaults = new LauncherConfig();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_paths.ConfigFile);
            return JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions) ?? new LauncherConfig();
        }
        catch
        {
            var backup = _paths.ConfigFile + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(_paths.ConfigFile, backup, true); } catch { }
            var defaults = new LauncherConfig();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(LauncherConfig config)
    {
        _paths.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(_paths.ConfigFile, json);
    }
}
