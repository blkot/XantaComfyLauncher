using System.Diagnostics;
using System.Reflection;

namespace XantaComfyLauncher.Bootstrapper;

internal static class InstallerEngine
{
    internal const string PayloadResourceName = "XantaComfyLauncher.SetupPayload.exe";

    public static string GetDefaultInstallPath()
    {
        var systemRoot = NormalizeRoot(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            if (string.Equals(NormalizeRoot(drive.RootDirectory.FullName), systemRoot,
                    StringComparison.OrdinalIgnoreCase)) continue;
            return Path.Combine(drive.RootDirectory.FullName, "Apps", "XantaComfyLauncher");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XantaComfyLauncher");
    }

    public static string NormalizeInstallPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("请选择安装目录。");

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(fullPath.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个子目录。");
        }

        return fullPath;
    }

    public static async Task<int> InstallAsync(string installPath, IProgress<string>? progress = null)
    {
        var target = NormalizeInstallPath(installPath);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        var tempDir = Path.Combine(Path.GetTempPath(), "XantaComfyLauncherSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var setupPath = Path.Combine(tempDir, "XantaComfyLauncher-Setup.exe");

        try
        {
            progress?.Report("正在准备安装包...");
            await using (var resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(PayloadResourceName)
                ?? throw new InvalidOperationException("安装器缺少内嵌的 Setup 程序。"))
            await using (var file = File.Create(setupPath))
            {
                await resource.CopyToAsync(file);
            }

            progress?.Report("正在安装到 " + target + " ...");
            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--silent");
            startInfo.ArgumentList.Add("--installto");
            startInfo.ArgumentList.Add(target);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动安装程序。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException(
                    $"安装程序返回代码 {process.ExitCode}。{Environment.NewLine}{detail}".Trim());
            }

            progress?.Report("安装完成。");
            return 0;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static string NormalizeRoot(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
