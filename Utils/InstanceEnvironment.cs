using System.Diagnostics;
using System.Collections.Generic;
using System.IO;

namespace XantaComfyLauncher.Utils;

public static class InstanceEnvironment
{
    public static Dictionary<string, string> Create(AppPaths paths)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEMP"] = paths.TempDir,
            ["TMP"] = paths.TempDir,
            ["HF_HOME"] = Path.Combine(paths.InstanceRoot, "cache", "huggingface"),
            ["TORCH_HOME"] = Path.Combine(paths.InstanceRoot, "cache", "torch"),
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUNBUFFERED"] = "1"
        };
        var pythonDir = Path.GetDirectoryName(paths.PythonExe);
        if (!string.IsNullOrWhiteSpace(pythonDir))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            env["PATH"] = pythonDir + ";" + Path.Combine(pythonDir, "Scripts") + ";" + currentPath;
        }
        return env;
    }

    public static void Apply(ProcessStartInfo startInfo, AppPaths paths)
    {
        foreach (var pair in Create(paths))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }
}
