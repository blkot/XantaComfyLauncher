using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class EnvironmentProbeService
{
    private readonly AppPaths _paths;
    private readonly GitService _git;

    public EnvironmentProbeService(AppPaths paths, GitService git)
    {
        _paths = paths;
        _git = git;
    }

    public async Task<EnvironmentInfo> ProbeAsync(CancellationToken ct = default)
    {
        var versions = await ProbePythonPackagesAsync(ct).ConfigureAwait(false);
        var tag = await _git.GetCurrentTagAsync(ct).ConfigureAwait(false);
        var commit = await _git.GetShortCommitAsync(ct).ConfigureAwait(false);
        var date = await _git.GetCommitDateAsync(ct).ConfigureAwait(false);
        var (gpuName, gpuMemory, driver) = await ProbeGpuAsync(ct).ConfigureAwait(false);

        return new EnvironmentInfo
        {
            ComfyVersion = string.IsNullOrWhiteSpace(tag) ? "detached" : tag,
            ComfyCommit = commit,
            ComfyCommitDate = date,
            PythonVersion = versions.Python,
            TorchVersion = versions.Torch,
            SageAttentionVersion = versions.Sage,
            TritonVersion = versions.Triton,
            GpuName = gpuName,
            GpuMemory = gpuMemory,
            DriverVersion = driver,
            LanIp = _paths.GetLanIp()
        };
    }

    public async Task<bool> IsPortFreeAsync(int port, CancellationToken ct = default)
    {
        try
        {
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var inUse = properties.GetActiveTcpListeners().Any(x => x.Port == port);
            if (inUse) return false;
            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }
        catch
        {
            return true;
        }
    }

    private async Task<(string Python, string Torch, string Sage, string Triton)> ProbePythonPackagesAsync(
        CancellationToken ct)
    {
        const string script = """
import importlib.metadata as md
import json
import sys

def version(name):
    try:
        return md.version(name)
    except Exception:
        return None

print(json.dumps({
    "python": sys.version.split()[0],
    "torch": version("torch"),
    "sageattention": version("sageattention"),
    "triton": version("triton-windows") or version("triton"),
}))
""";
        var result = await ProcessRunner.RunAsync(
            _paths.PythonExe,
            new[] { "-c", script },
            _paths.ComfyDir,
            InstanceEnvironment.Create(_paths),
            cancellationToken: ct).ConfigureAwait(false);

        if (!result.Success)
        {
            return ("未知", "未知", "未知", "未知");
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StandardOutput);
            var root = doc.RootElement;
            return (
                Read(root, "python"),
                Read(root, "torch"),
                Read(root, "sageattention"),
                Read(root, "triton"));
        }
        catch
        {
            return ("未知", "未知", "未知", "未知");
        }
    }

    private async Task<(string Name, string Memory, string Driver)> ProbeGpuAsync(CancellationToken ct)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "nvidia-smi",
                new[]
                {
                    "--query-gpu=name,memory.total,driver_version",
                    "--format=csv,noheader,nounits"
                },
                _paths.InstanceRoot,
                cancellationToken: ct).ConfigureAwait(false);
            if (!result.Success) return ("未检测到", "", "");
            var first = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first == null) return ("未检测到", "", "");
            var parts = first.Split(',', StringSplitOptions.TrimEntries);
            return (parts.ElementAtOrDefault(0) ?? "未知",
                parts.ElementAtOrDefault(1) is { } memory ? memory + " MB" : "",
                parts.ElementAtOrDefault(2) ?? "");
        }
        catch
        {
            return ("未检测到", "", "");
        }
    }

    private static string Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "未知"
            : "未安装";
}
