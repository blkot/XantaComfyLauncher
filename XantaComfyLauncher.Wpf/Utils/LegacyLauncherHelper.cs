using System.IO;

namespace XantaComfyLauncher.Desktop.Utils;

public static class LegacyLauncherHelper
{
    public static string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "XantaComfyLauncher.exe"),
            Path.Combine(AppContext.BaseDirectory, "legacy", "XantaComfyLauncher.Legacy.exe"),
            Path.Combine(AppContext.BaseDirectory, "XantaComfyLauncher.Legacy.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "net9.0-windows", "XantaComfyLauncher.exe")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
