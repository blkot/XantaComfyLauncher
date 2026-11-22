using System.Text.Json;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public static class LauncherUpdateCliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var report = new Dictionary<string, object?>();
        var exitCode = 1;
        var applyUpdate = args.Contains("--apply-launcher-update", StringComparer.OrdinalIgnoreCase);
        LauncherLog.Write("update-check", applyUpdate ? "apply update requested" : "check requested");
        try
        {
            var configService = new LauncherConfigService();
            var config = configService.LoadOrCreate();
            var service = new LauncherUpdateService(() => config);
            var result = await service.CheckForUpdatesAsync();
            report["config_file"] = configService.ConfigFile;
            report["channel"] = result.Channel;
            report["is_installed"] = result.IsInstalled;
            report["current_version"] = result.CurrentVersion;
            report["available_version"] = result.AvailableVersion;
            report["update_available"] = result.Update != null;
            report["apply_requested"] = applyUpdate;
            report["ok"] = true;
            exitCode = result.IsInstalled ? 0 : 2;
            LauncherLog.Write("update-check",
                $"current={result.CurrentVersion}; available={result.AvailableVersion ?? "-"}; installed={result.IsInstalled}");

            if (applyUpdate && result.Update != null)
            {
                report["download_started"] = true;
                WriteReport(report);
                await service.DownloadAsync(result.Update, null, CancellationToken.None);
                report["download_completed"] = true;
                WriteReport(report);
                service.ApplyAndRestart(result.Update, new[] { "--check-launcher-update" });
                return 0;
            }
        }
        catch (Exception ex)
        {
            report["ok"] = false;
            report["exception"] = ex.ToString();
            LauncherLog.Error("update-check", ex);
        }

        WriteReport(report);
        return exitCode;
    }

    private static void WriteReport(Dictionary<string, object?> report)
    {
        try
        {
            LauncherStorage.EnsureDirectories();
            var path = Path.Combine(LauncherStorage.DataDir, "update-check.json");
            report["report_file"] = path;
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best effort; the exit code still communicates the result.
        }
    }
}
