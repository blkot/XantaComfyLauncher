using System.Diagnostics;
using System.Globalization;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class GpuMonitorService
{
    public async Task<GpuSnapshot> GetSnapshotAsync(int? comfyProcessPid, CancellationToken ct = default)
    {
        var snapshot = new GpuSnapshot();
        try
        {
            var main = await RunNvidiaSmiAsync(
                "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits",
                ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(main))
            {
                snapshot.Error = "nvidia-smi 未返回 GPU 信息";
                return snapshot;
            }

            var line = main.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (line == null)
            {
                snapshot.Error = "未检测到 NVIDIA GPU";
                return snapshot;
            }
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            snapshot.GpuName = parts.Length > 0 ? parts[0] : "NVIDIA GPU";
            snapshot.UtilizationPercent = ParseInt(parts.ElementAtOrDefault(1));
            snapshot.MemoryUsedMb = ParseInt(parts.ElementAtOrDefault(2));
            snapshot.MemoryTotalMb = ParseInt(parts.ElementAtOrDefault(3));
            snapshot.TemperatureC = ParseInt(parts.ElementAtOrDefault(4));
            if (snapshot.MemoryUsedMb is int used && snapshot.MemoryTotalMb is int total)
                snapshot.MemoryFreeMb = Math.Max(0, total - used);

            if (comfyProcessPid is int pid)
            {
                snapshot.ComfyProcessRunning = ProcessExists(pid);
                var apps = await RunNvidiaSmiAsync(
                    "--query-compute-apps=pid,used_gpu_memory --format=csv,noheader,nounits",
                    ct).ConfigureAwait(false);
                foreach (var appLine in apps.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cols = appLine.Split(',', StringSplitOptions.TrimEntries);
                    if (cols.Length < 2 || !int.TryParse(cols[0], out var appPid) || appPid != pid) continue;
                    snapshot.ComfyProcessMemoryMb = ParseInt(cols[1]) ?? 0;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            snapshot.Error = ex.Message;
            LauncherLog.Write("gpu", "nvidia-smi 监控失败：" + ex.Message);
        }
        return snapshot;
    }

    public async Task<bool> HasEnoughFreeMemoryAsync(
        int requiredMb,
        int? comfyPid,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(comfyPid, ct).ConfigureAwait(false);
        return snapshot.MemoryFreeMb is not int free || free >= requiredMb;
    }

    private static async Task<string> RunNvidiaSmiAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException("无法启动 nvidia-smi");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("nvidia-smi 查询超时");
        }
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "nvidia-smi 执行失败" : error.Trim());
        return output;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool ProcessExists(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }
}
