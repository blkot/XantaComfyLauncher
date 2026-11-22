using System.Text;

namespace XantaComfyLauncher.Utils;

public static class LauncherLog
{
    private const long MaxLogBytes = 10 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogFile => Path.Combine(LauncherStorage.LogsDir, "launcher.log");

    public static void Write(string category, string message)
    {
        try
        {
            LauncherStorage.EnsureDirectories();
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                var path = LogFile;
                if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                {
                    try { File.Move(path, path + ".1", overwrite: true); } catch { }
                }
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Logging must never break application behavior.
        }
    }

    public static void Error(string category, Exception exception) =>
        Write(category, exception.ToString());
}
